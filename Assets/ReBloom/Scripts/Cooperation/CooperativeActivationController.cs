using System;
using System.Collections;
using Fusion;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;

/// <summary>
/// Host authority validates the two-player hand activation. The result is kept
/// on NetworkPlayer so every peer receives the same contact and success state.
/// </summary>
[RequireComponent(typeof(NetworkPlayer))]
public sealed class CooperativeActivationController : MonoBehaviour
{
    public static event Action ActivationSucceeded;

    [Header("Activation Motion")]
    [SerializeField, Min(0f)] private float minimumForwardDistance = 0.2f;
    [SerializeField, Min(0.01f)] private float handContactDistance = 0.16f;
    [SerializeField, Min(0f)] private float holdDuration = 1.5f;

    [Header("Feedback")]
    [SerializeField] private HapticPattern contactHapticPattern;
    [SerializeField] private UnityEvent onActivationSucceeded;

    private NetworkPlayer owner;
    private float holdElapsed;
    private bool previousContact;
    private bool previousSuccess;

    private void Awake()
    {
        owner = GetComponent<NetworkPlayer>();
    }

    private void FixedUpdate()
    {
        if (owner == null || !owner.HasNetworkStateAuthority || owner.HasCooperativeActivationSucceeded)
            return;

        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        if (!TryGetPair(players, out NetworkPlayer hostPlayer, out NetworkPlayer clientPlayer) ||
            owner != hostPlayer)
            return;

        bool handsContacted = AreHandsContacted(hostPlayer, clientPlayer);
        bool bothReady = IsActivationReady(hostPlayer) && IsActivationReady(clientPlayer);
        bool holding = handsContacted && bothReady;

        hostPlayer.SetCooperativeHandsContacted(handsContacted);
        clientPlayer.SetCooperativeHandsContacted(handsContacted);

        if (!holding)
        {
            holdElapsed = 0f;
            return;
        }

        holdElapsed += Time.fixedDeltaTime;
        if (holdElapsed < holdDuration)
            return;

        hostPlayer.SetCooperativeActivationSucceeded();
        clientPlayer.SetCooperativeActivationSucceeded();
    }

    private void Update()
    {
        if (owner == null || !owner.IsLocalNetworkRig)
            return;

        bool handsContacted = owner.AreCooperativeHandsContacted;
        if (handsContacted && !previousContact)
            StartCoroutine(PlayContactHaptic());

        bool succeeded = owner.HasCooperativeActivationSucceeded;
        if (succeeded && !previousSuccess)
        {
            onActivationSucceeded?.Invoke();
            ActivationSucceeded?.Invoke();
        }

        previousContact = handsContacted;
        previousSuccess = succeeded;
    }

    private bool IsActivationReady(NetworkPlayer player)
    {
        if (!player.IsActivationTriggerHeld || player.PlayerTransform == null || player.RightHandTransform == null)
            return false;

        Vector3 localHandPosition = player.PlayerTransform.InverseTransformPoint(player.RightHandTransform.position);
        return localHandPosition.z >= minimumForwardDistance;
    }

    private bool AreHandsContacted(NetworkPlayer first, NetworkPlayer second)
    {
        if (first.RightHandTransform == null || second.RightHandTransform == null)
            return false;

        return Vector3.Distance(first.RightHandTransform.position, second.RightHandTransform.position)
               <= handContactDistance;
    }

    private static bool TryGetPair(
        NetworkPlayer[] players,
        out NetworkPlayer hostPlayer,
        out NetworkPlayer clientPlayer)
    {
        hostPlayer = null;
        clientPlayer = null;

        foreach (NetworkPlayer player in players)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            if (player.Object.InputAuthority.PlayerId == 1)
                hostPlayer = player;
            else if (clientPlayer == null)
                clientPlayer = player;
        }

        return hostPlayer != null && clientPlayer != null;
    }

    private IEnumerator PlayContactHaptic()
    {
        if (contactHapticPattern == null)
            yield break;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!device.TryGetHapticCapabilities(out HapticCapabilities capabilities) || !capabilities.supportsImpulse)
            yield break;

        int pulseCount = Mathf.Max(1, contactHapticPattern.pulseCount);
        for (int pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
        {
            float normalizedPulse = pulseCount == 1 ? 1f : (float)pulseIndex / (pulseCount - 1);
            float curveMultiplier = contactHapticPattern.amplitudeCurve == null
                ? 1f
                : contactHapticPattern.amplitudeCurve.Evaluate(normalizedPulse);
            float amplitude = Mathf.Clamp01(contactHapticPattern.amplitude * curveMultiplier);
            device.SendHapticImpulse(0, amplitude, contactHapticPattern.duration);

            if (pulseIndex < pulseCount - 1)
                yield return new WaitForSeconds(contactHapticPattern.interval);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents()
    {
        ActivationSucceeded = null;
    }
}
