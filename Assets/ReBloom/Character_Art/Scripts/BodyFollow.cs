using UnityEngine;

/// <summary>
/// 아바타 몸통이 머리를 따라가게 한다.
///   · 회전 : 머리의 Y축 회전을 <see cref="rotationSpeed"/> 로 부드럽게 추종한다.
///   · 위치 : 머리의 <b>월드 XZ</b> 를 즉시 따라간다. 월드 Y는 건드리지 않는다.
///
/// <b>왜 위치까지 따라가야 하는가</b> —
/// HMD를 쓴 채 실제로 걸으면(Room-scale) XR Origin 루트는 그대로 있고
/// 카메라만 그 안에서 움직인다. 그래서 네트워크 아바타에서는
/// <c>HeadTarget</c> 과 양손 IK 타깃만 이동하고 몸통은 루트에 남는다.
/// 그 상태로 <c>TwoBoneIKConstraint</c> 가 손 타깃을 향해 팔을 풀면
/// 어깨와 손 사이가 걸어간 거리만큼 벌어져 팔이 꺾이거나 늘어난다.
/// 몸통 XZ를 머리에 붙여 두면 어깨-손 거리가 실제 신체와 같아진다.
///
/// <b>왜 Lerp 를 쓰지 않는가</b> —
/// 손 타깃은 지연 없이 들어오므로, 몸통만 보간하면 어깨가 손보다 늦게 따라와
/// 그만큼 팔이 다시 늘어난다. 위치는 즉시 반영해야 한다.
/// (회전은 어깨-손 거리에 영향이 없어 기존 Lerp 를 그대로 둔다)
///
/// <b>왜 LateUpdate 가 아니라 Update 인가</b> —
/// Animation Rigging 의 <c>RigBuilder</c> 는 Animator 평가 단계, 즉
/// <c>LateUpdate</c> <b>이전</b>에 IK를 푼다. LateUpdate 에서 몸통을 옮기면
/// 그 프레임의 IK는 이미 옛 몸통 위치로 풀려 한 프레임 늦는다.
/// Update 에서 옮기면 같은 프레임 IK에 반영되고, 기존 회전 처리와도
/// 같은 머리 샘플을 쓰게 되어 회전·위치가 서로 어긋나지 않는다.
///
/// <b>월드 좌표로 처리하는 이유</b> —
/// 몸통과 <c>HeadTarget</c> 이 같은 부모를 쓴다는 보장이 없으므로
/// 로컬 좌표가 아니라 월드 XZ를 직접 맞춘다. 월드 Y는 매 프레임 현재 값을
/// 그대로 다시 쓰므로, 부모(루트)가 오르내리면 자식 관계로 자연히 따라가고
/// 높이·무릎 처리는 <c>CrouchIK</c> 가 계속 담당한다.
/// </summary>
public class BodyFollow : MonoBehaviour
{
    public Transform headTarget;
    public float rotationSpeed = 5f;

    /// <summary>
    /// <see cref="headTarget"/> 을 Inspector 에서 받지 못해 로컬 리그로 대체했는지.
    /// </summary>
    /// <remarks>
    /// 대체한 경우에는 <b>위치를 따라가지 않는다.</b> 그 머리는 이 아바타의 것이 아니라
    /// 이 기기를 쓰는 사람의 HMD이기 때문이다. 텔레포트 고스트와 로비 전시 캐릭터가
    /// 이 경로를 쓰는데(headTarget 비어 있음), 그쪽까지 위치를 따라가면
    /// 목적지나 전시 위치에 서 있어야 할 몸이 내 머리에 달라붙는다.
    /// 회전만 따라가는 기존 동작은 그대로 유지한다.
    /// </remarks>
    private bool headTargetIsLocalRigFallback;

    private void Start()
    {
        // Inspector에 연결되어 있지 않다면 자동으로 찾기
        if (headTarget == null)
        {
            HardwareRig rig = FindFirstObjectByType<HardwareRig>();

            if (rig != null)
            {
                headTarget = rig.headTransform;
                headTargetIsLocalRigFallback = true;
            }
        }
    }

    void Update()
    {
        if (headTarget == null) return;

        float headYRotation = headTarget.eulerAngles.y;
        Quaternion targetRotation = Quaternion.Euler(0, headYRotation, 0);
        transform.rotation = Quaternion.Lerp(transform.rotation,
                            targetRotation, Time.deltaTime * rotationSpeed);

        // 이 아바타의 머리가 아니면 위치는 건드리지 않는다.
        if (headTargetIsLocalRigFallback) return;

        // 월드 XZ만 머리에 맞추고 월드 Y는 지금 값을 그대로 유지한다.
        Vector3 bodyPosition = transform.position;
        Vector3 headPosition = headTarget.position;

        transform.position = new Vector3(
            headPosition.x,
            bodyPosition.y,
            headPosition.z);
    }
}