using System;
using System.Collections;
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
    [SerializeField, Min(0.01f)] private float handContactDistance = 0.16f;
    [SerializeField, Min(0f)] private float holdDuration = 1.5f;

    [Header("Debug")]
    [Tooltip("활성화 판정 상태를 주기적으로 출력합니다.")]
    [SerializeField] private bool logActivationDebug = true;
    [SerializeField, Min(0.05f)] private float debugLogInterval = 0.25f;

    [Header("Feedback")]
    [SerializeField] private HapticPattern contactHapticPattern;
    [SerializeField] private UnityEvent onActivationSucceeded;

    private NetworkPlayer owner;
    private NetworkPlayer cachedHost;
    private NetworkPlayer cachedClient;
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

        if (!TryResolvePair(out NetworkPlayer hostPlayer, out NetworkPlayer clientPlayer) ||
            owner != hostPlayer)
            return;
        
        bool hostReady = IsActivationReady(hostPlayer);
        bool clientReady = IsActivationReady(clientPlayer);
        float handDistance = GetHandDistance(hostPlayer, clientPlayer);
        bool handsContacted = handDistance <= handContactDistance;
        bool bothReady = hostReady && clientReady;
        bool holding = handsContacted && bothReady;
        
        hostPlayer.SetCooperativeHandsContacted(handsContacted);
        clientPlayer.SetCooperativeHandsContacted(handsContacted);

        if (!holding)
        {
            holdElapsed = 0f;
            LogActivationState(
                hostPlayer, clientPlayer, handDistance, hostReady, clientReady, handsContacted, holding);
            return;
        }

        holdElapsed += Time.fixedDeltaTime;
        LogActivationState(
            hostPlayer, clientPlayer, handDistance, hostReady, clientReady, handsContacted, holding);
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
        return player.IsActivationTriggerHeld;
    }

    private static float GetHandDistance(NetworkPlayer first, NetworkPlayer second)
    {
        if (first.RightHandTransform == null || second.RightHandTransform == null)
            return float.PositiveInfinity;

        return Vector3.Distance(first.RightHandTransform.position, second.RightHandTransform.position);
    }

    private void LogActivationState(
        NetworkPlayer hostPlayer,
        NetworkPlayer clientPlayer,
        float handDistance,
        bool hostReady,
        bool clientReady,
        bool handsContacted,
        bool holding)
    {
        if (!logActivationDebug || Time.unscaledTime < nextDebugLogTime)
            return;

        nextDebugLogTime = Time.unscaledTime + debugLogInterval;
        Debug.Log(
            $"[Cooperative Activation] host(trigger={hostPlayer.IsActivationTriggerHeld}, ready={hostReady}) " +
            $"client(trigger={clientPlayer.IsActivationTriggerHeld}, ready={clientReady}) " +
            $"hands(distance={handDistance:F2}/{handContactDistance:F2}, contact={handsContacted}) " +
            $"holding={holding}, hold={holdElapsed:F2}/{holdDuration:F2}, success={hostPlayer.HasCooperativeActivationSucceeded}", this);
    }

    /// <summary>
    /// 캐시된 페어가 유효하면 스캔 없이 재사용하고, 무효일 때만 씬을 한 번 스캔한다.
    /// (상대가 아직 스폰 전이거나 접속이 끊긴 경우에만 다시 탐색)
    /// </summary>
    private bool TryResolvePair(out NetworkPlayer hostPlayer, out NetworkPlayer clientPlayer)
    {
        if (IsValidPlayer(cachedHost) && IsValidPlayer(cachedClient))
        {
            hostPlayer = cachedHost;
            clientPlayer = cachedClient;
            return true;
        }

        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        if (TryGetPair(players, out hostPlayer, out clientPlayer))
        {
            cachedHost = hostPlayer;
            cachedClient = clientPlayer;
            return true;
        }

        cachedHost = null;
        cachedClient = null;
        return false;
    }

    private static bool IsValidPlayer(NetworkPlayer player)
    {
        return player != null && player.Object != null && player.Object.IsValid;
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
            float curveMultiplier = contactHapticPattern.amplitudeCurve == null ||
                                    contactHapticPattern.amplitudeCurve.length == 0
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
