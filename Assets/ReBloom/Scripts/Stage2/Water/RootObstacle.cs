using Fusion;
using UnityEngine;

/// <summary>
/// 나무뿌리 : 박혀 있다가 "당겨서" 빼낸 뒤 옮긴다.
///
/// 손이 당긴 만큼만 원위치에서 <see cref="leanMax"/> 까지 살짝 딸려온다.
/// 잡은 지점에서 손이 <see cref="pullThreshold"/> 이상 멀어지면 손을 따라 들려서 옮겨진다.
/// 빠진 뒤 놓으면 그 자리에서 낙하, 빠지기 전에 놓으면 박힌 자리에 그대로 정지.
/// </summary>
public class RootObstacle : WaterMissionObstacle
{
    [Header("당기기")]
    [Tooltip("잡은 지점에서 손이 이 거리(m) 이상 멀어지면 뿌리가 빠진다.")]
    [SerializeField] private float pullThreshold = 0.3f;

    [Tooltip("빠지기 전, 뿌리가 손 쪽으로 딸려오는 최대 거리(m).")]
    [SerializeField] private float leanMax = 0.1f;

    // 한 번 빠지면 계속 옮길 수 있게 유지(네트워크 공유).
    [Networked] private NetworkBool Extracted { get; set; }

    // 호스트 전용: 잡은 순간의 손 위치
    private Vector3 grabHandPos;
    private Vector3 offsetPos;
    private Quaternion offsetRot;

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        grabHandPos = h.posA; // 빠진 뒤 손을 따라올 때 쓸 상대 포즈(잡는 순간 기준).
        offsetPos = Quaternion.Inverse(h.rotA) * (transform.position - h.posA);
        offsetRot = Quaternion.Inverse(h.rotA) * transform.rotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;

        if (!Extracted)
        {
            // 잡은 지점 대비 당긴 거리.
            Vector3 pull = h.posA - grabHandPos;
            if (pull.magnitude >= pullThreshold)
            {
                Extracted = true;
            }
            else
            {
                // 아직 안 빠짐: 원위치에서 당긴 방향으로 leanMax 까지만 딸려온다.
                pos = originPosition + Vector3.ClampMagnitude(pull, leanMax);
                rot = originRotation;
                return;
            }
        }
        
        pos = h.posA + h.rotA * offsetPos;
        rot = h.rotA * offsetRot;
    }

    protected override void OnReleased(Vector3 velocity)
    {
        if (Extracted)
            base.OnReleased(Vector3.zero);   // 빠진 뒤엔 그 자리에서 낙하
        else
            SettleInPlace();                 // 안 빠졌으면 박힌 채 정지
    }
}
