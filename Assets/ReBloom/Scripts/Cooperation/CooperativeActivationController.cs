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

    [Header("Debug")]
    [Tooltip("활성화 판정 상태를 주기적으로 출력합니다. 검증 후 끄세요.")]
    [SerializeField] private bool logActivationDebug = true;
    [SerializeField, Min(0.05f)] private float debugLogInterval = 0.25f;

    [Header("Feedback")]
    [SerializeField] private HapticPattern contactHapticPattern;
    [SerializeField] private UnityEvent onActivationSucceeded;

    private NetworkPlayer owner;
    private float holdElapsed;
    private bool previousContact;
    private bool previousSuccess;
    private float nextDebugLogTime;

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

        float hostForwardDistance = GetForwardDistance(hostPlayer);
        float clientForwardDistance = GetForwardDistance(clientPlayer);
        bool hostReady = IsActivationReady(hostPlayer, hostForwardDistance);
        bool clientReady = IsActivationReady(clientPlayer, clientForwardDistance);
        float handDistance = GetHandDistance(hostPlayer, clientPlayer);
        bool handsContacted = handDistance <= handContactDistance;
        bool bothReady = IsActivationReady(hostPlayer, hostForwardDistance) && clientReady;
        bool holding = handsContacted && bothReady;

        hostPlayer.SetCooperativeHandsContacted(handsContacted);
        clientPlayer.SetCooperativeHandsContacted(handsContacted);

        if (!holding)
        {
            holdElapsed = 0f;
            LogActivationState(
                hostPlayer, clientPlayer, hostForwardDistance, clientForwardDistance,
                handDistance, hostReady, clientReady, handsContacted, holding);
            return;
        }

        holdElapsed += Time.fixedDeltaTime;
        LogActivationState(
            hostPlayer, clientPlayer, hostForwardDistance, clientForwardDistance,
            handDistance, hostReady, clientReady, handsContacted, holding);
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

    private bool IsActivationReady(NetworkPlayer player, float forwardDistance)
    {
        return player.IsActivationTriggerHeld && forwardDistance >= minimumForwardDistance;
    }

    private static float GetForwardDistance(NetworkPlayer player)
    {
        if (player.PlayerTransform == null || player.RightHandTransform == null)
            return float.NegativeInfinity;

        // playerTransform은 월드 -축을 정면으로 보고 있어 손을 앞으로 뻗으면 월드 좌표는
        // 감소하지만, InverseTransformPoint가 회전을 반영하므로 로컬 +Z(정면)는 증가한다.
        // 따라서 부호 반전 없이 로컬 z가 곧 "정면으로 뻗은 거리"다.
        return player.PlayerTransform.InverseTransformPoint(player.RightHandTransform.position).z;
    }

    private static float GetHandDistance(NetworkPlayer first, NetworkPlayer second)
    {
        if (first.RightHandTransform == null || second.RightHandTransform == null)
            return float.PositiveInfinity;

        return Vector3.Distance(first.RightHandTransform.position, second.RightHandTransform.position);
    }

    private static Vector3 GetLocalHandOffset(NetworkPlayer player)
    {
        if (player.PlayerTransform == null || player.RightHandTransform == null)
            return Vector3.zero;

        return player.PlayerTransform.InverseTransformPoint(player.RightHandTransform.position);
    }

    private void LogActivationState(
        NetworkPlayer hostPlayer,
        NetworkPlayer clientPlayer,
        float hostForwardDistance,
        float clientForwardDistance,
        float handDistance,
        bool hostReady,
        bool clientReady,
        bool handsContacted,
        bool holding)
    {
        if (!logActivationDebug || Time.unscaledTime < nextDebugLogTime)
            return;

        nextDebugLogTime = Time.unscaledTime + debugLogInterval;
        Vector3 hostLocal = GetLocalHandOffset(hostPlayer);
        Vector3 clientLocal = GetLocalHandOffset(clientPlayer);
        Debug.Log(
            $"[Cooperative Activation] host(trigger={hostPlayer.IsActivationTriggerHeld}, forward={hostForwardDistance:F2}, ready={hostReady}, local=({hostLocal.x:F2},{hostLocal.y:F2},{hostLocal.z:F2})) " +
            $"client(trigger={clientPlayer.IsActivationTriggerHeld}, forward={clientForwardDistance:F2}, ready={clientReady}, local=({clientLocal.x:F2},{clientLocal.y:F2},{clientLocal.z:F2})) " +
            $"hands(distance={handDistance:F2}/{handContactDistance:F2}, contact={handsContacted}) " +
            $"holding={holding}, hold={holdElapsed:F2}/{holdDuration:F2}, success={hostPlayer.HasCooperativeActivationSucceeded}", this);
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
