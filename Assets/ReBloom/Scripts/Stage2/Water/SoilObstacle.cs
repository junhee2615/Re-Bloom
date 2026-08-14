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

    [Header("들 때 위치")]
    [Tooltip("들 때 손 기준(로컬) 오프셋. 손을 축으로 조각이 함께 돈다.")]
    [SerializeField] private Vector3 localHoldOffset = new Vector3(0f, -0.05f, 0f);

    [Header("스폰 자세")]
    [Tooltip("스폰 시 초기 회전.")]
    [SerializeField] private Vector3 spawnEuler = Vector3.zero;

    // 스폰 순간의 회전을 고정 캡처. 잡으면 이 값을 기준으로 손을 따라 돈다.
    private Quaternion offsetRot;
    private Quaternion spawnRotation;

    public override void Spawned()
    {
        transform.rotation = Quaternion.Euler(spawnEuler);   // 프리팹 지정 초기 회전
        base.Spawned();
        spawnRotation = transform.rotation;
    }

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        offsetRot = Quaternion.Inverse(h.rotA) * spawnRotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;
        pos = h.posA + h.rotA * localHoldOffset;   // 손 기준 오프셋 → 손을 축으로 함께 돎(rigid)
        rot = h.rotA * offsetRot;                  // 스폰 자세에서 시작해 손을 따라 회전
    }

    // 던지기
    protected override void OnReleased(Vector3 velocity)
        => base.OnReleased(Vector3.ClampMagnitude(velocity * throwVelocityMultiplier, maxThrowSpeed));
}
