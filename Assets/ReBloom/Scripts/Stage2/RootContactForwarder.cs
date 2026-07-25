using UnityEngine;

/// <summary>
/// AliveStump(콜라이더가 있는 오브젝트)에 붙여, Right Controller가 트리거에서
/// 빠져나가는 순간(OnTriggerExit)을 감지해 해당 뿌리의 활성화 미션에 알린다.
/// 미션 스크립트는 UI 패널(MissionPanel)에 있어 직접 OnTriggerExit를 받지 못하므로,
/// 뿌리에 붙은 이 포워더가 접촉을 중계한다.
/// </summary>
public class RootContactForwarder : MonoBehaviour
{
    [Tooltip("이 뿌리의 활성화 미션 (MissionPanel에 붙은 미션 스크립트)")]
    [SerializeField] private ActivationMission mission;

    [Tooltip("접촉으로 인정할 컨트롤러 태그")]
    [SerializeField] private string rightControllerTag = "Right Controller";

    private void OnTriggerExit(Collider other)
    {
        if (mission == null) return;
        if (other.CompareTag(rightControllerTag))
            mission.NotifyHandDetected();
    }
}
