using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stage1~3 TutorialMissionManager의 TutorialChanged 이벤트를 받아
/// 미션 시작 / 클리어 / 스테이지 클리어를 연구 로그로 남긴다. 미션 스크립트는 수정하지 않는다.
///
/// 단계 enum은 모두 None(0), Initial(1), 1번 미션 완료(2), 2번 미션 완료(3) ... 순서다.
///  - Initial            → 1번 미션 시작
///  - n번 미션 완료 단계  → n번 미션 클리어 + n+1번 미션 시작
///  - 마지막 미션 완료    → 스테이지 클리어
///
/// 기록되는 이벤트 (Mission 열에는 해당 미션 이름이 자동으로 붙는다)
///  - MissionStart | 미션 순번(1부터)
///  - MissionClear | 소요 시간(초)
///  - StageClear   | 씬 입장부터 걸린 시간(초) | 1번 미션 시작부터 걸린 시간(초)
/// </summary>
public static class ResearchMissionTracker
{
    // 순서는 각 TutorialStep enum의 완료 단계 순서와 같아야 한다.
    private static readonly string[] Stage1Missions = { "Generator", "Valve", "Lever" };
    private static readonly string[] Stage2Missions = { "Waterway", "AquaticPlant", "RootFind", "RootActivate" };
    private static readonly string[] Stage3Missions = { "SolarPanel", "Riverbed", "PlantMachine" };

    private static string[] _missions;          // 현재 스테이지의 미션 목록
    private static int _activeIndex = -1;       // 진행 중인 미션 index (-1 = 시작 전)
    private static float _missionStartTime;
    private static float _firstMissionStartTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        ResetStage();

        // 각 매니저의 TutorialChanged는 SubsystemRegistration에서 null로 초기화되므로 여기서 다시 구독한다.
        TutorialMissionManager.TutorialChanged += OnStage1Changed;
        TutorialMissionManager_2.TutorialChanged += OnStage2Changed;
        TutorialMissionManager_3.TutorialChanged += OnStage3Changed;

        // 도메인 리로드가 꺼져 있어도 중복 구독되지 않게 한 번 빼고 넣는다.
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private static void OnStage1Changed(TutorialStep step) => OnStep(Stage1Missions, (int)step);
    private static void OnStage2Changed(TutorialStep_2 step) => OnStep(Stage2Missions, (int)step);
    private static void OnStage3Changed(TutorialStep_3 step) => OnStep(Stage3Missions, (int)step);

    private static void OnActiveSceneChanged(Scene previous, Scene next) => ResetStage();

    private static void ResetStage()
    {
        _missions = null;
        _activeIndex = -1;
        _missionStartTime = 0f;
        _firstMissionStartTime = -1f;
    }

    private static void OnStep(string[] missions, int stepOrdinal)
    {
        if (stepOrdinal <= 0)
            return;

        if (_missions != missions)
        {
            ResetStage();
            _missions = missions;
        }

        int newIndex = stepOrdinal - 1;
        if (newIndex <= _activeIndex)
            return; // 이미 처리한 단계

        float now = Time.time;

        if (_activeIndex >= 0)
        {
            ClearMission(_activeIndex, now);

            // 두 단계가 한 프레임 안에 넘어가 중간 단계 이벤트가 생략된 경우(Stage3에서 뒤 미션을 먼저
            // 끝내 둔 경우 등)에도 순서대로 남긴다. 이런 미션은 소요 시간이 0으로 기록된다.
            for (int i = _activeIndex + 1; i < newIndex && i < missions.Length; i++)
            {
                StartMission(i, now);
                ClearMission(i, now);
            }
        }

        _activeIndex = newIndex;

        if (newIndex < missions.Length)
            StartMission(newIndex, now);
        else
            ClearStage(now);
    }

    private static void StartMission(int index, float now)
    {
        _missionStartTime = now;
        if (_firstMissionStartTime < 0f)
            _firstMissionStartTime = now;

        ResearchLog.SetMission(_missions[index]);
        ResearchLog.Event("MissionStart", index + 1);
    }

    private static void ClearMission(int index, float now)
    {
        if (index >= _missions.Length)
            return;

        ResearchLog.SetMission(_missions[index]);
        ResearchLog.Event("MissionClear", ResearchTracker.FormatSeconds(now - _missionStartTime));
    }

    private static void ClearStage(float now)
    {
        ResearchLog.ClearMission();

        float sinceFirstMission = _firstMissionStartTime >= 0f ? now - _firstMissionStartTime : -1f;
        ResearchLog.Event("StageClear",
            ResearchTracker.FormatSeconds(now - ResearchTracker.SceneEnterTime),
            sinceFirstMission >= 0f ? ResearchTracker.FormatSeconds(sinceFirstMission) : string.Empty);
    }
}
