using UnityEngine;

/// <summary>
/// Stage3의 현재 미션 단계를 읽어 <see cref="MissionGuideBoard"/> 에 표시 여부를 알려 준다.
///
/// 보드 본체는 Stage2의 <c>TutorialMissionManager_2</c> 를 직접 읽도록 만들어져 있어
/// Stage3에서는 단계를 알 수 없다. 보드의 <c>Step Source</c> 에 이 컴포넌트를 연결하면
/// 판정만 이쪽으로 넘어오고, 거리 감지·페이드·Billboard·컷씬 숨김은 보드가 그대로 맡는다.
///
/// 배치: 안내판(<see cref="MissionGuideBoard"/>)과 같은 GameObject에 붙이고
/// 보드의 <c>Step Source</c> 에 연결한다.
///
/// 단계 설정 —
///  · 미션 1 (태양광 패널) 안내판 : <see cref="TutorialStep_3.Initial"/>
///  · 미션 2 (물결 타이밍) 안내판 : <see cref="TutorialStep_3.PanelComplete"/>
/// <c>TutorialMissionManager_3</c> 는 단계를 순서대로만 넘기고 한 번에 하나만 들고 있으므로
/// "Initial 부터 PanelComplete 직전까지" 는 곧 "CurrentTutorial == Initial" 과 같다.
/// </summary>
/// <remarks>
/// <b>이벤트를 구독하지 않는 이유</b> —
/// 보드가 매 <c>LateUpdate</c> 마다 <see cref="IsOnTargetStep"/> 을 읽으므로, 복제된
/// <c>[Networked] CurrentTutorial</c> 을 그때 직접 보는 것으로 충분하다.
/// <c>TutorialMissionManager_3.TutorialChanged</c> 는 <c>None</c> 을 발행하지 않고
/// 늦게 켜진 쪽은 지나간 발행을 놓치므로, 어차피 복제값을 기준으로 삼아야 한다.
/// 구독을 두지 않으면 해제를 빠뜨려 생기는 누수도 없다.
/// (<c>MissionGuideBoard.RefreshStepFromManager</c> 가 같은 이유로 복제값을 기준으로 쓴다)
///
/// <b>스폰 전 보호</b> —
/// <c>[Networked]</c> 프로퍼티는 스폰 전·디스폰 후에 접근할 수 없다. 그래서 읽기 전에
/// <c>Object == null || !Object.IsValid</c> 를 먼저 보고, 걸리면 <b>false</b> 를 돌려준다.
/// 판정할 수 없을 때 보드를 띄우지 않는 쪽이 안전하다.
/// </remarks>
[AddComponentMenu("ReBloom/Stage3/Stage3 Mission Step Source")]
public sealed class Stage3MissionStepSource : MissionStepSource
{
    [Header("표시 조건")]
    [Tooltip("이 단계일 때만 안내판이 보인다. " +
             "미션 1(태양광)은 Initial, 미션 2(물결)는 PanelComplete.")]
    [SerializeField] private TutorialStep_3 visibleStep = TutorialStep_3.Initial;

    // 매니저를 못 찾았을 때 다시 찾기까지의 간격(초).
    // 스폰 전에는 찾아도 없으므로 매 프레임 Find 하지 않도록 둔다.
    // (MissionGuideBoard 의 같은 이름 상수와 같은 값이다)
    private const float ManagerSearchInterval = 0.5f;

    private TutorialMissionManager_3 manager;
    private float nextManagerSearch;

    /// <summary>
    /// 지금이 <see cref="visibleStep"/> 단계인가.
    /// 매니저를 못 찾았거나 아직 스폰되지 않았으면 false 다.
    /// </summary>
    public override bool IsOnTargetStep
    {
        get
        {
            if (!TryGetManager(out TutorialMissionManager_3 found))
                return false;

            return found.CurrentTutorial == visibleStep;
        }
    }

    /// <summary>
    /// 쓸 수 있는 상태의 Stage3 매니저를 돌려준다.
    /// </summary>
    /// <remarks>
    /// 찾은 매니저는 캐시하되, 씬이 바뀌어 파괴되면 Unity의 null 비교에 걸려 다시 찾는다.
    /// 못 찾는 동안에는 <see cref="ManagerSearchInterval"/> 간격으로만 재탐색한다.
    /// </remarks>
    private bool TryGetManager(out TutorialMissionManager_3 found)
    {
        found = null;

        if (manager == null)
        {
            if (Time.unscaledTime < nextManagerSearch)
                return false;

            nextManagerSearch = Time.unscaledTime + ManagerSearchInterval;
            manager = FindAnyObjectByType<TutorialMissionManager_3>();

            if (manager == null)
                return false;
        }

        // 스폰 전 / 디스폰 후에는 [Networked] 프로퍼티를 읽을 수 없다.
        if (manager.Object == null || !manager.Object.IsValid)
            return false;

        found = manager;
        return true;
    }
}
