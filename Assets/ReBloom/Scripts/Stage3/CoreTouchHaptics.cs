using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 코어에 손을 대고 있는 동안 진동을 보냄, ear역할만 느낌 
///   0   : 일정한 간격·세기 (WaterCore)
///   0.3 : 살짝 흔들림 (PlantCore)
///   0.9 : 크게 튀고 지지직거림 (ElectricityCore)
/// </summary>
[RequireComponent(typeof(Collider))]
public class CoreTouchHaptics : MonoBehaviour
{
    [Tooltip("0 = 완전히 규칙적, 1 = 매우 불규칙")]
    [Range(0f, 1f)] public float irregularity = 0f;

    const float BaseAmplitude = 0.5f;
    const float BaseDuration = 0.1f;
    const float BaseInterval = 0.45f;

    readonly HashSet<Collider> touchingHands = new HashSet<Collider>();
    float nextPulseTime;

    void OnTriggerEnter(Collider other)
    {
        if (!IsHand(other)) return;
        if (touchingHands.Count == 0) nextPulseTime = 0f; // 닿자마자 첫 진동
        touchingHands.Add(other);
    }

    void OnTriggerExit(Collider other)
    {
        if (touchingHands.Remove(other) && touchingHands.Count == 0) StopAll();
    }

    void OnDisable()
    {
        touchingHands.Clear();
        StopAll();
    }

    void Update()
    {
        if (touchingHands.Count == 0) return;
        if (!RoleManager.LocalIsEar) return;
        if (Time.time < nextPulseTime) return;

        float r = irregularity;
        float amp = Mathf.Clamp01(BaseAmplitude * (1f + Random.Range(-r, r) * 0.6f));
        float dur = BaseDuration * (1f + Random.Range(-r, r) * 0.8f);

        foreach (var hand in touchingHands)
            Send(hand.CompareTag("Left Controller") ? XRNode.LeftHand : XRNode.RightHand, amp, dur);

        // 불규칙할수록 간격이 크게 흔들리고, 가끔 짧게 연달아 튄다
        float gap = BaseInterval * (1f + Random.Range(-r, r));
        if (Random.value < r * r * 0.4f) gap = dur + 0.04f;
        nextPulseTime = Time.time + Mathf.Max(0.03f, gap);
    }

    static bool IsHand(Collider c) => c.CompareTag("Right Controller") || c.CompareTag("Left Controller");

    static void Send(XRNode node, float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            device.SendHapticImpulse(0u, amplitude, duration);
    }

    static void StopAll()
    {
        InputDevices.GetDeviceAtXRNode(XRNode.RightHand).StopHaptics();
        InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).StopHaptics();
    }
}
