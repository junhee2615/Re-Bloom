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
    [Tooltip("닿는 순간의 펄스.")]
    [SerializeField] private HapticPattern contactSnapPattern;
    [Tooltip("홀드 진동.")]
    [SerializeField] private HapticPattern holdPattern;
    [Tooltip("성공 순간의 릴리즈 펄스.")]
    [SerializeField] private HapticPattern successPattern;

    [Header("Events")]
    [SerializeField] private UnityEvent onActivationSucceeded;

    private NetworkPlayer owner;
    private NetworkPlayer cachedHost;
    private NetworkPlayer cachedClient;
    private float holdElapsed;
    private bool previousContact;
    private bool previousSuccess;
    private float nextDebugLogTime;
    private float nextHoldPulseTime;

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
            hostPlayer.SetCooperativeHoldProgress(0f);
            clientPlayer.SetCooperativeHoldProgress(0f);
            LogActivationState(
                hostPlayer, clientPlayer, handDistance, hostReady, clientReady, handsContacted, holding);
            return;
        }

        holdElapsed += Time.fixedDeltaTime;
        float holdProgress = holdDuration <= 0f ? 1f : Mathf.Clamp01(holdElapsed / holdDuration);
        hostPlayer.SetCooperativeHoldProgress(holdProgress);
        clientPlayer.SetCooperativeHoldProgress(holdProgress);
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

        bool succeeded = owner.HasCooperativeActivationSucceeded;
        bool handsContacted = owner.AreCooperativeHandsContacted;
        float holdProgress = owner.CooperativeHoldProgress;

        // 1) 닿는 순간 — 스냅 펄스
        if (handsContacted && !previousContact && !succeeded)
            StartCoroutine(PlayPattern(contactSnapPattern));

        // 2) 유지 구간 — 하나의 패턴이 진행도(progress)에 따라 세기·간격을 변화
        bool charging = !succeeded && holdProgress > 0f && holdPattern != null;
        if (charging)
        {
            if (Time.unscaledTime >= nextHoldPulseTime)
            {
                SendHapticPulse(holdPattern.AmplitudeAtProgress(holdProgress), holdPattern.duration);
                nextHoldPulseTime = Time.unscaledTime + holdPattern.IntervalAtProgress(holdProgress);
            }
        }
        else
        {
            // 다음 충전이 시작되면 즉시 첫 펄스가 울리도록 리셋
            nextHoldPulseTime = 0f;
        }

        // 3) 성공 순간 — 릴리즈 펄스
        if (succeeded && !previousSuccess)
        {
            StartCoroutine(PlayPattern(successPattern));
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
        if (first.RightHand == null || second.RightHand == null)
            return float.PositiveInfinity;

        return Vector3.Distance(first.RightHand.position, second.RightHand.position);
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

    /// <summary>
    /// HapticPattern을 재생한다.
    /// </summary>
    private IEnumerator PlayPattern(HapticPattern pattern)
    {
        if (pattern == null)
            yield break;

        int pulseCount = Mathf.Max(1, pattern.pulseCount);
        for (int pulseIndex = 0; pulseIndex < pulseCount; pulseIndex++)
        {
            float normalizedPulse = pulseCount == 1 ? 1f : (float)pulseIndex / (pulseCount - 1);
            float curveMultiplier = pattern.amplitudeCurve == null || pattern.amplitudeCurve.length == 0
                ? 1f
                : pattern.amplitudeCurve.Evaluate(normalizedPulse);

            SendHapticPulse(pattern.amplitude * curveMultiplier, pattern.duration);

            if (pulseIndex < pulseCount - 1)
                yield return new WaitForSeconds(pattern.interval);
        }
    }

    private static void SendHapticPulse(float amplitude, float duration)
    {
        amplitude = Mathf.Clamp01(amplitude);
        if (amplitude <= 0f || duration <= 0f)
            return;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand); // 닿은 손 기준으로 수정 예정
        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
            device.SendHapticImpulse(0, amplitude, duration);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents()
    {
        ActivationSucceeded = null;
    }
}
