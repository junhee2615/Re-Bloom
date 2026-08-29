using Fusion;
using System.Collections;
using UnityEngine;

public class TrainFloor : NetworkBehaviour
{
    [Networked] private NetworkBool Player1On { get; set; }
    [Networked] private NetworkBool Player2On { get; set; }
    [Networked] private NetworkBool IsActivated { get; set; }

    // 플레이어 콜라이더 대신 이 영역으로 탑승을 판정한다.
    // 비어 있으면 이 오브젝트의 Trigger Collider(기존 탑승 감지 박스)를 자동으로 사용한다.
    [SerializeField] private Collider boardingZone;

    // 탑승 감지 박스가 높이 0의 평면이라, XZ 영역 + 이 수직 허용치로 판정한다.
    [SerializeField] private float verticalTolerance = 2f;

    public AudioSource audioSource;
    public AudioClip doorCloseClip;
    public Transform doorRight;
    public Transform doorLeft;
        private TrainDepartureManager departureManager;

    private void Start()
    {
                departureManager = GetComponent<TrainDepartureManager>();
        ResolveBoardingZone();
    }

    private void ResolveBoardingZone()
    {
        if (boardingZone != null)
            return;

        // MeshCollider(바닥 물리)가 아니라 Trigger 박스를 우선 선택한다.
        foreach (var c in GetComponents<Collider>())
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

            if (departureManager != null)
                departureManager.BeginDeparture();
            else
                Debug.LogError(
                    "[TrainFloor] TrainDepartureManager를 찾지 못해 Stage2로 넘어갈 수 없습니다.",
                    this);
        }
    }

    // Host(StateAuthority)가 매 틱 각 플레이어의 몸통 위치가
    // 탑승 구역 안에 있는지 검사해 Player1On / Player2On 을 갱신한다.
    private void UpdateBoardingState()
    {
        bool p1 = false;
        bool p2 = false;

        var players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        foreach (var player in players)
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

    // 탑승 박스의 월드 AABB로 XZ 영역을 판정하고, 수직은 허용치로 처리한다.
    // (탑승 구역은 회전 없이 바닥에 평평하게 놓여 있다는 전제)
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

    public IEnumerator CloseDoorsRoutine()
    {
        yield return new WaitForSeconds(3f);

        if (audioSource != null && doorCloseClip != null)
            audioSource.PlayOneShot(doorCloseClip);

        if (doorRight == null || doorLeft == null)
            yield break;

        Vector3 rightStart = doorRight.localPosition;
        Vector3 leftStart = doorLeft.localPosition;

        Vector3 rightTarget = rightStart + Vector3.left * 0.5f;
        Vector3 leftTarget = leftStart + Vector3.right * 0.5f;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime;

            doorRight.localPosition = Vector3.Lerp(rightStart, rightTarget, t);
            doorLeft.localPosition = Vector3.Lerp(leftStart, leftTarget, t);

            yield return null;
        }

        doorRight.localPosition = rightTarget;
        doorLeft.localPosition = leftTarget;
    }
}
