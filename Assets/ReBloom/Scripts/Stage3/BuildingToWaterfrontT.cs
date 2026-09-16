using UnityEngine;
using Fusion;
using System.Collections;

// Building -> Waterfront 전환 텔레포터
// 바닥에 깔린 Collider 영역에 두 플레이어 다 들어오면 됨
// delayBeforeTransition(기본 3초) 후에 화면 페이드아웃, WaterfrontToBuilding 텔레포터로 이동
// - 감지: TrainFloor와 동일하게 Host(StateAuthority)가 매 틱 각 플레이어 몸통 위치를
// 영역 AABB(+수직 허용치)로 검사
// - 페이드인: 씬 로드 시 ScreenFade가 자동으로 FadeIn

public class BuildingToWaterfrontT : NetworkBehaviour
{
    [Header("감지 영역")]
    [SerializeField] private Collider boardingZone;
    [SerializeField] private float verticalTolerance = 2f; // 수직 판정치
    [Header("이동 위치")]
    [SerializeField] private Transform waterfrontSpawnPoint;
    [Header("타이밍(초)")]
    [SerializeField] private float delayBeforeTransition = 3f; // 딜레이 시간
    [SerializeField] private float fadeDuration = 1f; // 페이드아웃 시간
    [Header("유리문")]
    [SerializeField] private Transform glassDoor; // 유리문
    [SerializeField] private float doorRotateAngle = 100f; // 각도
    [SerializeField] private float doorRotateDuration = 1.5f;


    [Networked] private NetworkBool Player1On { get; set; }
    [Networked] private NetworkBool Player2On { get; set; }
    [Networked] private NetworkBool IsActivated { get; set; }
    private ScreenFade screenFade;
    // Host 전용 상태
    private int reportsReceived;
    private bool loadTriggered;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        ResolveBoardingZone();
    }

    private void ResolveBoardingZone()
    {
        if (boardingZone != null)
            return;

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
        if (!HasStateAuthority)
            return;

        UpdateBoardingState();

        if (Player1On && Player2On && !IsActivated)
        {
            IsActivated = true;
            StartCoroutine(BeginTeleportAfterDelay());
        }
    }

    private void UpdateBoardingState()
    {
        bool p1 = false;
        bool p2 = false;

        NetworkPlayer[] players =
            FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);

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

            if (id == 1)
                p1 = true;
            else if (id == 2)
                p2 = true;
        }

        Player1On = p1;
        Player2On = p2;
    }

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

    private IEnumerator BeginTeleportAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforeTransition);

        RPC_BeginTeleport();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_BeginTeleport()
    {
        StartCoroutine(TeleportRoutine());
    }

    private IEnumerator TeleportRoutine()
    {
        if (screenFade == null)
            screenFade = FindFirstObjectByType<ScreenFade>();

        // 1. 유리문 닫기
        yield return StartCoroutine(RotateDoor());

        // 2. 페이드아웃
        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeOut(fadeDuration)
            );
        }

        // 3. 두 플레이어 이동
        if (HasStateAuthority)
        {
            TeleportPlayers();
        }

        // 4. 모든 피어에서 페이드인
        if (screenFade != null)
        {
            yield return StartCoroutine(
                screenFade.FadeIn(fadeDuration)
            );
        }

        // 5. 다음 이동을 다시 사용할 수 있도록 초기화
        if (HasStateAuthority)
        {
            IsActivated = false;
        }
    }

    private void TeleportPlayers()
    {
        if (waterfrontSpawnPoint == null) { return; }
        RPC_TeleportPlayers(waterfrontSpawnPoint.position, waterfrontSpawnPoint.rotation);
    }

    private IEnumerator RotateDoor()
    {
        if (glassDoor == null)
            yield break;

        Quaternion startRotation = glassDoor.rotation;
        Quaternion targetRotation = startRotation * Quaternion.Euler(0f, doorRotateAngle, 0f);

        float elapsed = 0f;

        while (elapsed < doorRotateDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / doorRotateDuration);
            // 부드럽게 회전
            t = Mathf.SmoothStep(0f, 1f, t); 

            glassDoor.rotation = Quaternion.Slerp(startRotation, targetRotation, t);

            yield return null;
        }
        glassDoor.rotation = targetRotation;
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_TeleportPlayers(Vector3 position, Quaternion rotation)
    {
        NetworkPlayer local = NetworkPlayer.LocalInstance;
        if (local == null || local.HardwareRig == null) return;
        
        local.HardwareRig.transform.SetPositionAndRotation(position, rotation);
    }
}
