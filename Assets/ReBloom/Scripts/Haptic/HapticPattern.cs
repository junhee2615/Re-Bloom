using UnityEngine;

[CreateAssetMenu(fileName = "HapticPattern", menuName = "Haptics/Haptic Pattern")]
public class HapticPattern : ScriptableObject
{
    public float amplitude; // 세기
    public float duration;
    
    [Header("Pulse")]
    public int pulseCount;
    public float interval;
    [Header("Curve")]
    public AnimationCurve amplitudeCurve;
    [Tooltip("비우면 interval 상수 사용.")]
    public AnimationCurve intervalCurve;
    public float totalDuration = 1f;

    /// <summary>진행도에 따른 진폭. amplitude를 peak로 보고 amplitudeCurve로 배율.</summary>
    public float AmplitudeAtProgress(float progress)
    {
        float p = Mathf.Clamp01(progress);
        float multiplier = amplitudeCurve != null && amplitudeCurve.length > 0
            ? amplitudeCurve.Evaluate(p)
            : 1f;
        return Mathf.Clamp01(amplitude * multiplier);
    }

    /// <summary>진행도에 따른 펄스 간격(초). intervalCurve가 비면 interval 상수.</summary>
    public float IntervalAtProgress(float progress)
    {
        float p = Mathf.Clamp01(progress);
        return intervalCurve != null && intervalCurve.length > 0
            ? Mathf.Max(0.01f, intervalCurve.Evaluate(p))
            : interval;
    }
}