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
    private Hand activeMentalHand;
    private Hand activeEarHand;
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

        if (!TryResolvePair(out NetworkPlayer mentalPlayer, out NetworkPlayer earPlayer))
        {
            holdElapsed = 0f;
            activeMentalHand = Hand.None;
            activeEarHand = Hand.None;
            owner.SetCooperativeContactHand(Hand.None);
            owner.SetCooperativeHoldProgress(0f);
            return;
        }

        if (owner != mentalPlayer)
            return;

        bool hadActivePair = activeMentalHand != Hand.None && activeEarHand != Hand.None;
        float handDistance = float.PositiveInfinity;
        bool activePairValid = hadActivePair &&
            IsValidActivationPair(mentalPlayer, activeMentalHand, earPlayer, activeEarHand, out handDistance);

        bool holding = activePairValid;
        bool pairChanged = false;
        Hand mentalHand = activePairValid ? activeMentalHand : Hand.None;
        Hand earHand = activePairValid ? activeEarHand : Hand.None;

        if (!activePairValid)
        {
            holding = GetClosestValidHandPair(
                mentalPlayer, earPlayer, out mentalHand, out earHand, out handDistance);

            if (holding)
            {
                pairChanged = hadActivePair &&
                    (mentalHand != activeMentalHand || earHand != activeEarHand);
                activeMentalHand = mentalHand;
                activeEarHand = earHand;
            }
            else
            {
                activeMentalHand = Hand.None;
                activeEarHand = Hand.None;
            }
        }

        bool handsContacted = holding;
        bool mentalReady;
        bool earReady;

        if (!holding)
        {
            handDistance = GetClosestHandPair(
                mentalPlayer, earPlayer, out mentalHand, out earHand);
            handsContacted = handDistance <= handContactDistance;
            mentalReady = mentalPlayer.IsTriggerHeld(mentalHand);
            earReady = earPlayer.IsTriggerHeld(earHand);
        }
        else
        {
            mentalReady = true;
            earReady = true;
        }

        mentalPlayer.SetCooperativeContactHand(handsContacted ? mentalHand : Hand.None);
        earPlayer.SetCooperativeContactHand(handsContacted ? earHand : Hand.None);

        if (pairChanged)
        {
            holdElapsed = 0f;
            mentalPlayer.SetCooperativeHoldProgress(0f);
            earPlayer.SetCooperativeHoldProgress(0f);
            LogActivationState(
                mentalPlayer, handDistance, mentalHand, earHand, mentalReady, earReady, handsContacted, holding);
            return;
        }

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
            StartCoroutine(HapticPatternPlayer.Play(contactSnapPattern, hapticNode));

        // 2) 유지 구간 — 하나의 패턴이 진행도(progress)에 따라 세기·간격을 변화
        bool charging = !succeeded && holdProgress > 0f && holdPattern != null;
        if (charging)
        {
            if (Time.unscaledTime >= nextHoldPulseTime)
            {
                HapticPatternPlayer.Pulse(holdPattern.AmplitudeAtProgress(holdProgress), holdPattern.duration, hapticNode);
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
            StartCoroutine(HapticPatternPlayer.Play(successPattern, hapticNode));
            onActivationSucceeded?.Invoke();
            ActivationSucceeded?.Invoke();
        }

        previousContact = handsContacted;
        previousSuccess = succeeded;
    }

    // 두 플레이어의 손 중 가장 가까운 쌍과 그 거리(접촉 햅틱·로그용).
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

    // 접촉 거리와 양쪽 트리거 입력을 모두 만족하는 가장 가까운 조합.
    private bool GetClosestValidHandPair(
        NetworkPlayer first, NetworkPlayer second,
        out Hand firstHand, out Hand secondHand, out float closest)
    {
        firstHand = Hand.None;
        secondHand = Hand.None;
        closest = float.PositiveInfinity;

        ConsiderValidPair(first, Hand.Right, second, Hand.Right, ref firstHand, ref secondHand, ref closest);
        ConsiderValidPair(first, Hand.Left, second, Hand.Left, ref firstHand, ref secondHand, ref closest);
        ConsiderValidPair(first, Hand.Right, second, Hand.Left, ref firstHand, ref secondHand, ref closest);
        ConsiderValidPair(first, Hand.Left, second, Hand.Right, ref firstHand, ref secondHand, ref closest);

        return firstHand != Hand.None && secondHand != Hand.None;
    }

    private void ConsiderValidPair(
        NetworkPlayer first, Hand firstCandidate,
        NetworkPlayer second, Hand secondCandidate,
        ref Hand firstHand, ref Hand secondHand, ref float closest)
    {
        Transform a = first.GetHand(firstCandidate);
        Transform b = second.GetHand(secondCandidate);
        if (a == null || b == null)
            return;

        float distance = Vector3.Distance(a.position, b.position);
        if (distance > handContactDistance ||
            !first.IsTriggerHeld(firstCandidate) ||
            !second.IsTriggerHeld(secondCandidate) ||
            distance >= closest)
            return;

        closest = distance;
        firstHand = firstCandidate;
        secondHand = secondCandidate;
    }

    private bool IsValidActivationPair(
        NetworkPlayer first, Hand firstHand, NetworkPlayer second, Hand secondHand, out float distance)
    {
        distance = float.PositiveInfinity;

        Transform a = first.GetHand(firstHand);
        Transform b = second.GetHand(secondHand);
        if (a == null || b == null)
            return false;

        distance = Vector3.Distance(a.position, b.position);
        return distance <= handContactDistance &&
            first.IsTriggerHeld(firstHand) &&
            second.IsTriggerHeld(secondHand);
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

    // 패턴 재생은 HapticPatternPlayer로 옮겼다 — 여기 private으로 묶여 있어서
    // HapticTrigger 같은 다른 사용처가 세기·길이를 하드코딩할 수밖에 없었다.
    // 동작은 그대로이므로 이 컨트롤러의 체감은 바뀌지 않는다.

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents()
    {
        ActivationSucceeded = null;
    }
}
