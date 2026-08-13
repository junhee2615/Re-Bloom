using UnityEngine;


public class MissionOutlineHighlighter : MonoBehaviour
{
    [Header("첫 번째 미션")]
    [SerializeField] private Outline[] firstOutlines;

    [Header("두 번째 미션")]
    [SerializeField] private Outline[] secondOutlines;

    [Header("세 번째 미션")]
    [SerializeField] private Outline[] thirdOutlines;

    [Header("마지막")]
    [SerializeField] private Outline[] lastOutlines;

    private void Awake()
    {
        // 시작 시 전부 비활성화 (씬에서 기본 enabled=true 이므로 반드시 꺼준다)
        SetAll(false);
        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
    }

    private void OnTutorialChanged(TutorialStep step)
    {
        // 단계가 바뀔 때마다 전부 끄고, 해당 단계 세트만 켠다.
        SetAll(false);

        switch (step)
        {
            case TutorialStep.Initial:
                Toggle(firstOutlines, true);
                break;

            case TutorialStep.GeneratorComplete:
                Toggle(secondOutlines, true);
                break;

            case TutorialStep.ValveComplete:
                Toggle(thirdOutlines, true);
                break;

            default:
                Toggle(lastOutlines, true);
                break;
        }
    }

    private void SetAll(bool on)
    {
        Toggle(firstOutlines, on);
        Toggle(secondOutlines, on);
        Toggle(thirdOutlines, on);
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