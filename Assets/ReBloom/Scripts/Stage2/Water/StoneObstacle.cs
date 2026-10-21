using Fusion;
using UnityEngine;

/// <summary>
/// 돌 : 서로 다른 2인이 함께 잡아야 움직이는 협동 장애물.
/// 두 손의 중점을 따라 이동하며, 놓으면 그 자리에서 중력으로 떨어진다.
/// 한 명만 잡으면(또는 한 사람이 양손으로 잡으면) 꿈쩍도 하지 않는다.
/// </summary>
public class StoneObstacle : WaterMissionObstacle
{
    private Vector3 offsetPos;

    // 서로 다른 두 플레이어가 잡아야만 움직인다.
    protected override bool HasEnoughGrabbers(int count)
        => GrabberPlayerA != PlayerRef.None
        && GrabberPlayerB != PlayerRef.None
        && GrabberPlayerA != GrabberPlayerB;

    protected override void OnHeldBegin(Hands h)
    {
        // 두 손 중점 기준 상대 위치 유지
        offsetPos = transform.position - h.Midpoint;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        pos = h.Midpoint + offsetPos;
        rot = originRotation; // 협동 운반은 회전 고정
    }

    // 놓을 때 그 자리에서 중력으로 떨어진다.
    protected override void OnReleased(Vector3 velocity) => base.OnReleased(Vector3.zero);

    // ── 연구 로그: 협동 잡기 대기 시간 ──
    // 아무도 잡지 않던 돌을 한 플레이어가 잡은 순간부터, 다른 플레이어가 함께 잡아
    // 돌이 움직이기 시작한 순간(HasEnoughGrabbers = true)까지의 시간을 잰다. 호스트에서만 동작.
    //  - StoneCoopGrab        | 돌 이름 | 걸린 시간(초) | 먼저 잡은 플레이어 역할
    //  - StoneCoopGrabInvalid | 돌 이름 | 잡고 있던 시간(초) | 먼저 잡은 플레이어 역할
    //    (다른 플레이어가 잡기 전에 모든 손을 놓아 무효가 된 경우. 측정값에서는 제외한다.)
    private int researchPrevGrabberCount;
    private bool researchWaiting;
    private float researchWaitStart;
    private string researchFirstRole;

    public override void FixedUpdateNetwork()
    {
        base.FixedUpdateNetwork();

        if (HasStateAuthority)
            TrackCoopGrabForResearch();
    }

    private void TrackCoopGrabForResearch()
    {
        int count = GrabberCount;
        bool moving = HasEnoughGrabbers(count);

        // 아무도 안 잡고 있다가 처음 잡혔다 → 대기 시작
        if (researchPrevGrabberCount == 0 && count > 0)
        {
            researchWaiting = true;
            researchWaitStart = Time.time;
            researchFirstRole = NetworkPlayer.TryGet(GrabberPlayerA, out NetworkPlayer first)
                ? first.AssignedRole.ToString()
                : string.Empty;
        }

        if (researchWaiting)
        {
            if (moving)
            {
                researchWaiting = false;
                ResearchLog.Event("StoneCoopGrab", name,
                    ResearchTracker.FormatSeconds(Time.time - researchWaitStart), researchFirstRole);
            }
            else if (count == 0)
            {
                researchWaiting = false;
                ResearchLog.Event("StoneCoopGrabInvalid", name,
                    ResearchTracker.FormatSeconds(Time.time - researchWaitStart), researchFirstRole);
            }
        }

        researchPrevGrabberCount = count;
    }
}
