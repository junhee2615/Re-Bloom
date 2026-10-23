using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ReBloom.Electricity;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Events;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Stage3 엔딩. 엔딩 코어의 나무가 다 자라고 <see cref="delayAfterTreeGrown"/>초 뒤에 시작한다.
///
///  1. 온실 문 앞의 두 캐릭터(mental, ear)가 내려 둔 손을 서서히 올려 손바닥을 맞댄다.
///  2. 손바닥이 닿는 순간 빛이 터지고 안개가 걷힌다. 손을 내린다.
///  3. 두 사람이 온실 밖으로 걸어 나와 멈춰 선다.
///  4. 두 사람의 뒤, 눈높이에서 Flower&amp;Tree&amp;Building&amp;Ivy와 식생이 자라는 것을 바라본 뒤 페이드 아웃.
///
/// 순서와 타이밍은 전부 <see cref="director"/>의 타임라인에 있다. 카메라는 Cinemachine 트랙,
/// 나머지는 <see cref="EndingActionTrack"/> 클립이 <see cref="ApplyAction"/>을 부른다.
///
/// 캐릭터 몸은 애니메이터(Idle / Walk, IsWalking)가 움직이고, 손을 올리는 팔만 IK가 덮어쓴다.
/// IK는 평소 꺼 두므로(가중치 0) 두 손 모두 Idle 포즈대로 다리 옆에 내려가 있다.
///
/// VR이라 Cinemachine이 HMD 카메라를 직접 잡지 않는다. 전용 Brain(<see cref="cutsceneBrain"/>)이
/// 카메라 없는 빈 오브젝트를 움직이고, 그 위치를 따라 XR Origin을 옮긴다(Stage1·Stage3 물길 컷씬과 같은 방식).
/// 각 플레이어가 자기 화면에서 따로 재생한다(네트워크 전송 없음).
///
/// 씬 구성은 메뉴 Re_Bloom/Stage3/Ending Cutscene Setup - Apply 가 만든다.
/// </summary>
public class Stage3EndingCutscene : MonoBehaviour
{
    [Serializable]
    public class EndingActor
    {
        [Tooltip("컷씬용 캐릭터. 시작 전에는 꺼 둔다.")]
        public GameObject root;

        [Tooltip("손을 올리는 팔의 IK. 타깃을 움직여 손바닥을 맞댄다.")]
        public TwoBoneIKConstraint reachIK;

        [Tooltip("손바닥이 닿았을 때의 손 위치·방향. 씬에서 직접 돌려 맞출 수 있다.")]
        public Transform meetPose;

        [Tooltip("온실 밖으로 걸어 나와 멈출 자리·방향.")]
        public Transform walkTarget;

        [NonSerialized] public Animator animator;
        [NonSerialized] public IRigConstraint[] constraints;
        [NonSerialized] public Vector3 restPosition;
        [NonSerialized] public Quaternion restRotation;
        [NonSerialized] public bool restCaptured;
        [NonSerialized] public Vector3 walkStartPosition;
        [NonSerialized] public Quaternion walkStartRotation;
        [NonSerialized] public bool walkCaptured;

        // 엔딩을 시작했던 자리(온실 문 앞). 크레딧의 손 흔들기 컷에서 이 자리로 돌아온다.
        [NonSerialized] public Vector3 homePosition;
        [NonSerialized] public Quaternion homeRotation;

        // 발소리: 걷는 중인가, 두 발 뼈, 발마다 땅에 닿았는지 보는 상태, 소리 낼 곳
        [NonSerialized] public bool walking;
        [NonSerialized] public FootTracker[] feet;
        [NonSerialized] public AudioSource stepSource;
    }

    // 발 하나가 땅에 닿는 순간을 찾는다. 걷는 동안 발 높이(캐릭터 발밑 기준)의 최저·최고를 익히고,
    // 내려오던 발이 최저점 근처에서 다시 올라가기 시작하면 "디딤"으로 본다.
    public class FootTracker
    {
        public Transform bone;
        public float previous;
        public bool lifted;
        public float min = float.MaxValue;
        public float max = float.MinValue;
        public float lastStep = -1f;
        public int frames;
    }

    private static readonly int IsWalkingId = Animator.StringToHash("IsWalking");

    [Header("시작")]
    [Tooltip("엔딩 코어 나무가 다 자란 뒤 엔딩까지의 뜸(초).")]
    [SerializeField, Min(0f)] private float delayAfterTreeGrown = 5f;

    [Tooltip("나무 연출이 끝나지 않을 때(EndingCore가 꺼져 있는 등) 전기 미션 클리어 후 이 시간(초)이 지나면 그냥 시작한다. 0이면 쓰지 않는다.")]
    [SerializeField, Min(0f)] private float fallbackDelayAfterClear = 20f;

    [Header("연결")]
    [SerializeField] private PlayableDirector director;

    [Tooltip("엔딩 전용 Brain. 카메라가 없는 빈 오브젝트에 붙어 있고, 그 오브젝트의 위치를 XR Origin이 따라간다.")]
    [SerializeField] private CinemachineBrain cutsceneBrain;

    [Tooltip("엔딩 카메라들의 부모. 시작 전에는 꺼 둔다 — 켜져 있으면 XR 카메라의 Brain이 잡아챈다.")]
    [SerializeField] private GameObject cameraRoot;

    [SerializeField] private VegetationGrowth vegetation;

    [Tooltip("엔딩이 검게 닫힌 뒤 이어서 나올 엔딩 크레딧. 비우면 검은 화면으로 끝난다.")]
    [SerializeField] private EndingCredits credits;

    [Header("캐릭터")]
    [SerializeField] private EndingActor[] actors = Array.Empty<EndingActor>();

    [Tooltip("엔딩 시작 때 두 캐릭터 사이를 이만큼(m) 좁힌다. 각자 절반씩 다가선다. " +
             "가운데에서 손을 맞대도 팔을 끝까지 뻗지 않아 팔꿈치가 자연스럽게 굽는다.")]
    [SerializeField, Min(0f)] private float closeGap = 0.1f;

    [Tooltip("팔꿈치가 굽는 방향. 어깨와 손의 중간에서 아래로 이만큼(m) 내려간 곳을 향한다.")]
    [SerializeField, Min(0f)] private float elbowDrop = 0.35f;

    [Tooltip("팔꿈치가 몸 바깥쪽으로 벌어지는 정도(m).")]
    [SerializeField, Min(0f)] private float elbowOut = 0.2f;

    [Tooltip("손을 올릴 때 상대 쪽으로 다가가기 시작하는 시점(HandReach 클립의 0~1). " +
             "클수록 손이 먼저 높이 올라온 뒤 마지막에 맞닿는다.")]
    [SerializeField, Range(0f, 0.8f)] private float reachCloseDelay = 0.3f;

    [Tooltip("손을 올릴 때 손목을 MeetPose 방향으로 돌리는 정도(0~1). 1이면 손바닥이 정확히 마주 본다. " +
             "낮추면 손목 비틀림은 줄지만 손바닥이 비스듬해져 손가락끼리 엇갈린다(깍지 낀 것처럼 보인다).")]
    [SerializeField, Range(0f, 1f)] private float handRotationWeight = 1f;

    [Tooltip("두 캐릭터 가운데의 이 높이(m)에 WalkCenter를 둔다. 걷는 동안 따라가는 카메라가 이 점을 본다.")]
    [SerializeField, Min(0f)] private float walkCenterHeight = 1.3f;

    [Tooltip("걸을 때 발을 지면 높이에 맞춘다(아래로 레이캐스트).")]
    [SerializeField] private bool snapToGround = true;

    [Header("걷기")]
    [Tooltip("두 캐릭터의 가운데를 따라다니는 빈 오브젝트. 따라가는 카메라(Cam_03_Follow)의 대상이다.")]
    [SerializeField] private Transform walkCenter;

    [Tooltip("걷기 시작할 때 진행 방향으로 몸을 돌리는 시간(초).")]
    [SerializeField, Min(0.05f)] private float turnToWalk = 0.7f;

    [Tooltip("멈춘 뒤 바라볼 방향(WalkTarget의 방향)으로 몸을 돌리는 시간(초).")]
    [SerializeField, Min(0.05f)] private float turnToView = 0.9f;

    [Header("발소리")]
    [Tooltip("걸을 때 쓸 발소리. 여러 걸음이 이어진 긴 클립이면 엔딩 시작 때 걸음 하나씩 잘라 쓴다(Grass Footstep Long).")]
    [SerializeField] private AudioClip footstepClip;

    [SerializeField, Range(0f, 1f)] private float footstepVolume = 0.7f;

    [Tooltip("0이면 화면 어디서든 같은 크기(2D), 1이면 캐릭터 위치에서 들린다(3D).")]
    [SerializeField, Range(0f, 1f)] private float footstepSpatialBlend = 0.5f;

    [Header("크레딧 손 흔들기 컷")]
    [Tooltip("크레딧 마지막에 온실 문 앞의 두 사람을 찍는 카메라 자리·방향. 비우면 이 컷이 빠진다.")]
    [SerializeField] private Transform waveCameraPose;

    [Tooltip("손을 좌우로 흔드는 빠르기(초당 왕복 횟수).")]
    [SerializeField, Min(0.1f)] private float waveSpeed = 1.4f;

    [Tooltip("팔꿈치를 축으로 아래팔이 좌우로 흔들리는 각도(도). 가운데에서 한쪽으로 이만큼 기운다.")]
    [SerializeField, Range(0f, 60f)] private float waveAngle = 25f;

    [Header("자라는 식생 그룹")]
    [Tooltip("엔딩에서 자라날 그룹(Flower&Tree&Building&Ivy). 바로 아래 자식들이 가까운 것부터 차례로 자란다. " +
             "엔딩이 시작될 때(검은 화면) 크기를 0으로 접어 둔다.")]
    [SerializeField] private GameObject growGroup;

    [Tooltip("자식 하나가 다 자라는 데 걸리는 시간(초).")]
    [SerializeField, Min(0.1f)] private float growEach = 3f;

    [Tooltip("다 자랄 때 목표 크기를 넘었다가 돌아오는 탄성. 0이면 튀지 않는다.")]
    [SerializeField, Min(0f)] private float growOvershoot = 1.2f;

    [Header("손의 빛")]
    [SerializeField] private Light glowLight;
    [SerializeField] private ParticleSystem glowParticles;
    [SerializeField, Min(0f)] private float glowPeakIntensity = 8f;
    [Tooltip("빛이 잦아든 뒤 남는 밝기.")]
    [SerializeField, Min(0f)] private float glowRemainIntensity = 1.5f;
    [Tooltip("HandGlow 클립에서 최고 밝기에 닿는 지점(0~1).")]
    [SerializeField, Range(0.01f, 1f)] private float glowPeakAt = 0.12f;

    [Header("안개")]
    [Tooltip("엔딩 시작 때 깔아 둘 안개 강도. Resonance 안개의 기본 강도에 곱한다.")]
    [SerializeField, Min(0f)] private float fogStrength = 1f;

    [Tooltip("손바닥이 닿아 빛이 터지는 순간 렌더러(PC_Renderer)의 Flat Kit Fog 피처를 꺼서 안개를 완전히 없앤다.")]
    [SerializeField] private bool disableFlatKitFogOnTouch = true;

    [Header("페이드")]
    [Tooltip("게임 화면에서 엔딩으로 넘어갈 때 검게 닫히는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeOutBeforeStart = 0.8f;
    [Tooltip("엔딩 첫 화면이 열리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeInAtStart = 1.2f;

    [Header("XR")]
    [SerializeField] private bool followYaw = true;
    [Tooltip("VR 멀미를 줄이려면 꺼 둔다. 끄면 위아래 시선은 플레이어가 직접 움직인다.")]
    [SerializeField] private bool followPitch;

    [Header("연출 중 화면 정리")]
    [SerializeField] private bool hideAvatars = true;
    [SerializeField] private bool lockLocomotion = true;
    [SerializeField] private bool hideTutorialCanvas = true;

    [Header("이벤트")]
    public UnityEvent onEndingStarted;
    [Tooltip("타임라인이 끝나 화면이 검게 닫힌 뒤. 크레딧·씬 전환을 여기에 연결한다.")]
    public UnityEvent onEndingFinished;

    [Header("Debug")]
    [SerializeField] private bool xrReady;
    [SerializeField] private bool isPlaying;

    // 셋업 메뉴가 쓰는 구성 버전. 엔딩 구성이 바뀌면 셋업이 카메라·타임라인을 다시 만든다.
    [SerializeField, HideInInspector] private int setupLayoutVersion;

    private Camera runtimeCamera;
    private Transform xrHead;
    private Transform xrOrigin;
    private ScreenFade screenFade;
    private CanvasGroup previewFade;
    private HardwareRig hardwareRig;

    private bool pendingPlay;
    private bool hasPlayed;
    private bool treeGrown;
    private bool glowStarted;
    private bool glowStopped;
    private bool vegetationStarted;

    // 자라는 그룹의 자식들. 가까운 것부터 정렬돼 있다.
    private readonly List<Transform> growItems = new List<Transform>();
    private readonly List<Vector3> growBaseScales = new List<Vector3>();

    private readonly ResonanceCutsceneOverride resonance = new ResonanceCutsceneOverride();

    public bool IsPlaying => isPlaying;

    private void Awake()
    {
        if (cameraRoot != null)
            cameraRoot.SetActive(false);

        foreach (EndingActor actor in actors)
            if (actor != null && actor.root != null)
                actor.root.SetActive(false);

        if (glowLight != null)
        {
            glowLight.intensity = 0f;
            glowLight.enabled = false;
        }

        if (glowParticles != null)
            glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        if (director != null)
            director.playOnAwake = false;
    }

    private void OnEnable()
    {
        PlantCoreTreeGrow.Grown += OnTreeGrown;
        ElectricityCoreMission.Cleared += OnMissionCleared;
        CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
    }

    private void OnDisable()
    {
        PlantCoreTreeGrow.Grown -= OnTreeGrown;
        ElectricityCoreMission.Cleared -= OnMissionCleared;
        CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
    }

    private IEnumerator Start()
    {
        // XR 카메라는 방 입장 후 생성되므로 기다린다
        while (runtimeCamera == null)
        {
            runtimeCamera = Camera.main;
            yield return null;
        }

        xrHead = runtimeCamera.transform;
        for (Transform current = xrHead; current != null; current = current.parent)
        {
            if (current.name.Contains("XR Origin"))
            {
                xrOrigin = current;
                break;
            }
        }

        if (xrOrigin == null)
        {
            Debug.LogError("[Stage3Ending] XR Origin을 찾지 못했습니다.", this);
            yield break;
        }

        screenFade = runtimeCamera.GetComponentInChildren<ScreenFade>(true);
        hardwareRig = FindFirstObjectByType<HardwareRig>();
        xrReady = true;

        if (pendingPlay)
        {
            pendingPlay = false;
            Play();
        }
    }

    // ── 시작 ────────────────────────────────────────────────────────────

    private void OnTreeGrown()
    {
        treeGrown = true;
        StartCoroutine(PlayAfter(delayAfterTreeGrown));
    }

    private void OnMissionCleared()
    {
        if (fallbackDelayAfterClear > 0f)
            StartCoroutine(FallbackAfterClear());
    }

    private IEnumerator FallbackAfterClear()
    {
        yield return new WaitForSeconds(fallbackDelayAfterClear);

        if (treeGrown || hasPlayed) yield break;

        Debug.LogWarning("[Stage3Ending] 엔딩 코어 나무 연출이 끝나지 않아 클리어 기준으로 엔딩을 시작합니다. " +
                         "EndingCore가 꺼져 있지 않은지 확인하세요.", this);
        Play();
    }

    private IEnumerator PlayAfter(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        Play();
    }

    /// <summary>엔딩을 시작한다. 한 번만 재생된다.</summary>
    public void Play()
    {
        if (hasPlayed || isPlaying)
            return;

        if (!xrReady)
        {
            pendingPlay = true;
            Debug.Log("[Stage3Ending] XR 카메라가 아직 없어 엔딩을 대기합니다. 방에 입장해 XR Origin이 생기면 시작합니다.", this);
            return;
        }

        if (director == null || cutsceneBrain == null)
        {
            Debug.LogError("[Stage3Ending] Director 또는 Cutscene Brain이 비어 있습니다. " +
                           "메뉴 Re_Bloom/Stage3/Ending Cutscene Setup - Apply 를 다시 실행하세요.", this);
            return;
        }

        hasPlayed = true;
        StartCoroutine(PlayRoutine());
    }

    private IEnumerator PlayRoutine()
    {
        isPlaying = true;

        if (lockLocomotion && hardwareRig != null)
            hardwareRig.SetLocomotionLocked(true);
        if (hideTutorialCanvas && hardwareRig != null)
            hardwareRig.SetTutorialCanvasVisible(false);

        // 1. 게임 화면 -> 검정
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeOutBeforeStart));

        // 2. 검은 화면에서 무대를 바꾼다
        if (hideAvatars)
            SetAvatarsVisible(false);

        resonance.FreezeForFogClear(fogStrength);

        // 엔딩부터는 역할별 감각 제약이 없다 — mental의 검은 블러, ear의 먹먹한 소리를 걷는다.
        // 다시 제약으로 돌아가지 않는다(엔딩은 크레딧으로 끝난다).
        resonance.ClearSensoryConstraints();

        // 엔딩부터는 미션 강조 Outline을 모두 끈다(강조 스크립트가 다시 켜도 Update에서 계속 끈다)
        outlinesOff = true;
        DisableOutlines();

        SuspendOtherBrains();
        CollapseGrowGroup();

        if (cameraRoot != null)
            cameraRoot.SetActive(true);

        foreach (EndingActor actor in actors)
        {
            if (actor?.root == null) continue;
            actor.homePosition = actor.root.transform.position;
            actor.homeRotation = actor.root.transform.rotation;
        }

        StepActorsCloser();

        foreach (EndingActor actor in actors)
            PrepareActor(actor);

        BuildStepSlices();
        foreach (EndingActor actor in actors)
            PrepareFootsteps(actor);

        UpdateWalkCenter();

        onEndingStarted?.Invoke();

        director.Play();

        // Brain이 첫 샷을 잡고 XR Origin이 따라갈 때까지 기다린 뒤 화면을 연다
        yield return null;
        yield return null;

        if (screenFade != null)
            StartCoroutine(screenFade.FadeIn(fadeInAtStart));

        // 3. 타임라인 끝까지. 마지막 FadeOut 클립이 화면을 닫는다.
        //    끝에서 멈춰 두는(Hold) 설정에서는 director.time이 duration에 정확히 닿지 않을 수 있어
        //    직접 잰 경과 시간으로도 끝을 판단한다.
        float elapsed = 0f;
        float length = (float)director.duration;
        while (director.state == PlayState.Playing
               && director.time < director.duration - 0.05
               && elapsed < length + 0.5f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetFadeAlpha(1f);

        isPlaying = false;
        onEndingFinished?.Invoke();

        // 4. 검은 화면에서 엔딩 크레딧으로. 크레딧이 카메라를 검은 배경 + 크레딧만 그리도록 바꾼 뒤
        //    검은 덮개를 걷는다 — 크레딧은 스스로 페이드인한다.
        Camera viewer = runtimeCamera != null ? runtimeCamera : cutsceneBrain.GetComponent<Camera>();
        if (credits == null)
        {
            Debug.Log("[Stage3Ending] 엔딩 끝. 연결된 크레딧이 없어 검은 화면으로 끝납니다.", this);
        }
        else if (viewer == null)
        {
            Debug.LogWarning("[Stage3Ending] 엔딩 끝. 화면을 그리는 카메라를 찾지 못해 크레딧을 띄우지 못했습니다.", this);
        }
        else
        {
            Debug.Log($"[Stage3Ending] 엔딩 끝 → 엔딩 크레딧 시작 (카메라: {viewer.name})", this);
            credits.Play(viewer);
            SetFadeAlpha(0f);
        }
    }

    // ── 타임라인 클립 ───────────────────────────────────────────────────

    /// <summary><see cref="EndingActionClip"/>이 매 프레임 진행도(0~1)와 클립 길이(초)를 넘긴다.</summary>
    public void ApplyAction(EndingAction action, float progress, float clipDuration)
    {
        // 편집 모드 타임라인 미리보기에서 씬 오브젝트를 건드려 저장되지 않도록 한다.
        if (!Application.isPlaying) return;

        // 타임라인은 끝에서 멈춘 채(Hold) 마지막 프레임을 계속 다시 재생한다. 엔딩이 끝난 뒤에는
        // 마지막 FadeOut 클립이 매 프레임 화면을 검게 덮어 크레딧을 가리므로 아무것도 하지 않는다.
        if (!isPlaying) return;

        switch (action)
        {
            case EndingAction.HandReach:
                ApplyHandReach(progress);
                break;

            case EndingAction.HandGlow:
                ApplyHandGlow(progress);
                break;

            case EndingAction.HandRelease:
                ApplyHandRelease(progress);
                break;

            case EndingAction.FogClear:
                resonance.SetFogAmount(1f - Mathf.SmoothStep(0f, 1f, progress));
                break;

            case EndingAction.Walk:
                ApplyWalk(progress, clipDuration);
                break;

            case EndingAction.GroupGrow:
                ApplyGroupGrow(progress, clipDuration);
                break;

            case EndingAction.VegetationGrow:
                if (!vegetationStarted && vegetation != null)
                {
                    vegetationStarted = true;
                    vegetation.Grow(clipDuration);
                }
                break;

            case EndingAction.FadeOut:
                SetFadeAlpha(Mathf.SmoothStep(0f, 1f, progress));
                break;
        }
    }

    // 다리 옆에 내려 둔 손(Idle 포즈)에서 MeetPose까지 서서히 올린다.
    // 높이는 먼저 올라오고(ease-out) 상대 쪽으로는 나중에 다가가서(늦게 시작하는 ease)
    // 손이 올라오다가 마지막에 손바닥이 닿는다. 같은 속도로 움직이면 손이 낮은 데서 먼저 만난 뒤
    // 함께 들려 올라가는 것처럼 보인다.
    private void ApplyHandReach(float progress)
    {
        float rise = 1f - (1f - progress) * (1f - progress);
        float close = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - reachCloseDelay) / (1f - reachCloseDelay)));
        float turn = Mathf.SmoothStep(0f, 1f, progress);

        foreach (EndingActor actor in actors)
        {
            if (actor == null || actor.reachIK == null || actor.meetPose == null) continue;

            Transform target = actor.reachIK.data.target;
            Transform tip = actor.reachIK.data.tip;
            if (target == null || tip == null) continue;

            // IK가 꺼져 있는 첫 프레임의 손 = 애니메이션이 내려 둔 손
            if (!actor.restCaptured)
            {
                actor.restPosition = tip.position;
                actor.restRotation = tip.rotation;
                actor.restCaptured = true;
            }

            Vector3 delta = actor.meetPose.position - actor.restPosition;
            Vector3 up = Vector3.Project(delta, Vector3.up);
            Vector3 across = delta - up;

            target.SetPositionAndRotation(
                actor.restPosition + up * rise + across * close,
                Quaternion.Slerp(actor.restRotation, actor.meetPose.rotation, turn));

            actor.reachIK.weight = 1f;
            actor.reachIK.data.targetRotationWeight = handRotationWeight;
            AimElbow(actor, target.position);
        }
    }

    // IK 가중치를 서서히 0으로 내려 손을 다시 애니메이션(Idle)에 맡긴다 — 손이 다리 옆으로 내려간다.
    private void ApplyHandRelease(float progress)
    {
        float weight = 1f - Mathf.SmoothStep(0f, 1f, progress);

        foreach (EndingActor actor in actors)
            if (actor?.reachIK != null)
                actor.reachIK.weight = weight;
    }

    // 팔꿈치 힌트를 어깨-손 중간의 아래·바깥쪽에 둔다. 힌트가 없거나 엉뚱한 곳에 있으면
    // IK가 팔을 일자로 펴거나 팔꿈치를 안쪽으로 꺾는다.
    private void AimElbow(EndingActor actor, Vector3 handPosition)
    {
        ref TwoBoneIKConstraintData data = ref actor.reachIK.data;
        if (data.hint == null || data.root == null) return;

        Vector3 shoulder = data.root.position;
        Vector3 outward = Vector3.ProjectOnPlane(shoulder - actor.root.transform.position, Vector3.up);
        outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.zero;

        data.hintWeight = 1f;
        data.hint.position = Vector3.Lerp(shoulder, handPosition, 0.5f)
                             + Vector3.down * elbowDrop
                             + outward * elbowOut;
    }

    // 진행 방향으로 돌아서서 걸어가 WalkTarget에 멈추고, WalkTarget이 보는 쪽으로 돌아선다.
    private void ApplyWalk(float progress, float duration)
    {
        float t = progress * duration;
        float moveFrom = turnToWalk * 0.5f;
        float moveTo = Mathf.Max(moveFrom + 0.1f, duration - turnToView * 0.6f);
        float u = Mathf.Clamp01((t - moveFrom) / (moveTo - moveFrom));
        bool walking = progress < 1f && u > 0f && u < 1f;

        foreach (EndingActor actor in actors)
        {
            if (actor == null || actor.root == null || actor.walkTarget == null) continue;

            Transform body = actor.root.transform;
            if (!actor.walkCaptured)
            {
                actor.walkStartPosition = body.position;
                actor.walkStartRotation = body.rotation;
                actor.walkCaptured = true;
            }

            Vector3 path = Vector3.ProjectOnPlane(actor.walkTarget.position - actor.walkStartPosition, Vector3.up);
            Quaternion pathRotation = path.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(path.normalized, Vector3.up)
                : actor.walkStartRotation;

            Quaternion rotation = t < turnToWalk
                ? Quaternion.Slerp(actor.walkStartRotation, pathRotation, Mathf.SmoothStep(0f, 1f, t / turnToWalk))
                : t > duration - turnToView
                    ? Quaternion.Slerp(pathRotation, actor.walkTarget.rotation,
                        Mathf.SmoothStep(0f, 1f, (t - (duration - turnToView)) / turnToView))
                    : pathRotation;

            Vector3 position = Vector3.Lerp(actor.walkStartPosition, actor.walkTarget.position, Mathf.SmoothStep(0f, 1f, u));
            if (snapToGround)
                position.y = GroundHeight(position);

            body.SetPositionAndRotation(position, rotation);

            if (actor.animator != null)
                actor.animator.SetBool(IsWalkingId, walking);

            actor.walking = walking;
        }

        UpdateWalkCenter();
    }

    // 지나는 길의 지면 높이. 못 찾으면 넘겨받은 높이 그대로.
    private static float GroundHeight(Vector3 around)
    {
        Vector3 from = around + Vector3.up * 1.5f;
        return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore)
            ? hit.point.y
            : around.y;
    }

    // 따라가는 카메라가 보는 점. 두 캐릭터 가운데, 가슴 높이.
    private void UpdateWalkCenter()
    {
        if (walkCenter == null) return;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (EndingActor actor in actors)
        {
            if (actor?.root == null) continue;
            sum += actor.root.transform.position;
            count++;
        }

        if (count > 0)
            walkCenter.position = sum / count + Vector3.up * walkCenterHeight;
    }

    // 가까운 것부터 차례로 돋아난다. 마지막 자식이 클립 끝에 다 자라도록 시작 시각을 나눈다.
    private void ApplyGroupGrow(float progress, float duration)
    {
        int count = growItems.Count;
        if (count == 0) return;

        float t = progress * duration;
        float each = Mathf.Min(growEach, duration);
        float spread = Mathf.Max(0f, duration - each);

        for (int i = 0; i < count; i++)
        {
            if (growItems[i] == null) continue;

            float start = count > 1 ? spread * i / (count - 1) : 0f;
            float p = progress >= 1f ? 1f : Mathf.Clamp01((t - start) / each);
            growItems[i].localScale = growBaseScales[i] * EaseOutBack(p, growOvershoot);
        }
    }

    private static float EaseOutBack(float t, float overshoot)
    {
        float x = t - 1f;
        return 1f + (overshoot + 1f) * x * x * x + overshoot * x * x;
    }

    // 엔딩용 위치로 두 사람을 마주 보는 방향으로 조금씩 당긴다. 화면이 검을 때 부른다.
    // 손이 만나는 지점(MeetPose)은 두 사람의 가운데라 그대로다.
    private void StepActorsCloser()
    {
        if (closeGap <= 0f || actors.Length < 2) return;

        Transform a = actors[0]?.root != null ? actors[0].root.transform : null;
        Transform b = actors[1]?.root != null ? actors[1].root.transform : null;
        if (a == null || b == null) return;

        Vector3 axis = Vector3.ProjectOnPlane(b.position - a.position, Vector3.up);
        float gap = axis.magnitude;
        if (gap < 0.0001f) return;

        float step = Mathf.Min(closeGap, gap * 0.5f) * 0.5f;
        axis /= gap;
        a.position += axis * step;
        b.position -= axis * step;
    }

    // 빠르게 최고 밝기로 터졌다가 천천히 잦아든다.
    private void ApplyHandGlow(float progress)
    {
        if (!glowStarted && progress > 0f)
        {
            glowStarted = true;

            if (glowLight != null)
                glowLight.enabled = true;
            if (glowParticles != null)
                glowParticles.Play(true);

            // 손바닥이 닿는 순간 렌더러(PC_Renderer)의 FlatKit 안개를 끈다. Play 종료 시 원래대로 돌아온다.
            if (disableFlatKitFogOnTouch)
                FogRendererFeatureSceneController.SetFogSuppressed(true);
        }

        if (glowLight != null)
        {
            glowLight.intensity = progress < glowPeakAt
                ? Mathf.Lerp(0f, glowPeakIntensity, progress / glowPeakAt)
                : Mathf.Lerp(glowPeakIntensity, glowRemainIntensity,
                    Mathf.SmoothStep(0f, 1f, (progress - glowPeakAt) / (1f - glowPeakAt)));
        }

        if (!glowStopped && progress >= 1f && glowParticles != null)
        {
            glowStopped = true;
            glowParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    // ── 무대 준비 ───────────────────────────────────────────────────────

    // 몸은 애니메이터(Idle)에 맡기고 모든 리그 제약을 끈다(가중치 0). 배치할 때 잡아 둔 포즈나
    // 컨트롤러를 따라가던 IK 타깃이 손을 엉뚱한 곳에 붙잡지 않도록 — 두 손 모두 다리 옆으로 내려간다.
    // 손을 올리는 팔의 IK만 HandReach가 다시 켠다.
    private static void PrepareActor(EndingActor actor)
    {
        if (actor == null || actor.root == null) return;

        actor.constraints = actor.root.GetComponentsInChildren<MonoBehaviour>(true).OfType<IRigConstraint>().ToArray();
        foreach (IRigConstraint constraint in actor.constraints)
            constraint.weight = 0f;

        // 팔꿈치 힌트가 위팔 뼈(arm_stretch)의 자식이면, 힌트를 옮길 때마다 IK가 위팔을 돌리고
        // 위팔을 따라 힌트가 다시 움직여 팔이 덜덜 떨린다. 뼈 밖(캐릭터 루트 아래)으로 빼 둔다.
        // IK 타깃처럼 Animator 바깥에 있어도 리그는 씬 Transform으로 읽는다.
        Transform hint = actor.reachIK != null ? actor.reachIK.data.hint : null;
        Transform upperArm = actor.reachIK != null ? actor.reachIK.data.root : null;
        if (hint != null && upperArm != null && hint.IsChildOf(upperArm))
            hint.SetParent(actor.root.transform, true);

        actor.animator = actor.root.GetComponentsInChildren<Animator>(true)
            .FirstOrDefault(a => a.runtimeAnimatorController != null);

        if (actor.animator == null)
            Debug.LogWarning($"[Stage3Ending] {actor.root.name}에 컨트롤러가 있는 Animator가 없습니다. 걷기 애니메이션이 나오지 않습니다.", actor.root);

        actor.root.SetActive(true);

        // 꺼진 Animator에 파라미터를 넣으면 경고가 나므로 켠 뒤에 넣는다
        if (actor.animator != null)
        {
            actor.animator.applyRootMotion = false;   // 위치는 Walk 클립이 옮긴다
            actor.animator.SetBool(IsWalkingId, false);
        }
    }

    // 자라는 그룹의 바로 아래 자식들을 0으로 접는다. 식생 매니저가 따로 키우는 것은 건드리지 않는다.
    private void CollapseGrowGroup()
    {
        growItems.Clear();
        growBaseScales.Clear();
        if (growGroup == null) return;

        var managed = new HashSet<Transform>(vegetation != null
            ? vegetation.vegetationList.Where(v => v != null)
            : Enumerable.Empty<Transform>());

        Vector3 viewer = actors.Where(a => a?.walkTarget != null).Select(a => a.walkTarget.position)
            .DefaultIfEmpty(growGroup.transform.position)
            .Aggregate(Vector3.zero, (sum, p) => sum + p) / Mathf.Max(1, actors.Count(a => a?.walkTarget != null));

        foreach (Transform child in growGroup.transform.Cast<Transform>()
                     .Where(c => !managed.Contains(c))
                     .OrderBy(c => (c.position - viewer).sqrMagnitude))
        {
            growItems.Add(child);
            growBaseScales.Add(child.localScale);
            child.localScale = Vector3.zero;
        }
    }

    // XR 카메라에도 Brain이 있다. 엔딩 카메라를 잡아채 HMD를 흔들지 않도록 꺼 둔다.
    // 엔딩은 검은 화면으로 끝나므로 되돌리지 않는다.
    private void SuspendOtherBrains()
    {
        foreach (CinemachineBrain brain in FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None))
            if (brain != cutsceneBrain)
                brain.enabled = false;
    }

    private void SetAvatarsVisible(bool visible)
    {
        foreach (NetworkPlayer player in NetworkPlayer.All)
            if (player != null)
                player.SetAvatarVisible(visible);
    }

    // ── 발소리 ──────────────────────────────────────────────────────────

#if UNITY_EDITOR
    private const string DefaultFootstepPath = "Assets/ReBloom/AssetStore/Rain Forest SFX/Footstep/Grass Footstep Long.wav";

    // 발소리 칸이 비어 있으면 기본 클립을 넣는다. 스크립트가 다시 컴파일될 때도 불리므로
    // 엔딩 셋업(Apply)을 다시 돌리지 않아도 연결된다(씬을 저장해야 남는다).
    private void OnValidate()
    {
        if (footstepClip != null || Application.isPlaying) return;

        footstepClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(DefaultFootstepPath);
        if (footstepClip != null)
            UnityEditor.EditorUtility.SetDirty(this);
    }
#endif

    // Assets/Resources/Ending/EndingFootsteps.wav (Grass Footstep Long과 같은 소리)
    private const string FallbackFootstepResource = "Ending/EndingFootsteps";

    private readonly List<AudioClip> stepSlices = new List<AudioClip>();
    private int nextStepSlice;
    private bool firstStepLogged;
    private bool footDiagnosisLogged;

    // 긴 발소리 클립을 걸음 하나씩 잘라 둔다. 소리가 갑자기 커지는 지점(걸음의 시작)을 찾아
    // 그 지점부터 다음 걸음 전까지(최대 0.45초)를 한 조각으로 만든다.
    private void BuildStepSlices()
    {
        if (stepSlices.Count > 0) return;

        // 씬에 클립이 저장돼 있지 않아도(셋업 전·저장 전) 발소리가 나도록 Resources에 둔 기본 클립을 쓴다
        if (footstepClip == null)
            footstepClip = Resources.Load<AudioClip>(FallbackFootstepResource);

        if (footstepClip == null)
        {
            Debug.LogWarning("[Stage3Ending] 발소리 클립(Footstep Clip)이 비어 있어 걸을 때 소리가 나지 않습니다.", this);
            return;
        }

        if (footstepClip.loadState != AudioDataLoadState.Loaded)
            footstepClip.LoadAudioData();

        int channels = footstepClip.channels;
        int frequency = footstepClip.frequency;
        var data = new float[footstepClip.samples * channels];
        if (!footstepClip.GetData(data, 0))
        {
            Debug.LogWarning($"[Stage3Ending] 발소리 {footstepClip.name}를 읽지 못했습니다(Load Type이 Decompress On Load인지 확인). 통째로 씁니다.", this);
            stepSlices.Add(footstepClip);
            return;
        }

        // 10ms 단위 소리 크기(모노로 합침)
        int window = Mathf.Max(1, frequency / 100);
        int windows = footstepClip.samples / window;
        var envelope = new float[windows];
        float peak = 0f;
        for (int w = 0; w < windows; w++)
        {
            float sum = 0f;
            for (int s = 0; s < window; s++)
            for (int c = 0; c < channels; c++)
            {
                float v = data[((w * window) + s) * channels + c];
                sum += v * v;
            }
            envelope[w] = Mathf.Sqrt(sum / (window * channels));
            peak = Mathf.Max(peak, envelope[w]);
        }

        // 걸음의 시작 찾기: 조용하다가(10% 아래) 크게(30% 위) 올라오는 곳, 걸음 사이는 최소 0.15초
        var onsets = new List<int>();
        bool loud = false;
        int minGap = Mathf.CeilToInt(0.15f * frequency / window);
        for (int w = 0; w < windows; w++)
        {
            if (!loud && envelope[w] > peak * 0.3f && (onsets.Count == 0 || w - onsets[onsets.Count - 1] >= minGap))
            {
                onsets.Add(Mathf.Max(0, w - 1));
                loud = true;
            }
            else if (loud && envelope[w] < peak * 0.1f)
            {
                loud = false;
            }
        }

        int maxLength = Mathf.CeilToInt(0.45f * frequency);
        for (int i = 0; i < onsets.Count && stepSlices.Count < 48; i++)
        {
            int start = onsets[i] * window;
            int end = i + 1 < onsets.Count ? onsets[i + 1] * window : footstepClip.samples;
            int length = Mathf.Min(end - start, maxLength);
            if (length < frequency / 20) continue;   // 50ms보다 짧은 조각은 버린다

            var slice = new float[length * channels];
            Array.Copy(data, start * channels, slice, 0, slice.Length);

            // 끝을 짧게 줄여 딸깍 소리가 나지 않게
            int fade = Mathf.Min(length, frequency / 100) * channels;
            for (int f = 0; f < fade; f++)
                slice[slice.Length - 1 - f] *= f / (float)fade;

            AudioClip clip = AudioClip.Create($"{footstepClip.name}_step{stepSlices.Count}", length, channels, frequency, false);
            clip.SetData(slice, 0);
            stepSlices.Add(clip);
        }

        if (stepSlices.Count == 0)
            stepSlices.Add(footstepClip);

        Debug.Log($"[Stage3Ending] 발소리 {footstepClip.name}에서 걸음 {stepSlices.Count}개를 잘라 냈습니다.", this);
    }

    // 발 뼈(foot.l / foot.r)와 소리 낼 AudioSource를 준비한다.
    private void PrepareFootsteps(EndingActor actor)
    {
        if (actor?.root == null || footstepClip == null) return;

        actor.feet = actor.root.GetComponentsInChildren<Transform>(true)
            .Where(t => t.name == "foot.l" || t.name == "foot.r")
            .Select(t => new FootTracker { bone = t })
            .ToArray();

        if (actor.feet.Length == 0)
            Debug.LogWarning($"[Stage3Ending] {actor.root.name}에서 발 뼈(foot.l / foot.r)를 찾지 못해 발소리가 나지 않습니다.", actor.root);

        actor.stepSource = actor.root.AddComponent<AudioSource>();
        actor.stepSource.playOnAwake = false;
        actor.stepSource.spatialBlend = footstepSpatialBlend;
        actor.stepSource.volume = footstepVolume;
        actor.stepSource.minDistance = 2f;
        actor.stepSource.maxDistance = 25f;
    }

    // 애니메이션이 발을 움직인 뒤(LateUpdate) 발이 땅에 닿는 순간마다 걸음 소리 한 조각을 튼다.
    private void LateUpdate()
    {
        if (!isPlaying || stepSlices.Count == 0) return;

        foreach (EndingActor actor in actors)
        {
            if (actor?.feet == null || actor.stepSource == null) continue;

            float ground = actor.root.transform.position.y;
            foreach (FootTracker foot in actor.feet)
            {
                float height = foot.bone.position.y - ground;

                if (!actor.walking)
                {
                    foot.frames = 0;
                    foot.lifted = false;
                    foot.min = float.MaxValue;
                    foot.max = float.MinValue;
                    continue;
                }

                foot.min = Mathf.Min(foot.min, height);
                foot.max = Mathf.Max(foot.max, height);
                foot.frames++;

                // 발이 충분히 들렸다가(폭의 50% 위) 다시 바닥 근처(25% 아래)로 내려오는 순간이 디딤.
                // 디딘 발은 땅에 붙어 높이가 거의 그대로라, "가장 낮은 점"을 찾는 방식은 놓치기 쉽다.
                float range = foot.max - foot.min;
                if (range > 0.03f)
                {
                    if (height > foot.min + range * 0.5f)
                        foot.lifted = true;

                    if (foot.lifted && height < foot.min + range * 0.25f && Time.time - foot.lastStep > 0.2f)
                    {
                        foot.lifted = false;
                        foot.lastStep = Time.time;
                        PlayStep(actor);
                    }
                }

                foot.previous = height;
            }

            // 걷기 시작하고 2초가 지나도 한 번도 안 울렸으면 원인을 알 수 있게 남긴다
            if (actor.walking && !firstStepLogged && !footDiagnosisLogged && actor.feet.Length > 0
                && actor.feet[0].frames > 0 && actor.feet[0].frames * Time.smoothDeltaTime > 2f)
            {
                footDiagnosisLogged = true;
                string ranges = string.Join(", ", actor.feet.Select(f => $"{f.bone.name} {f.min:F3}~{f.max:F3}m"));
                Debug.LogWarning($"[Stage3Ending] 걷는 중인데 발소리가 아직 한 번도 나지 않았습니다. 발 높이 범위: {ranges}", this);
            }
        }
    }

    private void PlayStep(EndingActor actor)
    {
        AudioClip clip = stepSlices[nextStepSlice];
        nextStepSlice = (nextStepSlice + 1) % stepSlices.Count;

        actor.stepSource.pitch = UnityEngine.Random.Range(0.94f, 1.06f);
        actor.stepSource.PlayOneShot(clip);

        if (!firstStepLogged)
        {
            firstStepLogged = true;
            Debug.Log($"[Stage3Ending] 첫 발소리 재생 ({actor.root.name})", this);
        }
    }

    // ── 크레딧 손 흔들기 컷 ─────────────────────────────────────────────

    private const int CreditsLayer = 30;   // EndingCredits가 크레딧 화면에 쓰는 레이어. 이 카메라에서는 뺀다.

    private Camera waveCamera;
    private RenderTexture waveTexture;
    private bool waving;
    private float waveTime;

    private bool outlinesOff;
    private float nextOutlineSweep;

    /// <summary>크레딧에 손 흔들기 컷을 넣을 수 있는가.</summary>
    public bool HasWaveShot => waveCameraPose != null && actors.Length > 0;

    /// <summary>
    /// 두 사람을 온실 문 앞(엔딩을 시작했던 자리)으로 돌려놓고, 카메라를 보며 손을 흔들게 한다.
    /// 그 모습을 실시간으로 그리는 텍스처를 돌려준다. 여러 번 불러도 같은 텍스처를 준다.
    /// </summary>
    public Texture BeginWaveShot()
    {
        if (waving) return waveTexture;
        if (!HasWaveShot) return null;

        waving = true;
        waveTime = 0f;

        Vector3 cameraPosition = waveCameraPose.position;
        foreach (EndingActor actor in actors)
        {
            if (actor?.root == null) continue;

            Vector3 toCamera = Vector3.ProjectOnPlane(cameraPosition - actor.homePosition, Vector3.up);
            Quaternion facing = toCamera.sqrMagnitude > 0.001f
                ? Quaternion.LookRotation(toCamera.normalized, Vector3.up)
                : actor.homeRotation;
            actor.root.transform.SetPositionAndRotation(actor.homePosition, facing);

            if (actor.animator != null)
                actor.animator.SetBool(IsWalkingId, false);
        }

        waveTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        waveTexture.Create();

        var cameraObject = new GameObject("CreditsWaveCamera");
        cameraObject.transform.SetParent(transform, false);
        cameraObject.transform.SetPositionAndRotation(waveCameraPose.position, waveCameraPose.rotation);
        waveCamera = cameraObject.AddComponent<Camera>();
        waveCamera.fieldOfView = 40f;
        waveCamera.targetTexture = waveTexture;
        waveCamera.allowMSAA = true;
        waveCamera.cullingMask = ~(1 << CreditsLayer);

        var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        cameraData.antialiasingQuality = AntialiasingQuality.High;

        return waveTexture;
    }

    /// <summary>손 흔들기를 멈추고 카메라를 정리한다.</summary>
    public void EndWaveShot()
    {
        if (!waving) return;
        waving = false;

        foreach (EndingActor actor in actors)
            if (actor?.reachIK != null)
                actor.reachIK.weight = 0f;

        if (waveCamera != null) Destroy(waveCamera.gameObject);
        waveCamera = null;
        if (waveTexture != null) waveTexture.Release();
        waveTexture = null;
    }

    private void Update()
    {
        if (waving)
            UpdateWave();

        if (outlinesOff && Time.unscaledTime >= nextOutlineSweep)
        {
            nextOutlineSweep = Time.unscaledTime + 0.5f;
            DisableOutlines();
        }
    }

    private static void DisableOutlines()
    {
        foreach (Outline outline in FindObjectsByType<Outline>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (outline.enabled)
                outline.enabled = false;
    }

    // 인사하듯 흔든다: 팔꿈치는 몸 바깥·조금 앞, 어깨보다 아래에 두고, 아래팔을 세운 채
    // 팔꿈치를 축으로 아래팔 전체를 카메라 기준 좌우 부채꼴로 흔든다. 손은 아래팔과 한 덩어리로 같이 기운다
    // (손목만 따로 까딱이면 손목만 흔들리는 것처럼 보인다). 손바닥은 카메라를 향한다.
    private void UpdateWave()
    {
        waveTime += Time.deltaTime;
        float angle = Mathf.Sin(waveTime * Mathf.PI * 2f * waveSpeed) * waveAngle;
        Vector3 cameraPosition = waveCameraPose.position;

        for (int i = 0; i < actors.Length; i++)
        {
            EndingActor actor = actors[i];
            if (actor?.root == null || actor.reachIK == null || actor.meetPose == null) continue;

            ref TwoBoneIKConstraintData data = ref actor.reachIK.data;
            Transform target = data.target;
            Transform shoulder = data.root;
            if (target == null || shoulder == null || data.mid == null || data.tip == null) continue;

            float upperArm = Vector3.Distance(shoulder.position, data.mid.position);
            float forearm = Vector3.Distance(data.mid.position, data.tip.position);

            // 방향은 캐릭터 몸 기준으로 잡는다. 루트가 몸 한가운데에 있지 않아서
            // "루트 → 어깨" 방향으로 바깥을 정하면 한쪽 팔이 안쪽·뒤쪽으로 꺾일 수 있다.
            Transform bodyTransform = actor.root.transform;
            Vector3 front = Vector3.ProjectOnPlane(bodyTransform.forward, Vector3.up).normalized;   // 카메라를 보고 서 있다
            Vector3 right = Vector3.Cross(Vector3.up, front);
            float sideSign = Vector3.Dot(shoulder.position - bodyTransform.position, right) >= 0f ? 1f : -1f;
            Vector3 outward = right * sideSign;
            Vector3 toCamera = front;

            // 팔꿈치: 어깨에서 위팔 길이만큼, 바깥·아래·조금 앞(몸보다 뒤로 가지 않게)
            Vector3 elbow = shoulder.position + (outward + Vector3.down * 0.45f + front * 0.35f).normalized * upperArm;

            // 아래팔: 위를 향한 채 살짝 앞으로 기울여(손이 얼굴보다 조금 앞에) 팔꿈치를 축으로 좌우로 흔든다.
            // 끝까지 펴지지 않게 조금 짧게 잡는다.
            Quaternion swing = Quaternion.AngleAxis(angle, front);
            Vector3 forearmDir = swing * (Vector3.up + front * 0.2f + outward * 0.1f).normalized;
            Vector3 hand = elbow + forearmDir * (forearm * 0.97f);

            // MeetPose는 손바닥이 상대를 보는 방향이다 — 그 방향을 카메라 쪽으로 돌리고, 아래팔과 같은 각도로 기울인다
            EndingActor partner = actors[(i + 1) % actors.Length];
            Vector3 towardPartner = partner?.root != null
                ? Vector3.ProjectOnPlane(partner.homePosition - actor.homePosition, Vector3.up).normalized
                : toCamera;
            Quaternion palmToCamera = Quaternion.FromToRotation(towardPartner, toCamera) * actor.meetPose.rotation;

            target.SetPositionAndRotation(hand, swing * palmToCamera);
            actor.reachIK.weight = 1f;

            // 힌트는 팔꿈치가 굽는 쪽(바깥·아래)에 두어 IK가 팔꿈치를 그 자리로 굽히게 한다
            if (data.hint != null)
            {
                data.hintWeight = 1f;
                data.hint.position = elbow + (outward + Vector3.down * 0.3f + front * 0.1f).normalized * 0.2f;
            }
        }
    }

    // ── XR 따라가기 ─────────────────────────────────────────────────────

    // 전용 Brain이 이번 프레임 카메라 위치를 정한 직후 불린다.
    // 엔딩이 끝나도 크레딧이 나오는 동안은 마지막 자리(5m 위 전경 카메라)에 계속 붙잡아 둔다 —
    // 놓으면 XR Origin이 땅으로 떨어져, 그 높이에 띄운 크레딧이 머리 한참 위에 남는다.
    private void OnCameraUpdated(CinemachineBrain brain)
    {
        bool holding = isPlaying || (credits != null && credits.IsPlaying);
        if (!holding || brain != cutsceneBrain || xrOrigin == null || xrHead == null)
            return;

        Transform shot = brain.ControlledObject.transform;

        if (followPitch)
        {
            xrOrigin.rotation = shot.rotation;
        }
        else if (followYaw)
        {
            Vector3 forward = Vector3.ProjectOnPlane(shot.forward, Vector3.up);
            if (forward.sqrMagnitude > 0.001f)
                xrOrigin.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        // 실제 HMD 위치를 컷씬 카메라 위치에 맞춘다
        xrOrigin.position += shot.position - xrHead.position;
    }

    // ── 테스트용 ────────────────────────────────────────────────────────

    [ContextMenu("테스트: 엔딩 바로 재생 (Play 모드)")]
    private void DebugPlay()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Stage3Ending] 엔딩은 Play 모드에서만 재생됩니다.", this);
            return;
        }

        if (!xrReady)
            StartPreviewWithoutXR();

        Play();
    }

    // 화면을 검게 덮는 정도(0~1). VR이면 XR 카메라의 ScreenFade, 미리보기면 화면 덮개를 쓴다.
    private void SetFadeAlpha(float alpha)
    {
        if (screenFade != null)
            screenFade.SetAlpha(alpha);
        else if (previewFade != null)
            previewFade.alpha = Mathf.Clamp01(alpha);
    }

    // 헤드셋 없이 씬을 바로 Play하면 XR Origin이 생기지 않아 엔딩이 영영 대기한다.
    // 테스트할 때만 Brain이 움직이는 오브젝트에 평면 카메라를 달아 Game 뷰로 보여 준다.
    private void StartPreviewWithoutXR()
    {
        if (cutsceneBrain == null) return;

        GameObject rig = cutsceneBrain.gameObject;
        if (!rig.TryGetComponent(out Camera previewCamera))
            previewCamera = rig.AddComponent<Camera>();

        previewCamera.depth = 100f;   // 씬에 다른 카메라가 있어도 위에 그린다

        // 소리를 듣는 AudioListener는 XR 카메라에 붙어 있다. 미리보기에는 없으므로 여기에 단다.
        if (FindFirstObjectByType<AudioListener>() == null)
            rig.AddComponent<AudioListener>();

        // Brain이 새로 붙은 카메라의 렌즈(FOV 등)까지 잡도록 대상을 다시 지정한다
        cutsceneBrain.ControlledObject = rig;

        CreatePreviewFade();

        xrReady = true;
        pendingPlay = false;

        Debug.LogWarning("[Stage3Ending] XR Origin이 없어 Game 뷰 미리보기로 재생합니다. " +
                         "헤드셋 위치 이동은 빠지고, 페이드는 화면 덮개로 대신합니다.", this);
    }

    // 미리보기용 검은 화면 덮개. VR의 ScreenFade 대신 마지막 페이드 아웃을 보여 준다.
    private void CreatePreviewFade()
    {
        if (previewFade != null) return;

        var canvasObject = new GameObject("EndingPreviewFade", typeof(Canvas), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        var black = new GameObject("Black", typeof(RectTransform), typeof(Image));
        black.transform.SetParent(canvasObject.transform, false);
        var rect = (RectTransform)black.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = black.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = false;

        previewFade = canvasObject.GetComponent<CanvasGroup>();
        previewFade.alpha = 0f;
        previewFade.blocksRaycasts = false;
        previewFade.interactable = false;
    }
}
