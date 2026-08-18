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
    private NetworkPlayer cachedMental;
    private NetworkPlayer cachedEar;
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

        if (!TryResolvePair(out NetworkPlayer mentalPlayer, out NetworkPlayer earPlayer) ||
            owner != mentalPlayer)
            return;
        
        float handDistance = GetClosestHandPair(mentalPlayer, earPlayer, out Hand mentalHand, out Hand earHand);
        bool handsContacted = handDistance <= handContactDistance;
        bool mentalReady = mentalPlayer.IsTriggerHeld(mentalHand);
        bool earReady = earPlayer.IsTriggerHeld(earHand);
        bool bothReady = mentalReady && earReady;
        bool holding = handsContacted && bothReady;
        
        mentalPlayer.SetCooperativeContactHand(handsContacted ? mentalHand : Hand.None);
        earPlayer.SetCooperativeContactHand(handsContacted ? earHand : Hand.None);

        if (!holding)
        {
            holdElapsed = 0f;
            mentalPlayer.SetCooperativeHoldProgress(0f);
            earPlayer.SetCooperativeHoldProgress(0f);
            LogActivationState(
                mentalPlayer, handDistance, mentalHand, earHand, mentalReady, earReady, handsContacted, holding);
            return;
        }

        holdElapsed += Time.fixedDeltaTime;
        float holdProgress = holdDuration <= 0f ? 1f : Mathf.Clamp01(holdElapsed / holdDuration);
        mentalPlayer.SetCooperativeHoldProgress(holdProgress);
        earPlayer.SetCooperativeHoldProgress(holdProgress);
        LogActivationState(
            mentalPlayer, handDistance, mentalHand, earHand, mentalReady, earReady, handsContacted, holding);
        if (holdElapsed < holdDuration)
            return;
        
        mentalPlayer.SetCooperativeActivationSucceeded();
        earPlayer.SetCooperativeActivationSucceeded();
    }

    private void Update()
    {
        if (owner == null || !owner.IsLocalNetworkRig)
            return;

        bool succeeded = owner.HasCooperativeActivationSucceeded;
        Hand contactHand = owner.CooperativeContactHand;
        bool handsContacted = contactHand != Hand.None;
        float holdProgress = owner.CooperativeHoldProgress;
        XRNode hapticNode = contactHand == Hand.Left ? XRNode.LeftHand : XRNode.RightHand;

        // 1) 닿는 순간 — 스냅 펄스
        if (handsContacted && !previousContact && !succeeded)
            StartCoroutine(PlayPattern(contactSnapPattern, hapticNode));

        // 2) 유지 구간 — 하나의 패턴이 진행도(progress)에 따라 세기·간격을 변화
        bool charging = !succeeded && holdProgress > 0f && holdPattern != null;
        if (charging)
        {
            if (Time.unscaledTime >= nextHoldPulseTime)
            {
                SendHapticPulse(holdPattern.AmplitudeAtProgress(holdProgress), holdPattern.duration, hapticNode);
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
            StartCoroutine(PlayPattern(successPattern, hapticNode));
            onActivationSucceeded?.Invoke();
            ActivationSucceeded?.Invoke();
        }

        previousContact = handsContacted;
        previousSuccess = succeeded;
    }

    // 두 플레이어의 손 중 가장 가까운 쌍과 그 거리.
    private static float GetClosestHandPair(
        NetworkPlayer first, NetworkPlayer second, out Hand firstHand, out Hand secondHand)
    {
        firstHand = Hand.None;
        secondHand = Hand.None;
        float closest = float.PositiveInfinity;

        closest = ConsiderPair(closest, first, Hand.Right, second, Hand.Right, ref firstHand, ref secondHand);
        closest = ConsiderPair(closest, first, Hand.Right, second, Hand.Left, ref firstHand, ref secondHand);
        closest = ConsiderPair(closest, first, Hand.Left, second, Hand.Right, ref firstHand, ref secondHand);
        closest = ConsiderPair(closest, first, Hand.Left, second, Hand.Left, ref firstHand, ref secondHand);

        return closest;
    }

    private static float ConsiderPair(
        float current,
        NetworkPlayer first, Hand firstCandidate,
        NetworkPlayer second, Hand secondCandidate,
        ref Hand firstHand, ref Hand secondHand)
    {
        Transform a = first.GetHand(firstCandidate);
        Transform b = second.GetHand(secondCandidate);
        if (a == null || b == null)
            return current;

        float distance = Vector3.Distance(a.position, b.position);
        if (distance >= current)
            return current;

        firstHand = firstCandidate;
        secondHand = secondCandidate;
        return distance;
    }

    private void LogActivationState(
        NetworkPlayer mentalPlayer,
        float handDistance,
        Hand mentalHand,
        Hand earHand,
        bool mentalReady,
        bool earReady,
        bool handsContacted,
        bool holding)
    {
        if (!logActivationDebug || Time.unscaledTime < nextDebugLogTime)
            return;

        nextDebugLogTime = Time.unscaledTime + debugLogInterval;
        Debug.Log(
            $"[Cooperative Activation] host(hand={mentalHand}, ready={mentalReady}) " +
            $"client(hand={earHand}, ready={earReady}) " +
            $"hands(distance={handDistance:F2}/{handContactDistance:F2}, contact={handsContacted}) " +
            $"holding={holding}, hold={holdElapsed:F2}/{holdDuration:F2}, success={mentalPlayer.HasCooperativeActivationSucceeded}", this);
    }

    /// <summary>
    /// 캐시된 페어가 유효하면 스캔 없이 재사용하고, 무효일 때만 씬을 한 번 스캔한다.
    /// (상대가 아직 스폰 전이거나 접속이 끊긴 경우에만 다시 탐색)
    /// </summary>
    private bool TryResolvePair(out NetworkPlayer mentalPlayer, out NetworkPlayer earPlayer)
    {
        if (IsValidPlayer(cachedMental) && IsValidPlayer(cachedEar))
        {
            mentalPlayer = cachedMental;
            earPlayer = cachedEar;
            return true;
        }

        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsSortMode.None);
        if (TryGetPair(players, out mentalPlayer, out earPlayer))
        {
            cachedMental = mentalPlayer;
            cachedEar = earPlayer;
            return true;
        }

        cachedMental = null;
        cachedEar = null;
        return false;
    }

    private static bool IsValidPlayer(NetworkPlayer player)
    {
        return player != null && player.Object != null && player.Object.IsValid;
    }

    private static bool TryGetPair(
        NetworkPlayer[] players,
        out NetworkPlayer mentalPlayer,
        out NetworkPlayer earPlayer)
    {
        mentalPlayer = null;
        earPlayer = null;

        foreach (NetworkPlayer player in players)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            // 역할은 Lobby에서 정해져 NetworkPlayer.AssignedRole로 동기화된다.
            if (player.AssignedRole == Role.mental)
            {
                if (mentalPlayer == null)
                    mentalPlayer = player;
            }
            else if (earPlayer == null)
            {
                earPlayer = player;
            }
        }

        return mentalPlayer != null && earPlayer != null;
    }

    /// <summary>
    /// HapticPattern을 재생한다.
    /// </summary>
    private IEnumerator PlayPattern(HapticPattern pattern, XRNode node)
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

            SendHapticPulse(pattern.amplitude * curveMultiplier, pattern.duration, node);

            if (pulseIndex < pulseCount - 1)
                yield return new WaitForSeconds(pattern.interval);
        }
    }

    private static void SendHapticPulse(float amplitude, float duration, XRNode node)
    {
        amplitude = Mathf.Clamp01(amplitude);
        if (amplitude <= 0f || duration <= 0f)
            return;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities) && capabilities.supportsImpulse)
            device.SendHapticImpulse(0, amplitude, duration);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents()
    {
        ActivationSucceeded = null;
    }
}
