using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공명 유지 게이지. 로컬 플레이어의 오른 손목에 붙어 "공명이 얼마나 남았나"를 보여준다.
///
/// 값의 출처는 셋이고, 이 컴포넌트는 전부 <b>읽기만</b> 한다.
///  - 충전 중: <see cref="NetworkPlayer.CooperativeHoldProgress"/> (활성화 모션과 같은 속도로 차오름)
///  - 해제됨: 1 − <see cref="ResonanceController.ReengageProgress"/> (거리 이탈 유예 시간에 걸쳐 줄어듦)
///  - 제약 중이고 충전도 아님: 0, 게이지 숨김
///
/// 줄어드는 건 타이머를 그대로 따라가고(늦게 보이면 실제보다 많이 남은 것처럼 속인다),
/// 차오르는 것만 충전 속도로 부드럽게 보간한다 — 거리 안으로 돌아오면 타이머가 0으로
/// 즉시 리셋되는데, 그 순간 게이지가 툭 차면 어색하다.
///
/// 배치: HardwareRig.rightHandTransform 하위. HardwareRig는 로컬 리그에만 있어 자동으로 로컬 전용이다.
/// </summary>
[AddComponentMenu("ReBloom/Resonance Gauge View")]
public class ResonanceGaugeView : MonoBehaviour
{
    [Header("게이지")]
    [Tooltip("칸 Image들을 담은 부모. 자식 순서(왼쪽부터)가 차는 순서다. 칸을 더하거나 빼도 다시 배선할 필요가 없다.")]
    [SerializeField] private Transform segmentsRoot;

    [Tooltip("직접 지정할 때만. 비우면 segmentsRoot의 자식 Image를 순서대로 쓴다. 한 장짜리 sprite면 하나만.")]
    [SerializeField] private Image[] segments;

    [Tooltip("차오르는 데 걸리는 시간(초). 공명 활성화 홀드 시간과 맞춘다.")]
    [SerializeField, Min(0.01f)] private float fillSeconds = 1.5f;

    [Tooltip("남은 비율(0~1)에 따른 칸 색. 오른쪽이 가득 찬 상태.")]
    [SerializeField] private Gradient fillColor = DefaultFillColor();

    [Header("표시")]
    [Tooltip("보이기/숨기기에 쓰는 CanvasGroup. 비우면 이 오브젝트에서 찾는다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("나타나고 사라지는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeSeconds = 0.25f;

    // 화면에 그리는 값. 목표를 향해 보간된다.
    private float displayed;

    // 바닥 붉은 → 중간 주황 → 가득 연두(첫 참고 이미지의 Damage 바).
    private static Gradient DefaultFillColor()
    {
        var g = new Gradient();
        g.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(0.95f, 0.30f, 0.25f), 0.00f),
                new GradientColorKey(new Color(0.95f, 0.75f, 0.20f), 0.40f),
                new GradientColorKey(new Color(0.45f, 0.90f, 0.35f), 1.00f),
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        if ((segments == null || segments.Length == 0) && segmentsRoot != null)
            segments = segmentsRoot.GetComponentsInChildren<Image>(true);

        if (segments == null || segments.Length == 0)
            Debug.LogWarning($"[ResonanceGaugeView] {name}: 칸 Image가 없습니다. segmentsRoot를 지정해주세요.", this);

        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    private void Update()
    {
        float target = ReadTarget(out bool visible);

        // 줄어드는 건 즉시, 차오르는 건 fillSeconds에 맞춰 선형으로.
        displayed = target < displayed
            ? target
            : Mathf.MoveTowards(displayed, target, Time.deltaTime / fillSeconds);

        Apply(displayed);
        Fade(visible);
    }

    /// <summary>지금 게이지가 향해야 할 값(0~1)과, 게이지를 보여야 하는지.</summary>
    private float ReadTarget(out bool visible)
    {
        ResonanceController controller = ResonanceController.Instance;
        NetworkPlayer local = NetworkPlayer.LocalInstance;

        if (controller != null && controller.IsConstraintReleased)
        {
            visible = true;
            return 1f - controller.ReengageProgress;
        }

        // 제약 중. 손을 대고 충전 중이면 그 진행도를, 아니면 0.
        float hold = local != null ? Mathf.Clamp01(local.CooperativeHoldProgress) : 0f;
        visible = hold > 0f;
        return hold;
    }

    private void Apply(float value)
    {
        if (segments == null || segments.Length == 0)
            return;

        Color color = fillColor != null ? fillColor.Evaluate(value) : Color.white;

        // 전체 0~1을 칸 수로 나눠 왼쪽부터 순서대로 채운다. 칸이 하나면 fillAmount = value.
        float perSegment = 1f / segments.Length;
        for (int i = 0; i < segments.Length; i++)
        {
            Image segment = segments[i];
            if (segment == null) continue;

            segment.fillAmount = Mathf.Clamp01((value - i * perSegment) / perSegment);
            segment.color = color;
        }
    }

    private void Fade(bool visible)
    {
        if (canvasGroup == null)
            return;

        float goal = visible ? 1f : 0f;
        canvasGroup.alpha = fadeSeconds <= 0f
            ? goal
            : Mathf.MoveTowards(canvasGroup.alpha, goal, Time.deltaTime / fadeSeconds);
    }
}
