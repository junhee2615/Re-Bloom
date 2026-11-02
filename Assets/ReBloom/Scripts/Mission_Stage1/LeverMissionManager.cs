using Fusion;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class LeverMissionManager : NetworkBehaviour
{
    public static event Action MissionCleared;

    [Header("Lever")]
    public LeverSwitch leverA;
    public LeverSwitch leverB;

    [Header("Train Door")]
    public Transform doorRight;
    public Transform doorLeft;

    public AudioSource audioSource;
    public AudioClip doorOpenClip;

    [Header("Stage Clear")]
    public GameObject beforebuilding;
    public GameObject clearBuilding;
    public GameObject trainLight;

    [Header("Realtime VR Cutscene")]
    [SerializeField]
    private Stage1XRCutsceneRigFollower stage1Cutscene;

    [Networked]
    public NetworkBool IsMissionClear { get; set; }

    [Networked]
    public NetworkBool IsRestoreApplied { get; set; }

    [Networked]
    public NetworkBool IsDoorOpen { get; set; }

    [Networked]
    public NetworkBool IsCutscenePlaying { get; set; }

    private bool started;
    private bool playedDoorAnimation;

    // 이 클라이언트가 현재 컷씬 상태인지
    private bool localCutsceneRunning;

    // Host에서 받은 컷씬 종료 보고 수
    private int cutsceneReportsReceived;

    private bool subscribedToCutscene;

    [Header("Mission Sound")]
    public AudioSource missionAudioSourceA;
    public AudioSource missionAudioSourceB;

    public AudioSource electricLoopAudioSourceA;
    public AudioSource electricLoopAudioSourceB;

    public AudioClip missionSuccessClip;
    public AudioClip electricLoopClip;

    private bool playedMissionSuccessSound;

    [Header("Streetlight Flicker")]
    [Tooltip("레버 미션 클리어 전까지 씬의 가로등(이름이 이 글자로 시작하는 오브젝트)이 무작위로 깜빡인다. 클리어되면 계속 켜진다.")]
    public string streetlightNamePrefix = "Streetlight";
    [Tooltip("깜빡임 사이에 켜진 채로 있는 시간(초, 최소~최대).")]
    public Vector2 flickerSteadyTime = new Vector2(0.4f, 3f);
    [Tooltip("한 번 깜빡일 때 꺼졌다 켜지는 횟수(최소~최대).")]
    public Vector2Int flickerBurstCount = new Vector2Int(1, 4);
    [Tooltip("깜빡이는 동안 한 번 꺼지거나 켜져 있는 시간(초, 최소~최대).")]
    public Vector2 flickerBlinkTime = new Vector2(0.03f, 0.15f);
    [Tooltip("깜빡여 꺼졌을 때의 밝기(0이면 완전히 꺼짐).")]
    [Range(0f, 1f)] public float flickerOffLevel = 0.05f;
    [Tooltip("깜빡일 차례에 짧게 깜빡이는 대신 몇 초 동안 아예 꺼져 있을 확률.")]
    [Range(0f, 1f)] public float longOffChance = 0.3f;
    [Tooltip("아예 꺼져 있는 시간(초, 최소~최대). 다시 켜질 땐 몇 번 깜빡이며 켜진다.")]
    public Vector2 longOffTime = new Vector2(1f, 4f);

    private readonly List<Streetlight> streetlights = new List<Streetlight>();
    private bool streetlightsSteady;

    // =================================================
    // Fusion
    // =================================================

    private void Awake()
    {
        // 네트워크 Spawn 전에도 열차 내부 불이 켜져 보이지 않게, 복구 전 상태(꺼짐)로 시작한다.
        if (trainLight != null)
            trainLight.SetActive(false);

        CollectStreetlights();
    }

    public override void Spawned()
    {
        SubscribeCutsceneEvent();

        ApplyRestoreVisualState();
    }

    private void Start()
    {
        SubscribeCutsceneEvent();
    }

    // Fusion의 정식 정리 훅.
    // SimulationBehaviour가 OnDestroy를 이미 선언하고 있어서
    // OnDestroy를 직접 선언하면 Fusion의 정리가 호출되지 않는다.
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        UnsubscribeCutsceneEvent();
    }

    // =================================================
    // Cutscene Event
    // =================================================

    private void SubscribeCutsceneEvent()
    {
        if (subscribedToCutscene)
            return;

        if (stage1Cutscene == null)
            return;

        stage1Cutscene.CutsceneFinished +=
            OnLocalCutsceneFinished;

        subscribedToCutscene = true;
    }

    private void UnsubscribeCutsceneEvent()
    {
        if (!subscribedToCutscene)
            return;

        if (stage1Cutscene != null)
        {
            stage1Cutscene.CutsceneFinished -=
                OnLocalCutsceneFinished;
        }

        subscribedToCutscene = false;
    }

    // =================================================
    // Update
    // =================================================

    private void Update()
    {
        bool cleared = Object != null && Object.IsValid && IsMissionClear;
        UpdateStreetlights(cleared);

        if (Object == null || !Object.IsValid)
            return;

        // Timeline이 건물/열차 불을 직접 제어하는 동안에는
        // LeverMissionManager가 상태를 덮어쓰지 않는다.
        if (!localCutsceneRunning)
        {
            ApplyRestoreVisualState();
        }

        // Host가 복구 상태를 확정하면
        // 각 클라이언트에서도 최종 상태를 적용
        if (localCutsceneRunning &&
            IsRestoreApplied)
        {
            localCutsceneRunning = false;
            ApplyRestoreVisualState();
        }

        // 미션 성공 최초 1회
        if (IsMissionClear && !started)
        {
            started = true;

            MissionCleared?.Invoke();

            PlayMissionClearSounds();

            if (HasStateAuthority)
            {
                cutsceneReportsReceived = 0;

                IsCutscenePlaying = true;

                RPC_StartStage1Cutscene();
            }
        }

        // 문 열기는 모든 피어가 각자 재생한다 (문 Transform은 동기화되지 않으므로
        // Host만 움직이면 Client 화면에서는 문이 닫힌 채로 보인다)
        if (IsDoorOpen &&
            !playedDoorAnimation)
        {
            playedDoorAnimation = true;

            StartCoroutine(
                OpenDoorAnimation());
        }

        if (!HasStateAuthority)
            return;

        // 두 레버 모두 활성화
        if (!IsMissionClear &&
            leverA != null &&
            leverB != null &&
            leverA.isActivated &&
            leverB.isActivated)
        {
            IsMissionClear = true;
        }

    }

    // =================================================
    // Start Cutscene
    // =================================================

    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All)]
    private void RPC_StartStage1Cutscene()
    {
        localCutsceneRunning = true;

        if (stage1Cutscene == null)
        {
            Debug.LogError(
                "[LeverMissionManager] Stage1XRCutsceneRigFollower가 연결되지 않았습니다.",
                this);

            RPC_ReportCutsceneFinished();
            return;
        }

        if (!stage1Cutscene.IsReady)
        {
            Debug.LogWarning(
                "[LeverMissionManager] XR Cutscene Rig가 아직 준비되지 않았습니다.",
                this);

            RPC_ReportCutsceneFinished();
            return;
        }

        stage1Cutscene.PlayCutscene();
    }

    // =================================================
    // Local Cutscene Finished
    // =================================================

    private void OnLocalCutsceneFinished()
    {
        if (!localCutsceneRunning)
            return;

        RPC_ReportCutsceneFinished();
    }

    // =================================================
    // Host receives completion
    // =================================================

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority)]
    private void RPC_ReportCutsceneFinished()
    {
        if (!HasStateAuthority)
            return;

        cutsceneReportsReceived++;

        int expectedPlayers =
            Runner != null
                ? Runner.ActivePlayers.Count()
                : 1;

        if (cutsceneReportsReceived >=
            expectedPlayers)
        {
            FinishStage1Cutscene();
        }
    }

    private void FinishStage1Cutscene()
    {
        if (!HasStateAuthority)
            return;

        if (!IsCutscenePlaying)
            return;

        IsCutscenePlaying = false;

        ApplyRestoreAfterCutscene();
    }

    // =================================================
    // Final Restore
    // =================================================

    private void ApplyRestoreAfterCutscene()
    {
        if (!HasStateAuthority)
            return;

        if (IsRestoreApplied)
            return;

        IsRestoreApplied = true;

        StartCoroutine(
            OpenDoorAfterDelay());
    }

    private void ApplyRestoreVisualState()
    {
        if (beforebuilding != null)
        {
            beforebuilding.SetActive(
                !IsRestoreApplied);
        }

        if (clearBuilding != null)
        {
            clearBuilding.SetActive(
                IsRestoreApplied);
        }

        if (trainLight != null)
        {
            trainLight.SetActive(
                IsRestoreApplied);
        }
    }

    // =================================================
    // Mission Sound
    // =================================================

    private void PlayMissionClearSounds()
    {
        if (playedMissionSuccessSound)
            return;

        playedMissionSuccessSound = true;

        if (missionAudioSourceA != null &&
            missionSuccessClip != null)
        {
            missionAudioSourceA.PlayOneShot(
                missionSuccessClip);
        }

        if (missionAudioSourceB != null &&
            missionSuccessClip != null)
        {
            missionAudioSourceB.PlayOneShot(
                missionSuccessClip);
        }

        if (electricLoopAudioSourceA != null &&
            electricLoopClip != null)
        {
            electricLoopAudioSourceA.clip =
                electricLoopClip;

            electricLoopAudioSourceA.loop = true;
            electricLoopAudioSourceA.Play();
        }

        if (electricLoopAudioSourceB != null &&
            electricLoopClip != null)
        {
            electricLoopAudioSourceB.clip =
                electricLoopClip;

            electricLoopAudioSourceB.loop = true;
            electricLoopAudioSourceB.Play();
        }
    }

    // =================================================
    // Streetlight Flicker
    //
    // 가로등은 미션 클리어 전까지 각자 무작위로 깜빡인다(연출용이라 피어마다 따로 돈다).
    // 빛(Light)과 전구 발광(Emission)을 함께 줄였다 켠다. 씬 연결 없이 이름으로 찾는다.
    // =================================================

    private class Streetlight
    {
        public Light[] lights;
        public float[] baseIntensity;
        public List<(Renderer renderer, int index, Color color)> emissives;
        public float nextTime;
        public int blinksLeft;
        public bool on = true;
    }

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock propertyBlock;

    private void CollectStreetlights()
    {
        streetlights.Clear();
        if (string.IsNullOrEmpty(streetlightNamePrefix))
            return;

        var roots = new HashSet<Transform>();
        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (light.gameObject.scene != gameObject.scene)
                continue;

            for (Transform t = light.transform; t != null; t = t.parent)
            {
                if (t.name.StartsWith(streetlightNamePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    roots.Add(t);
                    break;
                }
            }
        }

        foreach (Transform root in roots)
        {
            var lamp = new Streetlight
            {
                lights = root.GetComponentsInChildren<Light>(true),
                emissives = new List<(Renderer, int, Color)>(),
            };
            lamp.baseIntensity = lamp.lights.Select(l => l.intensity).ToArray();

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material m = materials[i];
                    if (m == null || !m.HasProperty(EmissionColorId) || !m.IsKeywordEnabled("_EMISSION"))
                        continue;

                    Color c = m.GetColor(EmissionColorId);
                    if (c.maxColorComponent > 0f)
                        lamp.emissives.Add((renderer, i, c));
                }
            }

            lamp.nextTime = Time.time + UnityEngine.Random.Range(0f, flickerSteadyTime.y);
            streetlights.Add(lamp);
        }
    }

    private void UpdateStreetlights(bool cleared)
    {
        if (cleared)
        {
            // 클리어 후엔 한 번만 원래 밝기로 되돌리고 더는 건드리지 않는다
            if (!streetlightsSteady)
            {
                streetlightsSteady = true;
                foreach (Streetlight lamp in streetlights)
                    SetStreetlight(lamp, 1f);
            }
            return;
        }

        streetlightsSteady = false;
        float now = Time.time;

        foreach (Streetlight lamp in streetlights)
        {
            if (now < lamp.nextTime)
                continue;

            if (lamp.on && lamp.blinksLeft <= 0)
            {
                // 한 번에 한 가로등만 깜빡인다. 다른 가로등이 깜빡이거나 꺼져 있으면 잠시 미룬다.
                if (streetlights.Any(other => other != lamp && (other.blinksLeft > 0 || !other.on)))
                {
                    lamp.nextTime = now + UnityEngine.Random.Range(0.3f, 1.2f);
                    continue;
                }

                if (UnityEngine.Random.value < longOffChance)
                {
                    // 몇 초 동안 아예 꺼졌다가, 다시 켜질 땐 몇 번 깜빡이며 켜진다
                    lamp.on = false;
                    SetStreetlight(lamp, flickerOffLevel);
                    lamp.blinksLeft = UnityEngine.Random.Range(flickerBurstCount.x, flickerBurstCount.y + 1) * 2 + 1;
                    lamp.nextTime = now + UnityEngine.Random.Range(longOffTime.x, longOffTime.y);
                    continue;
                }

                // 켜진 채로 쉬다가 깜빡이기 시작
                lamp.blinksLeft = UnityEngine.Random.Range(flickerBurstCount.x, flickerBurstCount.y + 1) * 2;
            }

            if (lamp.blinksLeft > 0)
            {
                lamp.blinksLeft--;
                lamp.on = !lamp.on;
                SetStreetlight(lamp, lamp.on ? UnityEngine.Random.Range(0.7f, 1f) : flickerOffLevel);

                lamp.nextTime = now + (lamp.blinksLeft > 0 || !lamp.on
                    ? UnityEngine.Random.Range(flickerBlinkTime.x, flickerBlinkTime.y)
                    : UnityEngine.Random.Range(flickerSteadyTime.x, flickerSteadyTime.y));

                // 꺼진 채로 끝나지 않게 마지막은 항상 켜짐
                if (lamp.blinksLeft == 0 && !lamp.on)
                    lamp.blinksLeft = 1;
            }
        }
    }

    private void SetStreetlight(Streetlight lamp, float level)
    {
        for (int i = 0; i < lamp.lights.Length; i++)
            if (lamp.lights[i] != null)
                lamp.lights[i].intensity = lamp.baseIntensity[i] * level;

        propertyBlock ??= new MaterialPropertyBlock();
        foreach ((Renderer renderer, int index, Color color) in lamp.emissives)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock, index);
            propertyBlock.SetColor(EmissionColorId, color * level);
            renderer.SetPropertyBlock(propertyBlock, index);
        }
    }

    // =================================================
    // Train Door
    // =================================================

    private IEnumerator OpenDoorAfterDelay()
    {
        // 현재 코드에 있던 3초 유지
        yield return new WaitForSeconds(3f);

        if (!HasStateAuthority)
            yield break;

        IsDoorOpen = true;
    }

    private IEnumerator OpenDoorAnimation()
    {
        if (audioSource != null &&
            doorOpenClip != null)
        {
            audioSource.PlayOneShot(
                doorOpenClip);
        }

        if (doorRight == null ||
            doorLeft == null)
        {
            yield break;
        }

        Vector3 rightStart =
            doorRight.localPosition;

        Vector3 leftStart =
            doorLeft.localPosition;

        Vector3 rightTarget =
            rightStart +
            Vector3.right * 0.5f;

        Vector3 leftTarget =
            leftStart +
            Vector3.left * 0.5f;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime;

            doorRight.localPosition =
                Vector3.Lerp(
                    rightStart,
                    rightTarget,
                    t);

            doorLeft.localPosition =
                Vector3.Lerp(
                    leftStart,
                    leftTarget,
                    t);

            yield return null;
        }

        doorRight.localPosition =
            rightTarget;

        doorLeft.localPosition =
            leftTarget;
    }

    // =================================================
    // Static Reset
    // =================================================

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        MissionCleared = null;
    }
}