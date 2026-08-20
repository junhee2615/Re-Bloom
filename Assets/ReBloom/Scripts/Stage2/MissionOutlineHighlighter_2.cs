using UnityEngine;

/// <summary>
/// Stage2 튜토리얼 단계에 맞춰 월드 오브젝트의 Outline을 켜고 끈다.
/// Stage1의 MissionOutlineHighlighter와 동작 방식이 같고, 단계만 TutorialStep_2를 따른다.
/// 단계가 바뀔 때마다 전부 끄고 해당 단계 세트만 켠다.
/// </summary>
public class MissionOutlineHighlighter_2 : MonoBehaviour
{
    [Header("MISSION 1 — 오염된 연못을 정화하라 (수로 장애물)")]
    [SerializeField] private Outline[] initialOutlines;

    [Header("MISSION 2 — 수생식물을 되살려라 (연꽃)")]
    [SerializeField] private Outline[] waterCompleteOutlines;

    [Header("MISSION 3 — 살아있는 뿌리를 찾아라 (그루터기 전체)")]
    [SerializeField] private Outline[] plantCompleteOutlines;

    [Header("MISSION 4 — 뿌리를 활성화하라 (살아있는 그루터기)")]
    [SerializeField] private Outline[] stumpCompleteOutlines;

    [Header("마지막 — 스테이지 종료")]
    [SerializeField] private Outline[] lastOutlines;

    private void Awake()
    {
        // 시작 시 전부 비활성화 (에디터 기본 enabled=true 이므로 반드시 꺼 준다)
        SetAll(false);
        TutorialMissionManager_2.TutorialChanged += OnTutorialChanged;
    }

    private void OnDestroy()
    {
        TutorialMissionManager_2.TutorialChanged -= OnTutorialChanged;
    }

    private void OnTutorialChanged(TutorialStep_2 step)
    {
        // 단계가 바뀔 때마다 전부 끄고, 해당 단계 세트만 켠다.
        SetAll(false);

        switch (step)
        {
            case TutorialStep_2.Initial:
                Toggle(initialOutlines, true);
                break;

            case TutorialStep_2.WaterComplete:
                Toggle(waterCompleteOutlines, true);
                break;

            case TutorialStep_2.PlantComplete:
                Toggle(plantCompleteOutlines, true);
                break;

            case TutorialStep_2.StumpComplete:
                Toggle(stumpCompleteOutlines, true);
                break;

            default:
                Toggle(lastOutlines, true);
                break;
        }
    }

    private void SetAll(bool on)
    {
        Toggle(initialOutlines, on);
        Toggle(waterCompleteOutlines, on);
        Toggle(plantCompleteOutlines, on);
        Toggle(stumpCompleteOutlines, on);
        Toggle(lastOutlines, on);
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
