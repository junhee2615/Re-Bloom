using Fusion;
using UnityEngine;

/// <summary>
/// 나무뿌리 : 밑동은 땅에 고정된 채, 위쪽을 잡고 당기면 밑동을 축으로 "기울어지며" 뽑힌다.
///
/// 잡은 동안 뿌리는 밑동(<see cref="basePivotOffset"/> 기준 피벗)을 축으로 손 쪽으로 기운다.
/// 잡은 순간 대비 기운 각도가 <see cref="extractTiltAngle"/> 이상이면 "툭" 빠져, 그때부터 손을 따라 들려서 옮겨진다.
/// 빠진 뒤 놓으면 그 자리에서 낙하, 빠지기 전에 놓으면 그 자리에 그대로 정지.
/// </summary>
public class RootObstacle : WaterMissionObstacle
{
    [Header("기울여 당기기")]
    [Tooltip("고정 피벗의 로컬 오프셋.")]
    [SerializeField] private Vector3 basePivotOffset = Vector3.zero;
    [Tooltip("잡은 순간 대비 이 각도 이상 기울이면 빠진다.")]
    [SerializeField] private float extractTiltAngle = 35f;

    [Header("디버그")]
    [Tooltip("씬 뷰에 밑동 피벗·transform 피벗을 표시해 basePivotOffset 조정을 돕는다.")]
    [SerializeField] private bool showPivotGizmo = true;
    [Tooltip("기즈모 구 반지름(m).")]
    [SerializeField] private float gizmoRadius = 0.05f;

    // 한 번 빠지면 계속 옮길 수 있게 유지(네트워크 공유).
    [Networked] private NetworkBool Extracted { get; set; }

    // 기울이는 동안엔 kinematic.
    protected override bool PoseDirectlyWhileHeld => !Extracted;

    // 호스트 전용: 잡은 순간의 손 위치 / 빠진 뒤 추종용 상대 포즈.
    private Vector3 grabHandPos;
    private Vector3 offsetPos;
    private Quaternion offsetRot;

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        grabHandPos = h.posA;
        offsetPos = Quaternion.Inverse(h.rotA) * (transform.position - h.posA);
        offsetRot = Quaternion.Inverse(h.rotA) * transform.rotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;

        if (!Extracted)
        {
            // 밑동을 축으로, 잡은 지점을 현재 손 쪽으로 회전 → 뿌리가 기운다.
            Vector3 pivot = originPosition + originRotation * basePivotOffset;
            Vector3 fromArm = grabHandPos - pivot;   // 잡은 순간
            Vector3 toArm = h.posA - pivot;           // 현재

            if (fromArm.sqrMagnitude < 1e-6f || toArm.sqrMagnitude < 1e-6f)
            {
                pos = originPosition;
                rot = originRotation;
                return;
            }

            float tiltAngle = Vector3.Angle(fromArm, toArm);
            if (tiltAngle >= extractTiltAngle)
            {
                Extracted = true;   // 충분히 기울임
            }
            else
            {
                // 피벗 기준 회전.
                Quaternion tilt = Quaternion.FromToRotation(fromArm, toArm);
                rot = tilt * originRotation;
                pos = pivot + tilt * (originPosition - pivot);
                return;
            }
        }
        
        // 빠진 뒤
        pos = h.posA + h.rotA * offsetPos;
        rot = h.rotA * offsetRot;
    }

    protected override void OnReleased(Vector3 velocity)
    {
        if (Extracted)
            base.OnReleased(Vector3.zero);   // 빠진 뒤엔 그 자리에서 낙하
        else
            SettleInPlace();                 // 안 빠졌으면 그 자리에 그대로 정지
    }

    // 에디터: 밑동 피벗을 눈으로 보고 basePivotOffset 을 맞춘다.
    // 정지 중엔 originPosition 이 없으므로 현재 transform 을 기준 포즈로 본다.
    private void OnDrawGizmosSelected()
    {
        if (!showPivotGizmo) return;

        Vector3 tformPivot = transform.position;                                  // transform 피벗(노랑)
        Vector3 basePivot = transform.position + transform.rotation * basePivotOffset; // 고정 피벗(초록)

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(tformPivot, gizmoRadius);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(basePivot, gizmoRadius);
        Gizmos.DrawLine(tformPivot, basePivot);
    }
}
