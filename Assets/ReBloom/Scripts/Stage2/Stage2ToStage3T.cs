using System.Collections;
using System.Linq;
using Fusion;
using UnityEngine;

/// <summary>
/// Stage2 → Stage3 전환 텔레포터.
/// 바닥에 깔린 트리거 영역(BoxCollider) 안에 두 플레이어가 모두 들어오면,
/// delayBeforeTransition(기본 3초) 뒤에 화면을 페이드아웃하고 Stage3 씬을 로드한다.
///
/// - 감지: TrainFloor와 동일하게 Host(StateAuthority)가 매 틱 각 플레이어 몸통 위치를
///   영역 AABB(+수직 허용치)로 검사한다. (트리거 이벤트가 아니라 영역 판정)
/// - 전환: CutscenePlayer의 축약판. 컷신/영상 없이 페이드아웃 → 씬 로드만 한다.
/// - 스폰 배치: 씬 로드 후 HardwareRig가 Stage3의 HostSpawnPoint/ClientSpawnPoint로
///   각 리그를 자동 이동시키므로 여기서 위치를 직접 옮기지 않는다.
/// - 페이드인: 씬 로드 시 ScreenFade가 자동으로 FadeIn 한다.
///
/// 부착 오브젝트는 NetworkObject여야 한다(씬 베이크). 감지 영역은 트리거 Collider를
/// 쓰거나 boardingZone에 직접 지정하면 된다.
/// </summary>
public class Stage2ToStage3T : NetworkBehaviour
{
    [Header("감지 영역")]
    [Tooltip("두 플레이어 탑승을 판정할 영역. 비우면 이 오브젝트의 트리거 Collider를 자동 사용한다.")]
    [SerializeField] private Collider boardingZone;
    [Tooltip("영역이 높이 0에 가까운 평면이라, 수직(Y)은 이 허용치로 판정한다.")]
    [SerializeField] private float verticalTolerance = 2f;

    [Header("타이밍(초)")]
    [Tooltip("두 플레이어가 모두 들어온 뒤 전환까지 대기")]
    [SerializeField] private float delayBeforeTransition = 3f;
    [Tooltip("페이드아웃 시간")]
    [SerializeField] private float fadeDuration = 1f;
    [Tooltip("클라이언트의 페이드 완료 보고를 기다리는 최대 시간(지연/이탈 대비)")]
    [SerializeField] private float clientWaitTimeout = 8f;

    [Header("씬")]
    [Tooltip("이동할 다음 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
    [SerializeField] private string nextSceneName = "Stage3";

    [Header("유리문")]
    [SerializeField] private Transform glassDoor; // 유리문
    [SerializeField] private float doorRotateAngle = 100f; // 각도
    [SerializeField] private float doorRotateDuration = 1.5f;
    [Header("사운드")]
    [Tooltip("텔레포트할 때 재생할 AudioSource. 비우면 이 오브젝트의 AudioSource 를 쓴다")]
    [SerializeField] private AudioSource teleportAudio;
    private Quaternion doorInitialLocalRotation; // 유리문의 원래 각도
    private bool doorInitialCaptured;

    [Networked] private NetworkBool Player1On { get; set; }
    [Networked] private NetworkBool Player2On { get; set; }
    [Networked] private NetworkBool IsUnlocked { get; set; }
    [Networked] private NetworkBool IsActivated { get; set; }

    private ScreenFade screenFade;

    // Host 전용 상태
    private int reportsReceived;
    private bool loadTriggered;

    private void Start()
    {
        ResolveBoardingZone();

        if (teleportAudio == null)
            teleportAudio = GetComponent<AudioSource>();
    }

    private void OnEnable()
    {
        Stage2TeleporterCutscene.CutsceneFinished += OnStage2CutsceneFinished;
    }

    private void CaptureDoorInitialRotation()
    {
        if (doorInitialCaptured || glassDoor == null)
            return;

        doorInitialLocalRotation = glassDoor.localRotation;
        doorInitialCaptured = true;
    }

    private void OnDisable()
    {
        Stage2TeleporterCutscene.CutsceneFinished -= OnStage2CutsceneFinished;
    }

    private void OnStage2CutsceneFinished()
    {
        if (HasStateAuthority)
            IsUnlocked = true;
    }

    private void ResolveBoardingZone()
    {
        if (boardingZone != null)
            return;

        // 바닥 물리 콜라이더가 아니라 트리거 영역을 우선 선택한다.
        foreach (Collider c in GetComponents<Collider>())
        {
            if (c.isTrigger)
            {
                boardingZone = c;
                return;
            }
        }

        boardingZone = GetComponent<Collider>();
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority || !IsUnlocked)
            return;

        UpdateBoardingState();

        if (Player1On && Player2On && !IsActivated)
        {
            IsActivated = true;
            StartCoroutine(BeginTransitionAfterDelay());
        }
    }

    // Host가 매 틱 각 플레이어 몸통 위치가 영역 안에 있는지 검사한다.
    private void UpdateBoardingState()
    {
        bool p1 = false;
        bool p2 = false;

        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (NetworkPlayer player in players)
        {
            if (player.Object == null)
                continue;

            Transform body = player.PlayerTransform != null
                ? player.PlayerTransform
                : player.transform;

            if (!IsInsideZone(body.position))
                continue;

            int id = player.Object.InputAuthority.PlayerId;
            if (id == 1) p1 = true;
            else if (id == 2) p2 = true;
        }

        Player1On = p1;
        Player2On = p2;
    }

    // 영역의 월드 AABB로 XZ를 판정하고 수직은 허용치로 처리한다.
    private bool IsInsideZone(Vector3 p)
    {
        if (boardingZone == null)
            return false;

        Bounds b = boardingZone.bounds;

        if (p.x < b.min.x || p.x > b.max.x)
            return false;

        if (p.z < b.min.z || p.z > b.max.z)
            return false;

        if (Mathf.Abs(p.y - b.center.y) > verticalTolerance)
            return false;

        return true;
    }

    // Host: 두 플레이어 감지 후 delayBeforeTransition 대기 → 모든 피어에 전환 시작 알림
    // (유리문 회전은 각 피어가 TransitionRoutine 에서 직접 재생한다. 문에 NetworkObject 는 필요 없다)
    private IEnumerator BeginTransitionAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeTransition);
        RPC_BeginTransition();
    }

    // 유리문 회전
    private IEnumerator RotateDoor()
    {
        if (glassDoor == null)
            yield break;

        // 항상 원래 각도에서 시작한다 (여러 번 써도 각도가 누적되지 않게)
        CaptureDoorInitialRotation();
        Quaternion startRotation = doorInitialLocalRotation;
        Quaternion targetRotation = startRotation * Quaternion.Euler(0f, doorRotateAngle, 0f);

        float elapsed = 0f;

        while (elapsed < doorRotateDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / doorRotateDuration);
            // 부드럽게 회전
            t = Mathf.SmoothStep(0f, 1f, t);

            glassDoor.localRotation = Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }
        glassDoor.localRotation = targetRotation;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BeginTransition()
    {
        StartCoroutine(TransitionRoutine());
    }

    // 각 피어: 유리문 닫기 → 텔레포트 소리 + 페이드아웃 → Host에 완료 보고
    private IEnumerator TransitionRoutine()
    {
        // 1. 유리문이 닫히는 것을 끝까지 보여 준다
        yield return StartCoroutine(RotateDoor());

        // 2. 텔레포트 소리
        if (teleportAudio != null)
            teleportAudio.Play();

        // 3. 페이드아웃
        if (screenFade == null)
            screenFade = FindFirstObjectByType<ScreenFade>();

        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));
        else
            Debug.LogWarning("[Stage2ToStage3T] ScreenFade를 찾지 못했습니다. 페이드 없이 진행합니다.");

        RPC_ReportFaded();

        // 클라이언트 지연/이탈 대비 워치독
        if (HasStateAuthority)
            StartCoroutine(LoadWatchdog());
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportFaded()
    {
        if (!HasStateAuthority)
            return;

        reportsReceived++;

        int expected = Runner != null ? Runner.ActivePlayers.Count() : reportsReceived;
        if (reportsReceived >= expected)
            TriggerLoad();
    }

    private IEnumerator LoadWatchdog()
    {
        yield return new WaitForSeconds(clientWaitTimeout);
        TriggerLoad();
    }

    // Host: Stage3 씬 로드 (스폰 배치/페이드인은 로드 후 자동 처리)
    private void TriggerLoad()
    {
        if (!HasStateAuthority || loadTriggered)
            return;

        SceneRef next = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetSceneRef(nextSceneName)
            : SceneRef.None;

        if (next == SceneRef.None)
        {
            Debug.LogError($"[Stage2ToStage3T] 씬 '{nextSceneName}'을 찾을 수 없습니다. Build Profiles > Scene List를 확인하세요.", this);
            return;
        }

        loadTriggered = true;
        Runner.LoadScene(next);
    }
}
