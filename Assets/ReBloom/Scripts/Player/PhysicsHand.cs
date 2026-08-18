using UnityEngine;

/// <summary>
/// 트래킹 타깃(XR 컨트롤러)을 <b>속도로 추종</b>하는 물리 손.
/// 손 Rigidbody를 비-kinematic으로 두고 매 FixedUpdate마다 타깃 위치/회전으로
/// 밀어주므로, 손이 벽·바닥 같은 정적 지오메트리를 통과하지 않고 막힌다.
///
/// 컨트롤러 기준 손의 오프셋은 <see cref="localPositionOffset"/> / <see cref="localEulerOffset"/>에
/// 저장된 <b>배치 시점의 값</b>을 쓴다. 예전처럼 실행 중에 실제 Transform 차이로 계산하면,
/// XR 트래킹이 컨트롤러를 이미 옮긴 뒤에 계산될 경우 오프셋이 통째로 어긋나
/// 손이 허공에 뜬 채로 컨트롤러를 따라다니게 된다(기기/피어마다 타이밍이 달라 한쪽만 깨지기도 함).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicsHand : MonoBehaviour
{
    [Tooltip("추종할 트래킹 타깃")]
    public Transform target;

    [Header("컨트롤러 기준 손 오프셋")]
    [Tooltip("컨트롤러 로컬 기준 위치 오프셋. 프리팹 배치값에서 가져온다.")]
    [SerializeField] private Vector3 localPositionOffset = new Vector3(0f, 0f, -0.13f);

    [Tooltip("컨트롤러 로컬 기준 회전 오프셋(오일러).")]
    [SerializeField] private Vector3 localEulerOffset;

    [Tooltip("켜면 예전 방식대로 실행 중 첫 프레임의 Transform 차이로 오프셋을 계산한다. 트래킹 초기화 타이밍에 따라 어긋날 수 있으니 권장하지 않는다.")]
    [SerializeField] private bool captureOffsetAtRuntime = false;

    [Header("추종")]
    [Tooltip("접촉하지 않은 상태에서 이 거리(m)를 넘어 벌어지면 즉시 스냅한다")]
    public float snapDistance = 0.5f;

    [Tooltip("추종 속도 상한(m/s). 손을 빨리 움직여도 폭주/관통하지 않게 제한.")]
    public float maxFollowSpeed = 15f;

    [Tooltip("접촉 중이어도 이 거리(m)를 넘으면 스냅. snapDistance보다 커야 함.")]
    public float hardSnapDistance = 1.5f;

    Rigidbody rb;
    bool touching;   // 이 물리 스텝에 무언가와 접촉 중인지

    // 컨트롤러(target) 기준 손의 오프셋.
    Vector3 targetPositionOffset;
    Quaternion targetRotationOffset = Quaternion.identity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false;
        rb.maxAngularVelocity = 100f; // 최대 각속도(rad/s) 상한

        targetPositionOffset = localPositionOffset;
        targetRotationOffset = Quaternion.Euler(localEulerOffset);
    }

    void Start()
    {
        if (!captureOffsetAtRuntime || target == null)
            return;

        // 구버전 동작(권장하지 않음): 첫 프레임의 실제 상대 포즈로 오프셋을 잡는다.
        targetPositionOffset = Quaternion.Inverse(target.rotation) * (rb.position - target.position);
        targetRotationOffset = Quaternion.Inverse(target.rotation) * rb.rotation;

        if (targetPositionOffset.magnitude > 0.5f)
        {
            Debug.LogWarning(
                $"[PhysicsHand] {name}: 실행 중 계산한 오프셋이 {targetPositionOffset.magnitude:F2}m 입니다. " +
                $"XR 트래킹이 이미 컨트롤러를 옮긴 뒤에 계산된 것으로 보이며, 손이 허공에 뜹니다. " +
                $"Capture Offset At Runtime을 끄세요.", this);
        }
    }

    void FixedUpdate()
    {
        if (target == null) return;

        // 오프셋을 반영한 목표 포즈 (손이 컨트롤러 대비 원래 방향을 유지)
        Vector3 goalPosition = target.position + target.rotation * targetPositionOffset;
        Quaternion goalRotation = target.rotation * targetRotationOffset;

        Vector3 delta = goalPosition - rb.position;
        float sqrDelta = delta.sqrMagnitude;

        // 스냅 조건:
        //  - 접촉 중이 아닐 때 snapDistance 초과 → 지연 회복(텔레포트 등)
        //  - 접촉 중이어도 hardSnapDistance 초과 → 회복
        bool softExceeded = sqrDelta > snapDistance * snapDistance;
        bool hardExceeded = sqrDelta > hardSnapDistance * hardSnapDistance;
        if (hardExceeded || (softExceeded && !touching))
        {
            rb.position = goalPosition;
            rb.rotation = goalRotation;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            touching = false;
            return;
        }

        // 위치 추종 (속도 상한으로 폭주/관통 억제)
        rb.linearVelocity = Vector3.ClampMagnitude(delta / Time.fixedDeltaTime, maxFollowSpeed);

        // 회전 추종
        Quaternion rotDelta = goalRotation * Quaternion.Inverse(rb.rotation);
        rotDelta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f) angleDeg -= 360f;

        if (Mathf.Abs(angleDeg) > Mathf.Epsilon && !float.IsInfinity(axis.x))
            rb.angularVelocity = axis.normalized * (angleDeg * Mathf.Deg2Rad / Time.fixedDeltaTime);
        else
            rb.angularVelocity = Vector3.zero;

        // 다음 물리 스텝의 접촉 콜백 다시 세팅
        touching = false;
    }

    // 이 스텝에 무언가와 접촉 중이면 기록 → 접촉 중엔 스냅을 억제
    void OnCollisionStay(Collision _)
    {
        touching = true;
    }

    /// <summary>
    /// 에디터에서 현재 배치(손 Transform vs 컨트롤러 Transform)를 오프셋 값으로 굳힌다.
    /// 플레이 중이 아닐 때만 쓸 것.
    /// </summary>
    [ContextMenu("현재 배치에서 오프셋 캡처")]
    private void CaptureOffsetFromCurrentPose()
    {
        if (target == null)
        {
            Debug.LogWarning("[PhysicsHand] target이 없어 오프셋을 캡처할 수 없습니다.", this);
            return;
        }

        localPositionOffset = Quaternion.Inverse(target.rotation) * (transform.position - target.position);
        localEulerOffset = (Quaternion.Inverse(target.rotation) * transform.rotation).eulerAngles;

        Debug.Log($"[PhysicsHand] {name} 오프셋 캡처: pos={localPositionOffset}, euler={localEulerOffset}", this);
    }
}
