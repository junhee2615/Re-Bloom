using Fusion;
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Stage2 튜토리얼 진행 관리자. Stage1의 TutorialMissionManager와 같은 구조이며
/// 단계 enum만 TutorialStep_2를 쓴다.
///
/// 흐름
///  - Spawned 직후 : None (아무 미션도 안내하지 않는 상태)
///  - initialDelay(기본 5초) 뒤 : Initial            → MISSION 1 수로 정화
///  - WaterMissionManager.MissionCleared      → WaterComplete  → MISSION 2 수생식물
///  - PlantClearSequence.MissionCleared       → PlantComplete  → MISSION 3 뿌리 찾기
///  - RootMissionManager.RootsFound           → StumpComplete  → MISSION 4 뿌리 활성화
///  - RootMissionManager.AllRootsActivated    → AllComplete    → 스테이지 종료
///
/// 상태는 [Networked]로 복제되고, 값이 바뀌는 순간 모든 머신의 Update에서
/// static 이벤트 TutorialChanged가 1회 발행된다(UIPanel / MissionOutlineHighlighter_2가 구독).
/// </summary>
public class TutorialMissionManager_2 : NetworkBehaviour
{
    public static event Action<TutorialStep_2> TutorialChanged;

    [Header("첫 튜토리얼 지연")]
    [Tooltip("씬 진입 후 MISSION 1 안내를 띄우기까지 대기(초). 그 전까지는 None 상태.")]
    [SerializeField] private float initialDelay = 5f;

    [Networked]
    public TutorialStep_2 CurrentTutorial { get; set; }

    private TutorialStep_2 lastTutorial = TutorialStep_2.None;

    private void Awake()
    {
        WaterMissionManager.MissionCleared += ShowWaterComplete;
        PlantClearSequence.MissionCleared += ShowPlantComplete;
        RootMissionManager.RootsFound += ShowStumpComplete;
        RootMissionManager.AllRootsActivated += ShowAllComplete;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentTutorial = TutorialStep_2.None;
            StartCoroutine(BeginInitialAfterDelay());
        }
    }

    // 씬 진입 직후에는 아무 미션도 안내하지 않다가, initialDelay 뒤 MISSION 1을 띄운다.
    private IEnumerator BeginInitialAfterDelay()
    {
        yield return new WaitForSeconds(initialDelay);

        if (!IsReady || !HasStateAuthority)
            yield break;

        // 대기 중 이미 미션이 진행됐다면 덮어쓰지 않는다.
        if (CurrentTutorial != TutorialStep_2.None)
            yield break;

        CurrentTutorial = TutorialStep_2.Initial;
    }

    private void Update()
    {
        if (!IsReady)
            return;

        if (CurrentTutorial != lastTutorial)
        {
            lastTutorial = CurrentTutorial;

            if (CurrentTutorial != TutorialStep_2.None)
            {
                TutorialChanged?.Invoke(CurrentTutorial);
            }
        }
    }

    // MISSION 1(수로 정화) 완료 → MISSION 2 안내
    public void ShowWaterComplete()
    {
        SetStep(TutorialStep_2.WaterComplete);
    }

    // MISSION 2(수생식물 되살리기) 완료 → MISSION 3 안내
    public void ShowPlantComplete()
    {
        SetStep(TutorialStep_2.PlantComplete);
    }

    // MISSION 3(살아있는 뿌리 찾기) 완료 → MISSION 4 안내
    public void ShowStumpComplete()
    {
        SetStep(TutorialStep_2.StumpComplete);
    }

    // MISSION 4(뿌리 활성화) 완료 → 스테이지 종료
    public void ShowAllComplete()
    {
        SetStep(TutorialStep_2.AllComplete);
    }

    // 상태 변경은 권한자만. 클라이언트에서 호출되면 조용히 무시되고 복제된 값을 따른다.
    private void SetStep(TutorialStep_2 step)
    {
        if (!IsReady || !HasStateAuthority)
            return;

        CurrentTutorial = step;
    }

    // 스폰 전 / 디스폰 후에는 Object가 없어 네트워크 프로퍼티 접근이 불가하다.
    private bool IsReady => Object != null && Object.IsValid;

    private void OnDestroy()
    {
        WaterMissionManager.MissionCleared -= ShowWaterComplete;
        PlantClearSequence.MissionCleared -= ShowPlantComplete;
        RootMissionManager.RootsFound -= ShowStumpComplete;
        RootMissionManager.AllRootsActivated -= ShowAllComplete;
    }

    // 도메인 리로드 비활성화 환경 대비: 재생 세션 시작 전 정적 이벤트 리셋
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        TutorialChanged = null;
    }
}
