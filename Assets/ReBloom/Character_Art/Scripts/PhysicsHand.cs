using UnityEngine;

/// <summary>
/// 트래킹 타깃(XR 컨트롤러)을 <b>속도로 추종</b>하는 물리 손.
/// 손 Rigidbody를 비-kinematic으로 두고 매 FixedUpdate마다 타깃 위치/회전으로
/// 밀어주므로, 손이 벽·바닥 같은 정적 지오메트리를 통과하지 않고 막힌다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class PhysicsHand : MonoBehaviour
{
    [Tooltip("추종할 트래킹 타깃")]
    public Transform target;

    [Tooltip("접촉하지 않은 상태에서 이 거리(m)를 넘어 벌어지면 즉시 스냅한다")]
    public float snapDistance = 0.5f;

    [Tooltip("추종 속도 상한(m/s). 손을 빨리 움직여도 폭주/관통하지 않게 제한.")]
    public float maxFollowSpeed = 15f;

    [Tooltip("접촉 중이어도 이 거리(m)를 넘으면 스냅. snapDistance보다 커야 함.")]
    public float hardSnapDistance = 1.5f;

    Rigidbody rb;
    Collider handCollider; // 손의 콜라이더(루트 1개). IgnoreCollision 처리에 사용.
    bool touching;   // 이 물리 스텝에 무언가와 접촉 중인지

    // 컨트롤러(target) 기준 손의 원래 로컬 오프셋. 손이 컨트롤러의 자식이었을 때의
    // 위치/회전 차이를 Start에서 캡처해, 추종 시에도 손이 원래 방향(레이 방향)을 보게 한다.
    Vector3 targetPositionOffset;
    Quaternion targetRotationOffset = Quaternion.identity;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        handCollider = GetComponent<Collider>();
        rb.useGravity = false;
        rb.maxAngularVelocity = 100f; // 최대 각속도(rad/s) 상한
    }

    void Start()
    {
        // 물리 손은 자기 자신의 트래킹 컨트롤러(target)에 붙은 콜라이더와는 절대 충돌하면 안 된다.
        // 컨트롤러의 SphereCollider와 상시 겹쳐 서로 밀며 회전하기 때문.
        if (target == null)
            return;

        // 컨트롤러 대비 손의 초기 오프셋 캡처 (프리팹 배치 시점의 상대 포즈 = 올바른 손 방향).
        targetPositionOffset = Quaternion.Inverse(target.rotation) * (rb.position - target.position);
        targetRotationOffset = Quaternion.Inverse(target.rotation) * rb.rotation;
        
        if (handCollider == null) return;
        foreach (Collider t in target.GetComponentsInChildren<Collider>())
        {
            if (t == null || t == handCollider) continue;
            Physics.IgnoreCollision(handCollider, t, true);
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
        // 벽/바닥에 손을 대고 있는 동안(접촉 중)에는 스냅을 억제해, 세게 밀어도 손이 지오메트리를 통과하지 않고 표면에 막혀 있게 한다.
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

    /// <summary>잡는 동안 손↔잡힌 물체 충돌을 무시 (튐 방지). 놓을 때 복원.</summary>
    public void IgnoreCollisionWith(Collider other, bool ignore) // 확인 필요
    {
        if (other == null || handCollider == null)
            return;
        Physics.IgnoreCollision(handCollider, other, ignore);
    }
}
