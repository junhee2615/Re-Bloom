using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 코어에 손을 대고 있는 동안 박자 없이 일정한 진동을 계속 보낸다. ear 역할만 느낀다.
/// intensity 로 진동 세기를 정한다.
///   WaterCore       : 가장 강함 (0.9)
///   PlantCore       : 중간      (0.55)
///   ElectricityCore : 가장 약함 (0.25)
/// </summary>
[RequireComponent(typeof(Collider))]
public class CoreTouchHaptics : MonoBehaviour
{
    [Tooltip("진동 세기. 0 = 없음, 1 = 최대")]
    [Range(0f, 1f)] public float intensity = 0.5f;

    // 진동이 끊기지 않도록, 한 번 보낸 진동이 끝나기 전에 다음 진동을 이어서 보낸다
    const float PulseDuration = 0.1f;
    const float ResendInterval = 0.05f;

    readonly HashSet<Collider> touchingHands = new HashSet<Collider>();
    float nextPulseTime;

    void OnTriggerEnter(Collider other)
    {
        if (!IsHand(other)) return;
        if (touchingHands.Count == 0) nextPulseTime = 0f; // 닿자마자 바로 진동
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

        foreach (var hand in touchingHands)
            Send(hand.CompareTag("Left Controller") ? XRNode.LeftHand : XRNode.RightHand, intensity, PulseDuration);

        nextPulseTime = Time.time + ResendInterval;
    }

    static bool IsHand(Collider c) => c.CompareTag("Right Controller") || c.CompareTag("Left Controller");

    static void Send(XRNode node, float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
    }

    static void StopAll()
    {
        InputDevices.GetDeviceAtXRNode(XRNode.RightHand).StopHaptics();
        InputDevices.GetDeviceAtXRNode(XRNode.LeftHand).StopHaptics();
    }
}
