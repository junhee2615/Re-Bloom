using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 각 AliveStump(뿌리)에 붙는 "활성화 미션" 컴포넌트.
/// 매니저가 이 뿌리 차례에 BeginActivation()을 호출하면 ActivateCircle과 MissionCanvas를 켜다.
/// 플레이어가 StartButton을 누르면(OnStartButtonPressed) StartButton이 꺼지고 MissionPanel이 나타나며
/// 실제 미션(ActivationMission)이 시작된다. 미션을 클리어하면 CompleteActivation()으로
/// ActivateCircle/MissionCanvas를 끄고 AliveDecal(완료 문양)을 켜 뒤 매니저에 알린다.
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

    [Header("Mission UI")]
    [Tooltip("StartButton과 MissionPanel을 담고 있는 캔버스 (AliveStump 하위 MissionCanvas)")]
    [SerializeField] private GameObject missionCanvas;
    [Tooltip("미션 시작 버튼")]
    [SerializeField] private GameObject startButton;
    [Tooltip("미션 안내 텍스트/노트가 표시되는 패널")]
    [SerializeField] private GameObject missionPanel;
    [Tooltip("MissionPanel에 붙은 미션 스크립트 (VibrationTriggerMission / FallingNoteMission / CombinedMission)")]
    [SerializeField] private ActivationMission mission;

    // 이 뿌리의 활성화 미션이 끝났는지
    public bool IsActivated { get; private set; }

    // 지금 이 뿌리의 차례인지 (캔버스 표시 ~ 미션 완료 전)
    public bool IsRunning { get; private set; }

    // StartButton을 눌러 미션이 실제로 시작됐는지
    private bool missionStarted;

    private void Awake()
    {
        // 시작 상태 정리: 원/문양/캔버스 모두 꺼 둔다.
        if (activateCircle != null) activateCircle.SetActive(false);
        if (aliveDecal != null) aliveDecal.SetActive(false);
        if (missionCanvas != null) missionCanvas.SetActive(false);

        // StartButton이 UGUI Button이면 클릭을 자동으로 연결한다.
        // (VR 월드버튼 등 다른 방식이면 인스펙터에서 OnStartButtonPressed를 직접 연결하면 됨.)
        if (startButton != null)
        {
            Button btn = startButton.GetComponent<Button>();
            if (btn != null)
                btn.onClick.AddListener(OnStartButtonPressed);
        }
    }

    /// <summary>
    /// 매니저가 이 뿌리 차례가 되면 호출한다.
    /// ActivateCircle과 MissionCanvas를 켜서 "여기서 미션을 하세요"를 안내한다.
    /// (미션은 아직 시작하지 않고 StartButton 입력을 기다린다.)
    /// </summary>
    public void BeginActivation()
    {
        if (IsActivated || IsRunning)
            return;

        IsRunning = true;
        missionStarted = false;

        if (activateCircle != null)
            activateCircle.SetActive(true);

        // 미션 캔버스 켜기: StartButton 보이고, MissionPanel은 숨김
        if (missionCanvas != null)
            missionCanvas.SetActive(true);
        if (startButton != null)
            startButton.SetActive(true);
        if (missionPanel != null)
            missionPanel.SetActive(false);

        Debug.Log($"{name} 활성화 안내 표시 (StartButton 대기)");
    }

    /// <summary>
    /// StartButton을 눌렀을 때 호출한다.
    /// (UGUI Button이면 Awake에서 자동 연결됨. 그 외에는 인스펙터에서 이 메서드를 버튼 이벤트에 연결.)
    /// StartButton을 끄고 MissionPanel을 켜 뒤 실제 미션을 시작한다.
    /// </summary>
public void OnStartButtonPressed()
    {
        // 이 뿌리 차례가 아니거나 이미 시작했으면 무시 (중복 입력 방지)
        if (!IsRunning || missionStarted)
            return;

        // 역할 제한: 이 미션을 할 수 없는 역할의 플레이어가 누르면 무시
        if (mission != null && !mission.CanLocalPlayerPlay())
        {
            Debug.Log($"{name}: 이 미션은 지정된 역할의 플레이어만 시작할 수 있습니다.");
            return;
        }

        missionStarted = true;

        if (startButton != null)
            startButton.SetActive(false);
        if (missionPanel != null)
            missionPanel.SetActive(true);

        Debug.Log($"{name} 미션 시작");

        if (mission != null)
        {
            mission.OnCleared = CompleteActivation;
            mission.StartMission();
        }
        else
        {
            Debug.LogWarning($"{name} 에 mission이 연결되지 않았습니다.");
        }
    }

    /// <summary>
    /// 미션을 성공적으로 끝냈을 때 호출된다 (mission.Clear() → OnCleared).
    /// ActivateCircle/MissionCanvas를 끄고 AliveDecal을 켜 뒤 매니저에 완료를 알린다.
    /// </summary>
    public void CompleteActivation()
    {
        if (IsActivated)
            return;

        IsActivated = true;
        IsRunning = false;
        missionStarted = false;

        if (activateCircle != null)
            activateCircle.SetActive(false);

        if (missionCanvas != null)
            missionCanvas.SetActive(false);

        if (aliveDecal != null)
            aliveDecal.SetActive(true);

        Debug.Log($"{name} 활성화 미션 완료");

        // 매니저에 알림 → 다음 뿌리로 진행
        missionManager?.OnRootActivated(this);
    }
}
