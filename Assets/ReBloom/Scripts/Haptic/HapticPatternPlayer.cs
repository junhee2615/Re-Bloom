using System.Collections;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// <see cref="HapticPattern"/>을 컨트롤러로 보내는 공용 재생기.
///
/// 원래 이 로직은 <c>CooperativeActivationController</c>의 private 메서드 안에만 있어서
/// 다른 곳에서 패턴을 쓸 수 없었다(그래서 <c>HapticTrigger</c>는 세기·길이를 하드코딩하고 있었다).
/// 동작은 그때와 동일하게 두고 꺼내기만 했으므로, 공명 쪽 체감은 바뀌지 않는다.
/// </summary>
/// <remarks>
/// 코루틴이라 <b>호출자가 StartCoroutine으로 돌린다.</b> 패턴은 펄스 사이를 기다려야 하므로
/// 한 프레임에 끝낼 수 없다 — 그래서 static 메서드지만 MonoBehaviour가 주인이 되어야 한다.
/// </remarks>
public static class HapticPatternPlayer
{
    /// <summary>
    /// 패턴의 펄스를 순서대로 보낸다. 세기는 <see cref="HapticPattern.amplitude"/>를 peak로 보고
    /// <see cref="HapticPattern.amplitudeCurve"/>로 펄스별 배율을 매긴다.
    /// </summary>
    public static IEnumerator Play(HapticPattern pattern, XRNode node)
    {
        if (pattern == null)
            yield break;

        int pulseCount = Mathf.Max(1, pattern.pulseCount);

        for (int pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
        {
            // 펄스가 하나면 커브의 끝(=peak)을 쓴다. 0으로 보면 커브가 0에서 시작할 때 무음이 된다.
            float normalizedPulse = pulseCount == 1 ? 1f : (float)pulseIndex / (pulseCount - 1);

            float curveMultiplier = pattern.amplitudeCurve == null || pattern.amplitudeCurve.length == 0
                ? 1f
                : pattern.amplitudeCurve.Evaluate(normalizedPulse);

            Pulse(pattern.amplitude * curveMultiplier, pattern.duration, node);

            if (pulseIndex < pulseCount - 1)
                yield return new WaitForSeconds(pattern.interval);
        }
    }

    /// <summary>임펄스 하나. 세기나 길이가 0 이하면 아무것도 보내지 않는다.</summary>
    public static void Pulse(float amplitude, float duration, XRNode node)
    {
        amplitude = Mathf.Clamp01(amplitude);

        if (amplitude <= 0f || duration <= 0f)
            return;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);

        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
            device.SendHapticImpulse(0u, amplitude, duration);
    }
}
