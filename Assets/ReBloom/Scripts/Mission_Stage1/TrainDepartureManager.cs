using System.Collections;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

/// <summary>
/// Stage1 → Stage2 출발 연출. 기존 CutscenePlayer(2D 영상 컷신)를 대체한다.
///
/// 두 플레이어가 TrainFloor에 모두 올라오면
/// 문 닫힘 → 배경이 -X 방향으로 흘러감 → 페이드아웃 → Stage2 로드

/// 핵심 설계: 열차와 플레이어는 월드 좌표에서 정지해 있고, 배경만 움직인다.
/// 배경 이동은 로컬에서 처리.
/// </summary>
[RequireComponent(typeof(TrainFloor))]
public class TrainDepartureManager : NetworkBehaviour
{
    [Header("이동시킬 배경 루트")]
    [Tooltip("열차가 달리는 것처럼 보이도록 반대 방향으로 흘려보낼 씬 루트들.")]
    [SerializeField]
    private Transform[] movingRoots;

    [Tooltip("배경이 흘러갈 방향. 기본값 -X는 열차가 +X로 달리는 것처럼 보이게 한다.")]
    [SerializeField]
    private Vector3 scrollDirection = new Vector3(-1f, 0f, 0f);

    [Header("주행")]
    [Tooltip("문이 완전히 닫힌 뒤 출발까지의 대기 시간(초).")]
    [SerializeField]
    private float delayAfterDoorClose = 1f;

    [Tooltip("최고 속도(m/s).")]
    [SerializeField]
    private float maxSpeed = 8f;

    [Tooltip("최고 속도에 도달하는 데 걸리는 시간(초). VR 멀미를 줄이려면 완만하게.")]
    [SerializeField]
    private float accelerationTime = 4f;

    [Tooltip("출발부터 페이드아웃 시작까지의 총 주행 시간(초).")]
    [SerializeField]
    private float travelDuration = 9f;

    [Header("페이드 / 씬")]
    [SerializeField]
    private float fadeDuration = 1.5f;

    [Tooltip("다음 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
    [SerializeField]
    private string nextSceneName = "Stage2";

    [Tooltip("클라이언트의 페이드 완료 보고를 기다리는 최대 시간(초).")]
    [SerializeField]
    private float clientWaitTimeout = 15f;

    [Header("사운드")]
    [SerializeField]
    private AudioSource departureAudioSource;

    [SerializeField]
    private AudioClip departureLoopClip;

    private TrainFloor trainFloor;
    private ScreenFade screenFade;

    // 각 피어 로컬 상태
    private bool departureStarted;
    private bool locomotionLocked;

    // 호스트 전용 상태
    private int reportsReceived;
    private bool loadTriggered;

    private void Awake()
    {
        trainFloor = GetComponent<TrainFloor>();
    }

    /// <summary>호스트가 두 플레이어의 탑승을 확인했을 때 호출한다.</summary>
    public void BeginDeparture()
    {
        if (!HasStateAuthority || departureStarted)
            return;

        reportsReceived = 0;
        loadTriggered = false;

        RPC_BeginDeparture();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BeginDeparture()
    {
        if (departureStarted)
            return;

        departureStarted = true;

        StartCoroutine(DepartureRoutine());
    }

    private IEnumerator DepartureRoutine()
    {
        // 달리는 열차 밖으로 이동/텔레포트하지 못하게 잠근다.
        SetLocomotionEnabled(false);

        // 1. 문 닫힘 (TrainFloor의 기존 연출을 재사용)
        if (trainFloor != null)
            yield return StartCoroutine(trainFloor.CloseDoorsRoutine());

        if (delayAfterDoorClose > 0f)
            yield return new WaitForSeconds(delayAfterDoorClose);

        // 2. 주행 사운드
        if (departureAudioSource != null &&
            departureLoopClip != null)
        {
            departureAudioSource.clip = departureLoopClip;
            departureAudioSource.loop = true;
            departureAudioSource.Play();
        }

        // 3. 배경 스크롤
        yield return StartCoroutine(ScrollWorldRoutine());

        // 4. 페이드아웃 (Stage2의 FadeIn은 씬 로드 시 ScreenFade가 자동 처리한다)
        if (screenFade == null)
            screenFade = FindFirstObjectByType<ScreenFade>();

        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeOut(fadeDuration));
        }
        else
        {
            Debug.LogWarning(
                "[TrainDepartureManager] ScreenFade를 찾지 못했습니다. 페이드 없이 진행합니다.",
                this);
        }

        // 5. 호스트에 완료 보고
        RPC_ReportReady();

        // 클라이언트 지연/이탈 대비 워치독
        if (HasStateAuthority)
            StartCoroutine(LoadWatchdog());
    }

    private IEnumerator ScrollWorldRoutine()
    {
        if (movingRoots == null ||
            movingRoots.Length == 0)
        {
            Debug.LogWarning(
                "[TrainDepartureManager] 이동시킬 배경 루트가 비어 있습니다.",
                this);

            yield return new WaitForSeconds(travelDuration);
            yield break;
        }

        Vector3 direction =
            scrollDirection.sqrMagnitude > 0.0001f
                ? scrollDirection.normalized
                : Vector3.left;

        float elapsed = 0f;

        while (elapsed < travelDuration)
        {
            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;

            // 완만한 가속 → 최고 속도 유지
            float speed =
                accelerationTime > 0f
                    ? maxSpeed * Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(elapsed / accelerationTime))
                    : maxSpeed;

            Vector3 offset = direction * speed * deltaTime;

            for (int i = 0; i < movingRoots.Length; i++)
            {
                if (movingRoots[i] != null)
                    movingRoots[i].position += offset;
            }

            yield return null;
        }
    }

    private void SetLocomotionEnabled(bool value)
    {
        HardwareRig rig = FindFirstObjectByType<HardwareRig>();

        if (rig == null)
            return;

        // 이동/회전/텔레포트만 잠그고 중력은 유지한다.
        foreach (LocomotionProvider provider in
                 rig.GetComponentsInChildren<LocomotionProvider>(true))
        {
            if (provider is GravityProvider)
                continue;

            provider.enabled = value;
        }

        // 텔레포트 레이 시각화도 함께 끈다.
        foreach (XRRayInteractor interactor in
                 rig.GetComponentsInChildren<XRRayInteractor>(true))
        {
            if (interactor.gameObject.name.Contains("Teleport"))
                interactor.gameObject.SetActive(value);
        }

        locomotionLocked = !value;
    }


    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportReady()
    {
        if (!HasStateAuthority)
            return;

        reportsReceived++;

        int expected =
            Runner != null
                ? Runner.ActivePlayers.Count()
                : reportsReceived;

        if (reportsReceived >= expected)
            TriggerLoad();
    }

    private IEnumerator LoadWatchdog()
    {
        yield return new WaitForSeconds(clientWaitTimeout);

        TriggerLoad();
    }

    private void TriggerLoad()
    {
        if (!HasStateAuthority || loadTriggered)
            return;

        // 빌드 인덱스는 Scene List에 씬을 추가하면 밀리므로 이름으로 찾는다.
        SceneRef next =
            NetworkManager.Instance != null
                ? NetworkManager.Instance.GetSceneRef(nextSceneName)
                : SceneRef.None;

        if (next == SceneRef.None)
        {
            Debug.LogError(
                $"[TrainDepartureManager] 씬 '{nextSceneName}'을 찾을 수 없습니다. Build Profiles > Scene List를 확인하세요.",
                this);

            return;
        }

        loadTriggered = true;

        Runner.LoadScene(next);
    }

    // XR 리그는 씬을 넘어가도 유지되므로,
    // Stage1이 언로드될 때 반드시 이동 잠금을 풀어준다.
    private void OnDestroy()
    {
        if (locomotionLocked)
            SetLocomotionEnabled(true);
    }
}
