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
}
