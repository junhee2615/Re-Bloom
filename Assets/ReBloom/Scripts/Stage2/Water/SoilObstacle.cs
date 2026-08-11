using UnityEngine;

/// <summary>
/// 흙 : 한 손으로 잡아 옮기거나 던진다.
/// 잡은 손을 따라오다가, 놓으면 그 순간의 속도(배수·상한 적용)대로 물리로 날아가/떨어진다.
/// 던지기 튜닝은 흙에만 존재한다.
/// </summary>
public class SoilObstacle : WaterMissionObstacle
{
    [Header("던지기 튜닝")]
    [Tooltip("놓을 때 실리는 속도 배수. 클수록 더 멀리 날아간다.")]
    [SerializeField] private float throwVelocityMultiplier = 1.6f;

    [Tooltip("던지기 최대 속도(m/s) 상한. 클수록 세게 던지면 더 빨리/멀리 간다.")]
    [SerializeField] private float maxThrowSpeed = 24f;

    private Vector3 offsetPos;
    private Quaternion offsetRot;

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        // 잡은 순간의 손 기준 상대 포즈를 유지
        offsetPos = Quaternion.Inverse(h.rotA) * (transform.position - h.posA);
        offsetRot = Quaternion.Inverse(h.rotA) * transform.rotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;
        pos = h.posA + h.rotA * offsetPos;
        rot = h.rotA * offsetRot;
    }

    // 던지기
    protected override void OnReleased(Vector3 velocity)
        => base.OnReleased(Vector3.ClampMagnitude(velocity * throwVelocityMultiplier, maxThrowSpeed));
}
