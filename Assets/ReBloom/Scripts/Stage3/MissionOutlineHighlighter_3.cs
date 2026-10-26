using UnityEngine;

/// <summary>
/// Stage3 튜토리얼 단계에 맞춰 월드 오브젝트의 Outline을 켜고 끈다.
/// Stage2의 MissionOutlineHighlighter_2와 동작 방식이 같고, 단계만 TutorialStep_3를 따른다.
/// 단계가 바뀔 때마다 전부 끄고 해당 단계 세트만 켠다.
/// </summary>
public class MissionOutlineHighlighter_3 : MonoBehaviour
{
    [Header("Initial — 태양광 패널")]
    [SerializeField] private Outline[] initialOutlines;

    [Header("PanelComplete — 건물→물가 텔레포터, 발판(Stool)")]
    [SerializeField] private Outline[] panelCompleteOutlines;

    [Header("RiverbedComplete — 물가→최종 텔레포터, 세 코어")]
    [SerializeField] private Outline[] riverbedCompleteOutlines;

    private void Awake()
    {
        // 시작 시 전부 비활성화 (에디터 기본 enabled=true 이므로 반드시 꺼 준다)
        SetAll(false);
        TutorialMissionManager_3.TutorialChanged += OnTutorialChanged;
    }

    private void OnDestroy()
    {
        TutorialMissionManager_3.TutorialChanged -= OnTutorialChanged;
    }

    private void OnTutorialChanged(TutorialStep_3 step)
    {
        // 단계가 바뀔 때마다 전부 끄고, 해당 단계 세트만 켠다.
        SetAll(false);

        switch (step)
        {
            case TutorialStep_3.Initial:
                Toggle(initialOutlines, true);
                break;

            case TutorialStep_3.PanelComplete:
                Toggle(panelCompleteOutlines, true);
                break;

            case TutorialStep_3.RiverbedComplete:
                Toggle(riverbedCompleteOutlines, true);
                break;

            // 그 외 단계(PlantMachineComplete, AllComplete 등)는 모두 꺼 둔다
        }
    }

    private void SetAll(bool on)
    {
        Toggle(initialOutlines, on);
        Toggle(panelCompleteOutlines, on);
        Toggle(riverbedCompleteOutlines, on);
    }

    private void Toggle(Outline[] outlines, bool on)
    {
        if (outlines == null) return;

        foreach (var outline in outlines)
        {
            if (outline != null)
                outline.enabled = on;
        }
    }
}
