using UnityEngine;

/// <summary>
/// 흙 : 손으로 잡아 던져낸다.
/// 잡은 손을 따라오다가, 놓는 순간 손 속도가 임계값 이상이면 던져진 것으로 판정한다.
/// </summary>
public class SoilObstacle : WaterMissionObstacle
{
    [Header("던지기 설정")]
    [Tooltip("이 속도(m/s) 이상으로 손을 휘두르며 놓으면 '던졌다'고 판정")]
    [SerializeField] private float throwSpeed = 1.5f;

    private Vector3 offsetPos;
    private Quaternion offsetRot;

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        // 잡은 순간의 손 기준 상대 포즈를 유지
        // 손이 어디를 보든 상관없는 손 자신의 방향 기준 값으로 바꿔 저장
        offsetPos = Quaternion.Inverse(h.rotA) * (transform.position - h.posA);
        offsetRot = Quaternion.Inverse(h.rotA) * transform.rotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;
        pos = h.posA + h.rotA * offsetPos;
        rot = h.rotA * offsetRot;
    }

    protected override void OnHeldEnd()
    {
        // 놓는 순간 손이 충분히 빠르면 던져진 것 → 치움
        if (lastHandASpeed >= throwSpeed)
            ClearObstacle();
    }
}
