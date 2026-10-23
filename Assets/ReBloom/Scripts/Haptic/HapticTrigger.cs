using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 손이 이 오브젝트에 닿으면 <see cref="HapticPattern"/>을 <b>만진 손</b>에 보낸다. ear 역할만 느낀다.
///
/// 발전기 두 대가 서로 다른 고장을 알리는 용도다 —
/// 연료 부족은 짧고 빠른 연타, 부품 고장은 묵직하고 느린 반복.
/// <b>세기만 다르게 하면 손이 둘을 구분하지 못한다</b>(전기 코어에서 확인됐다).
/// 길이와 간격을 반대로 벌려야 질감이 갈린다.
/// </summary>
/// <remarks>
/// <paramref name="pattern"/>이 비어 있으면 <see cref="fallbackAmplitude"/>·<see cref="fallbackDuration"/>로
/// 단일 임펄스를 보낸다 — 패턴을 아직 배선하지 않은 기존 오브젝트가 조용해지지 않게 하기 위한 보루다.
/// </remarks>
[RequireComponent(typeof(Collider))]
public class HapticTrigger : MonoBehaviour
{
    [Header("패턴")]
    [Tooltip("닿을 때 보낼 패턴. 비우면 아래 fallback 값으로 단일 임펄스를 보낸다.")]
    public HapticPattern pattern;

    [Header("패턴이 없을 때")]
    [Range(0f, 1f)] public float fallbackAmplitude = 1f;
    [Min(0.01f)] public float fallbackDuration = 0.1f;

    [Header("손 판별")]
    [Tooltip("만진 손을 끝까지 알아내지 못했을 때 보낼 쪽. 보통 여기까지 오지 않는다.")]
    public XRNode targetNode = XRNode.RightHand;

    [Tooltip("같은 손이 이 시간(초) 안에 다시 닿으면 무시한다. 물리 손은 콜라이더가 여러 개라 " +
             "한 번 부딪혀도 OnCollisionEnter가 연달아 들어와 패턴이 겹쳐 쏟아진다.")]
    [Min(0f)] public float retriggerCooldown = 0.35f;

    float nextLeftTime;
    float nextRightTime;

    HardwareRig rig;

    void OnCollisionEnter(Collision collision)
    {
        Play(ResolveNode(collision.collider, collision.GetContact(0).point));
    }

    // 손 콜라이더가 트리거로 설정된 리그에서도 동작하게 둔다. 둘 다 들어와도
    // 쿨다운이 중복 발사를 막는다.
    void OnTriggerEnter(Collider other)
    {
        Play(ResolveNode(other, other.bounds.center));
    }

    void Play(XRNode node)
    {
        // 청각 제약을 받는 에코만 진동으로 정보를 받는다.
        if (!RoleManager.LocalIsEar)
            return;

        if (!TryConsumeCooldown(node))
            return;

        if (pattern != null)
            StartCoroutine(HapticPatternPlayer.Play(pattern, node));
        else
            HapticPatternPlayer.Pulse(fallbackAmplitude, fallbackDuration, node);
    }

    bool TryConsumeCooldown(XRNode node)
    {
        bool isLeft = node == XRNode.LeftHand;
        float next = isLeft ? nextLeftTime : nextRightTime;

        if (Time.time < next)
            return false;

        float until = Time.time + retriggerCooldown;
        if (isLeft) nextLeftTime = until;
        else nextRightTime = until;

        return true;
    }

    /// <summary>
    /// 닿은 콜라이더가 어느 손인지 가린다. 한 가지 방법에만 기대면 리그 설정이 조금 달라져도
    /// 조용히 한쪽 손으로 몰리므로, 확실한 것부터 차례로 시도한다.
    /// </summary>
    XRNode ResolveNode(Collider other, Vector3 contactPoint)
    {
        if (other == null)
            return targetNode;

        // ① 인터랙터가 조상에 있으면 그게 가장 확실하다.
        XRBaseInteractor interactor = other.GetComponentInParent<XRBaseInteractor>();
        if (interactor != null)
        {
            if (interactor.handedness == InteractorHandedness.Left) return XRNode.LeftHand;
            if (interactor.handedness == InteractorHandedness.Right) return XRNode.RightHand;
        }

        // ② 프로젝트 관례인 태그. 단 현재 리그에서는 'Left Controller' 태그가 비어 있고
        //    물리 손(hand_l / hand_r)은 무태그라 여기서 안 걸리는 경우가 많다.
        if (other.CompareTag("Left Controller")) return XRNode.LeftHand;
        if (other.CompareTag("Right Controller")) return XRNode.RightHand;

        // ③ 리그의 두 손 중 닿은 지점에 가까운 쪽. 태그·계층 구성에 의존하지 않는다.
        if (rig == null) rig = FindFirstObjectByType<HardwareRig>();

        if (rig != null && rig.leftHandTransform != null && rig.rightHandTransform != null)
        {
            float toLeft = (rig.leftHandTransform.position - contactPoint).sqrMagnitude;
            float toRight = (rig.rightHandTransform.position - contactPoint).sqrMagnitude;
            return toLeft < toRight ? XRNode.LeftHand : XRNode.RightHand;
        }

        return targetNode;
    }
}
