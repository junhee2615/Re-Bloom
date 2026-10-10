using System;
using System.Collections;
using Fusion;
using ReBloom.Solar;
using ReBloom.Water;
using UnityEngine;

/// <summary>
/// Stage3 튜토리얼 진행 관리자. Stage2 의 TutorialMissionManager_2 와 같은 구조다.
///
/// 흐름
///  - Spawned 직후 : None (아무 미션도 안내하지 않는 상태)
///  - initialDelay(기본 5초) 뒤                         : Initial
///  - SolarMissionManager.IsClear                       : PanelComplete
///  - RiverbedFlowController.onMissionComplete          : RiverbedComplete
///  - WaterValveRotate.IsCleared + PlantCoreSeedButton.IsPressed : PlantMachineComplete
///
/// 단계는 순서대로만 넘어간다. 앞 미션보다 뒤 미션을 먼저 끝냈다면, 앞 단계에 도달하는 즉시 이어서 넘어간다.
/// 상태는 [Networked] 로 복제되고 Host 만 바꾼다. 값이 바뀌는 순간 모든 머신의 Update 에서
/// static 이벤트 TutorialChanged 가 1회 발행된다(UIPanel 이 구독).
/// </summary>
public class TutorialMissionManager_3 : NetworkBehaviour
{
    public static event Action<TutorialStep_3> TutorialChanged;

    [Header("첫 튜토리얼 지연")]
    [Tooltip("씬 진입 후 MISSION 1 안내를 띄우기까지 대기(초). 그 전까지는 None 상태.")]
    [SerializeField] private float initialDelay = 5f;

    [Header("미션 (비우면 씬에서 자동으로 찾는다)")]
    [SerializeField] private SolarMissionManager solarMission;
    [SerializeField] private RiverbedFlowController riverbedMission;
    [SerializeField] private WaterValveRotate waterValve;
    [SerializeField] private PlantCoreSeedButton plantSeedButton;

    [Networked]
    public TutorialStep_3 CurrentTutorial { get; set; }

    private TutorialStep_3 lastTutorial = TutorialStep_3.None;

    // 물길 미션은 네트워크 상태 없이 이벤트로만 끝을 알리므로, 받은 사실을 기억해 둔다.
    private bool riverbedCompleted;

    private void Awake()
    {
        if (solarMission == null) solarMission = FindFirstObjectByType<SolarMissionManager>();
        if (riverbedMission == null) riverbedMission = FindFirstObjectByType<RiverbedFlowController>();
        if (waterValve == null) waterValve = FindFirstObjectByType<WaterValveRotate>();
        if (plantSeedButton == null) plantSeedButton = FindFirstObjectByType<PlantCoreSeedButton>();

        if (riverbedMission != null)
            riverbedMission.onMissionComplete.AddListener(OnRiverbedComplete);
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentTutorial = TutorialStep_3.None;
            StartCoroutine(BeginInitialAfterDelay());
        }
    }

    // 씬 진입 직후에는 아무 미션도 안내하지 않다가, initialDelay 뒤 MISSION 1 을 띄운다.
    private IEnumerator BeginInitialAfterDelay()
    {
        yield return new WaitForSeconds(initialDelay);

        if (!IsReady || !HasStateAuthority)
            yield break;

        if (CurrentTutorial == TutorialStep_3.None)
            CurrentTutorial = TutorialStep_3.Initial;
    }

    // Host 만 미션 상태를 보고 다음 단계로 넘긴다.
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        switch (CurrentTutorial)
        {
            case TutorialStep_3.Initial:
                if (IsSpawned(solarMission) && solarMission.IsClear)
                    CurrentTutorial = TutorialStep_3.PanelComplete;
                break;

            case TutorialStep_3.PanelComplete:
                if (riverbedCompleted)
                    CurrentTutorial = TutorialStep_3.RiverbedComplete;
                break;

            case TutorialStep_3.RiverbedComplete:
                if (IsSpawned(waterValve) && waterValve.IsCleared &&
                    IsSpawned(plantSeedButton) && plantSeedButton.IsPressed)
                    CurrentTutorial = TutorialStep_3.PlantMachineComplete;
                break;
        }
    }

    private void Update()
    {
        if (!IsReady)
            return;

        if (CurrentTutorial != lastTutorial)
        {
            lastTutorial = CurrentTutorial;

            if (CurrentTutorial != TutorialStep_3.None)
                TutorialChanged?.Invoke(CurrentTutorial);
        }
    }

    private void OnRiverbedComplete()
    {
        riverbedCompleted = true;
    }

    // 스폰 전 / 디스폰 후에는 Object 가 없어 네트워크 프로퍼티 접근이 불가하다.
    private bool IsReady => Object != null && Object.IsValid;

    private static bool IsSpawned(NetworkBehaviour behaviour) =>
        behaviour != null && behaviour.Object != null && behaviour.Object.IsValid;

    private void OnDestroy()
    {
        if (riverbedMission != null)
            riverbedMission.onMissionComplete.RemoveListener(OnRiverbedComplete);
    }

    // 도메인 리로드 비활성화 환경 대비: 재생 세션 시작 전 정적 이벤트 리셋
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        TutorialChanged = null;
    }
}
