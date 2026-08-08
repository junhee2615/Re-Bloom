using UnityEngine;

/// <summary>
/// 나무뿌리 : 한 손으로 잡고 당겨 빼낸 뒤 옮긴다.
/// 놓으면 그 자리에서 중력으로 떨어진다.
/// </summary>
public class RootObstacle : WaterMissionObstacle
{
    private Vector3 offsetPos;
    private Quaternion offsetRot;

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        offsetPos = Quaternion.Inverse(h.rotA) * (transform.position - h.posA);
        offsetRot = Quaternion.Inverse(h.rotA) * transform.rotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;
        pos = h.posA + h.rotA * offsetPos;
        rot = h.rotA * offsetRot;
    }

    // 놓을 때 그 자리에서 중력으로 떨어진다.
    protected override void OnReleased(Vector3 velocity) => base.OnReleased(Vector3.zero);
}
