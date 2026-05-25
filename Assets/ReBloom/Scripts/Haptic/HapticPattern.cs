using UnityEngine;

[CreateAssetMenu(fileName = "HapticPattern", menuName = "Haptics/Haptic Pattern")]
public class HapticPattern : ScriptableObject
{
    public float amplitude;
    public float duration;
    
    [Header("Pulse")]
    public int pulseCount;
    public float interval;
    [Header("Curve")]
    public AnimationCurve amplitudeCurve;
    public float totalDuration = 1f;
}