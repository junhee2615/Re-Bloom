using UnityEngine;

/// <summary>
/// 각 AliveStump(뿌리)에 붙는 "활성화 미션" 컴포넌트.
/// 매니저가 이 뿌리 차례에 BeginActivation()을 호출하면 ActivateCircle을 켜고
/// 리듬 미션을 시작한다. 미션을 성공하면 CompleteActivation()을 호출해
/// ActivateCircle을 끄고 AliveDecal(완료 문양)을 켠 뒤 매니저에 알린다.
/// (LivingRoot가 "찾기"를 담당했다면, RootActivation은 "활성화"를 담당하는 짝이다.)
/// </summary>
public class RootActivation : MonoBehaviour
{
    [Header("Manager")]
    public RootMissionManager missionManager;

    [Header("Visuals")]
    [Tooltip("활성화 미션 진행 중 표시되는 원")]
    [SerializeField] private GameObject activateCircle;
    [Tooltip("활성화 미션 완료 후 생기는 문양")]
    [SerializeField] private GameObject aliveDecal;

    [Header("Mission")]
    [Tooltip("이 뿌리 하위 Panel에 붙은 미션 스크립트 (VibrationTriggerMission / FallingNoteMission / CombinedMission)")]
    [SerializeField] private ActivationMission mission;

    // 이 뿌리의 활성화 미션이 끝났는지
    public bool IsActivated { get; private set; }

    // 지금 이 뿌리의 활성화 미션이 진행 중인지
    public bool IsRunning { get; private set; }

    private void Awake()
    {
        // 시작 상태 정리: 원/문양 모두 꺼 둔다.
        if (activateCircle != null) activateCircle.SetActive(false);
        if (aliveDecal != null) aliveDecal.SetActive(false);
    }

    /// <summary>
    /// 매니저가 이 뿌리 차례가 되면 호출한다. ActivateCircle을 켜고 리듬 미션을 시작.
    /// </summary>
public void BeginActivation()
    {
        if (IsActivated || IsRunning)
            return;

        IsRunning = true;

        if (activateCircle != null)
            activateCircle.SetActive(true);

        Debug.Log($"{name} 활성화 미션 시작");

        // 패널(미션 UI)을 켜고 미션 시작. 미션이 클리어되면 CompleteActivation()이 자동 호출됨.
        if (mission != null)
        {
            mission.gameObject.SetActive(true);
            mission.OnCleared = CompleteActivation;
            mission.StartMission();
        }
        else
        {
            Debug.LogWarning($"{name} 에 mission이 연결되지 않았습니다.");
        }
    }

    /// <summary>
    /// 리듬 미션을 성공적으로 끝냈을 때 호출한다.
    /// (비워 둔 미션 로직이 완료되는 지점에서 이 메서드를 부르면 된다.)
    /// ActivateCircle을 끄고 AliveDecal을 켠 뒤 매니저에 완료를 알린다.
    /// </summary>
public void CompleteActivation()
    {
        if (IsActivated)
            return;

        IsActivated = true;
        IsRunning = false;

        if (activateCircle != null)
            activateCircle.SetActive(false);

        if (aliveDecal != null)
            aliveDecal.SetActive(true);

        // 미션 패널 닫기
        if (mission != null)
            mission.gameObject.SetActive(false);

        Debug.Log($"{name} 활성화 미션 완료");

        // 매니저에 알림 → 다음 뿌리로 진행
        missionManager?.OnRootActivated(this);
    }
}
