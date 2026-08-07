using UnityEngine;

/// <summary>
/// 나무뿌리 : 한 손으로 잡고 "당겨 뽑는다".
/// 잡은 손을 따라오며, 원래 박혀 있던 위치에서 pullDistance 이상 멀어지면 뽑힌 것으로 판정해 치운다.
/// </summary>
public class RootObstacle : WaterMissionObstacle
{
    [Header("뿌리(당겨 뽑기) 설정")]
    [Tooltip("원래 위치에서 이만큼(m) 당겨 빼내면 '뽑혔다'고 판정")]
    [SerializeField] private float pullDistance = 0.35f;

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

    protected override void TryClearWhileHeld(Vector3 pos, Quaternion rot)
    {
        if (Vector3.Distance(pos, originPosition) >= pullDistance)
            ClearObstacle();
    }
}
