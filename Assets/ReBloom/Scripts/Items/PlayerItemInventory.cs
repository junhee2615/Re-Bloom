using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 플레이어 한 명의 아이템 보유 상태. 네트워크 단일 진실 공급원이다.
///
/// 배치: <see cref="NetworkPlayer"/> 와 <b>같은 오브젝트</b>(Player_ear / Player_mental 프리팹).
/// <see cref="CooperativeActivationController"/> 가 같은 자리에 붙어 있는 것과 같은 구성이다.
/// 씬에 별도 NetworkObject를 두지 않고 플레이어 오브젝트를 통로로 쓰는 것은
/// 이 프로젝트의 기존 관례다. (NetworkPlayer.cs의 RequestRevivePlant / RequestRiverbedHit 주석 참고)
///
/// 권한 모델 —
///   · 쓰기는 <b>StateAuthority(호스트)만</b> 한다.
///   · 클라이언트는 <see cref="Rpc_RequestAcquire"/> 로 요청만 보낸다.
///   · 호스트가 역할·중복을 <b>다시 검증</b>한 뒤 <see cref="HeldItem"/> 을 쓴다.
///   · 복제된 값은 두 피어가 같이 읽으므로 월드 아이템 숨김도 저절로 맞는다.
///
/// 스테이지 전환 —
/// 아바타는 스테이지마다 재스폰되어 [Networked] 값이 사라진다. 그래서 호스트가
/// <see cref="ItemInventoryPersistence"/> 에 한 벌 남겨 두고 <see cref="Spawned"/> 에서 되돌린다.
///
/// 이번 단계에서는 "획득"까지만 다룬다. 사용(제약 일시 해제)과 손목 UI는 다음 단계다.
/// </summary>
[RequireComponent(typeof(NetworkPlayer))]
public class PlayerItemInventory : NetworkBehaviour
{
    /// <summary>
    /// 이 플레이어가 들고 있는 아이템. None이면 빈손. 사용(소비)하면 None이 된다.
    /// StateAuthority만 쓴다. 다른 피어는 복제된 값을 읽는다.
    /// </summary>
    [Networked] public ItemKind HeldItem { get; private set; }

    /// <summary>
    /// 이 플레이어가 <b>획득한 적 있는</b> 아이템. 사용한 뒤에도 지워지지 않는다(단조 증가).
    ///
    /// 왜 HeldItem과 따로 두는가:
    /// 월드 아이템 숨김(<see cref="GrabbableItem"/>)과 설명 UI(<see cref="ItemDescriptionBoard"/>)는
    /// "이미 가져간 아이템"을 계속 true로 봐야 한다. HeldItem만 보면 사용하는 순간
    /// 아이템이 월드에 되살아나고 설명 UI가 숨는다.
    /// <see cref="ItemInventoryPersistence"/> 는 호스트에만 있어 클라이언트에서는 읽을 수 없으므로,
    /// 두 피어가 같은 결론에 도달하도록 이 값을 복제한다.
    /// </summary>
    [Networked] public ItemKind AcquiredItem { get; private set; }

    /// <summary>
    /// 아이템 효과에 <b>남은 플레이 시간</b>(초). 0 이하면 효과 없음.
    ///
    /// 왜 "종료 시각"이 아니라 "남은 시간"인가:
    /// 종료 시각(<c>SimulationTime + 30</c>)은 두 가지를 못 한다.
    ///  ① 컷씬·로딩 중에도 시간이 흐른다 — 멈출 방법이 없다.
    ///  ② 아바타가 스테이지마다 재스폰되면 [Networked] 값이 사라지는데,
    ///     절대 시각은 남은 시간으로 환산해 저장하지 않으면 복원할 수 없다.
    /// 남은 시간으로 들고 있으면 <b>안 깎으면 그대로 멈추고</b>, 그 값을 그대로
    /// <see cref="ItemInventoryPersistence"/> 에 넘겨 다음 스테이지에서 이어 쓸 수 있다.
    ///
    /// 누가 깎는가: <b>StateAuthority(호스트)만</b> <see cref="FixedUpdateNetwork"/> 에서 깎는다.
    /// 클라이언트는 복제된 값을 읽기만 하므로 남은 시간을 늘릴 수 없다.
    /// </summary>
    [Networked] public float EffectRemainingSeconds { get; private set; }

    /// <summary>
    /// 지금의 정지 상태가 <b>연속으로</b> 얼마나 이어졌는지(초). 정상 플레이가 재개되면 0으로 돌아간다.
    ///
    /// 왜 월 클록 상한이 아니라 이것인가:
    /// 처음에는 "사용 시점 + 180초" 절대 상한을 뒀다. 그런데 요구 동작은 <b>실제 플레이 시간 30초</b>라
    /// 컷씬이 길면 그 상한이 먼저 만료돼 효과가 조기 종료된다. 정상적인 컷씬·로딩 때문에
    /// 효과가 끝나서는 안 되므로 상한의 기준을 바꿨다.
    ///
    /// 이제 상한은 "한 번의 정지가 <see cref="maxPauseSeconds"/> 를 넘지 않는다"다.
    /// 컷씬이 아무리 길어도 끝나기만 하면 0으로 리셋되므로 효과가 조기 종료되지 않고,
    /// 정지가 <b>영구히 끼면</b> 그 시점부터 다시 깎기 시작해 효과가 반드시 끝난다.
    /// </summary>
    [Networked] public float EffectPausedSeconds { get; private set; }

    /// <summary>이 기기의 로컬 플레이어 인벤토리. 없으면 null.</summary>
    public static PlayerItemInventory LocalInstance { get; private set; }

    // 월드 아이템이 "이미 누군가 가져갔는지" 물어볼 수 있도록 모든 인스턴스를 모아 둔다.
    private static readonly List<PlayerItemInventory> Instances = new List<PlayerItemInventory>();

    [Header("사용")]
    [Tooltip("아이템을 사용했을 때 제약이 풀려 있는 '정상 플레이' 시간(초). " +
             "컷씬과 씬 로딩 중에는 줄어들지 않는다.")]
    [SerializeField, Min(0.1f)] private float effectSeconds = 30f;

    [Tooltip("한 번의 정지(컷씬·로딩)가 이 시간(초)을 넘으면 정지가 끼었다고 보고 다시 깎기 시작한다. " +
             "정상적인 컷씬보다 충분히 길게 둔다 — 컷씬 때문에 효과가 조기 종료되면 안 된다. " +
             "0이면 상한 없음(정지가 끼면 효과가 영구 유지되므로 권장하지 않음).")]
    [SerializeField, Min(0f)] private float maxPauseSeconds = 300f;

    [Header("Debug")]
    [SerializeField] private bool logAcquire = true;

    [Tooltip("효과 타이머가 정지·재개될 때 로그를 남긴다. 매 프레임이 아니라 상태가 바뀔 때만 출력한다.")]
    [SerializeField] private bool logTimer = true;

    // 정지 로그를 한 번만 내기 위한 플래그. 재개되면 풀린다.
    private bool loggedPaused;

    private NetworkPlayer owner;

    /// <summary>지금 손에 들고 있는 아이템이 있는지. 사용하면 false가 된다.</summary>
    public bool HasItem => HeldItem != ItemKind.None;

    /// <summary>획득한 적 있는지. 사용한 뒤에도 true다.</summary>
    public bool HasEverAcquired => AcquiredItem != ItemKind.None;

    /// <summary>
    /// 사용(소비)을 마쳤는지. 획득 이력은 있는데 손에 없는 상태다.
    /// 효과가 진행 중이어도 true다 — "소비됨"은 손에서 없어진 시점에 확정된다.
    /// </summary>
    public bool IsConsumed => AcquiredItem != ItemKind.None && HeldItem == ItemKind.None;

    /// <summary>
    /// 아이템 효과가 지금 유효한지. 복제된 남은 시간만 보므로 모든 피어가 같은 결론을 낸다.
    /// 컷씬·로딩 중에는 남은 시간이 줄지 않을 뿐, 이 값은 계속 true다
    /// (제약 해제 상태를 유지하라는 요구).
    /// </summary>
    public bool IsEffectActive =>
        EffectRemainingSeconds > 0f &&
        Object != null && Object.IsValid;

    /// <summary>
    /// 효과의 총 지속 시간(초). 진행률 UI가 <c>1 - 남은시간/총시간</c> 을 계산할 때 쓴다.
    ///
    /// 프리팹에 직렬화된 값이라 스테이지가 바뀌어도 같다. 남은 시간만 영속하면
    /// 비율이 어긋나지 않는다.
    /// </summary>
    public float EffectTotalSeconds => effectSeconds;

    /// <summary>
    /// 지금 사용할 수 있는지. 손에 있고, 효과가 진행 중이지 않아야 한다.
    /// </summary>
    public bool CanUse => HasItem && !IsEffectActive;

    /// <summary>이 플레이어의 역할. 스폰 시 서버가 확정한 값이다.</summary>
    public Role OwnerRole => owner != null ? owner.AssignedRole : Role.none;

    private void Awake()
    {
        owner = GetComponent<NetworkPlayer>();
    }

    public override void Spawned()
    {
        base.Spawned();

        if (owner == null)
            owner = GetComponent<NetworkPlayer>();

        Instances.Add(this);

        if (owner != null && owner.IsLocalNetworkRig)
            LocalInstance = this;

        RestoreFromPersistence();
    }

    /// <summary>
    /// 남은 효과 시간을 깎는다. <b>StateAuthority(호스트)만</b> 값을 쓴다.
    ///
    /// 왜 호스트의 로컬 상태로 판정하는가:
    /// 컷씬 여부는 각 피어가 자기 <see cref="HardwareRig"/> 로 로컬 판정한다. 그 값을
    /// 클라이언트가 RPC로 보고하게 하면 "일시정지 요청을 그대로 믿는" 구조가 되어
    /// 클라이언트가 효과를 무한정 늘릴 수 있다. 그래서 호스트는 <b>자기 판정만</b> 쓴다.
    ///
    /// 그래도 정확한 이유:
    /// 컷씬 5개는 모두 복제된 미션 상태로 시작하고 모든 피어에서 실행된다
    /// (Stage1 열차는 <c>RPC_BeginDeparture(RpcTargets.All)</c>, 나머지는 네트워크 상태 구독).
    /// 씬 로딩도 Fusion이 양쪽에서 동시에 수행한다. 따라서 호스트의 로컬 판정은 두 플레이어
    /// 모두에게 사실상 같고, 어긋나는 폭은 수 프레임 수준이다(30초 예산 대비 무시 가능).
    ///
    /// 왜 FixedUpdateNetwork인가:
    /// <c>Runner.DeltaTime</c> 은 네트워크 틱 간격이라 피어와 프레임레이트에 무관하다.
    /// Update의 <c>Time.deltaTime</c> 으로 깎으면 기기 성능에 따라 효과 길이가 달라진다.
    /// </summary>
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (EffectRemainingSeconds <= 0f)
            return;

        bool gameplayActive = ItemEffectClock.IsGameplayActive;

        // ① 정상 플레이가 아니면 깎지 않는다 — 컷씬·로딩 중 일시정지.
        if (!gameplayActive)
        {
            float paused = EffectPausedSeconds + Runner.DeltaTime;

            // 정지가 영구히 끼는 경우의 탈출구. 정상적인 컷씬보다 충분히 긴 값이라
            // 컷씬 때문에 여기에 걸리지는 않는다.
            if (maxPauseSeconds > 0f && paused >= maxPauseSeconds)
            {
                Debug.LogWarning(
                    $"[Item] 정지가 {maxPauseSeconds:F0}초를 넘겨 타이머를 다시 진행합니다. " +
                    $"컷씬이 비정상 종료됐을 수 있습니다. " +
                    $"남은 {EffectRemainingSeconds:F1}s / clock: {ItemEffectClock.DescribeState()}", this);

                EffectPausedSeconds = 0f;
                // 아래로 내려가 이번 틱부터 다시 깎는다.
            }
            else
            {
                if (logTimer && !loggedPaused)
                {
                    loggedPaused = true;
                    Debug.Log(
                        $"[Item] 타이머 정지 - 남은 {EffectRemainingSeconds:F1}s, " +
                        $"authority={HasStateAuthority} / clock: {ItemEffectClock.DescribeState()}", this);
                }

                EffectPausedSeconds = paused;
                return;
            }
        }
        else if (EffectPausedSeconds > 0f)
        {
            if (logTimer)
            {
                Debug.Log(
                    $"[Item] 타이머 재개 - {EffectPausedSeconds:F1}초 정지했고 남은 " +
                    $"{EffectRemainingSeconds:F1}s 부터 다시 감소합니다.", this);
            }

            EffectPausedSeconds = 0f;
        }

        loggedPaused = false;

        float remaining = EffectRemainingSeconds - Runner.DeltaTime;

        if (remaining <= 0f)
        {
            if (logAcquire)
            {
                Debug.Log(
                    $"[Item] 효과 종료 - {ItemKindRules.DisplayName(AcquiredItem)} " +
                    $"플레이 시간 {effectSeconds:F0}초를 모두 사용했습니다.", this);
            }

            EndEffect();
            return;
        }

        EffectRemainingSeconds = remaining;

        // 스테이지 전환을 넘기기 위해 호스트 쪽 기록을 함께 갱신한다.
        // Dictionary 대입 한 번이라 틱마다 해도 부담이 없다.
        ItemInventoryPersistence.SetEffectRemaining(
            Object.InputAuthority, remaining, EffectPausedSeconds);
    }

    /// <summary>효과를 끝낸다. StateAuthority에서만 호출된다.</summary>
    private void EndEffect()
    {
        EffectRemainingSeconds = 0f;
        EffectPausedSeconds = 0f;

        ItemInventoryPersistence.SetEffectRemaining(Object.InputAuthority, 0f, 0f);
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        Instances.Remove(this);

        if (LocalInstance == this)
            LocalInstance = null;
    }

    /// <summary>
    /// 스테이지 전환으로 아바타가 다시 스폰됐을 때 보유 상태를 되돌린다.
    ///
    /// 호스트만 수행한다 — 클라이언트는 복제로 같은 값을 받는다.
    /// 이미 값이 들어 있으면(같은 스테이지 안에서의 재호출 등) 건드리지 않아
    /// 중복 복원이나 덮어쓰기가 일어나지 않는다.
    /// </summary>
    private void RestoreFromPersistence()
    {
        if (!HasStateAuthority)
            return;

        if (HeldItem != ItemKind.None || AcquiredItem != ItemKind.None)
            return;   // 같은 스테이지 안에서의 재호출 등 — 이미 값이 있으면 건드리지 않는다

        PlayerRef player = Object.InputAuthority;

        // ① 획득 이력 — 소비한 뒤에도 남아 있어야 월드 아이템이 되살아나지 않는다.
        if (ItemInventoryPersistence.TryGetAcquired(player, out ItemKind acquired) &&
            acquired != ItemKind.None)
        {
            AcquiredItem = acquired;
        }

        // ② 현재 보유 — 소비했다면 기록이 없으므로 빈손으로 복원된다.
        if (ItemInventoryPersistence.TryGet(player, out ItemKind held) &&
            held != ItemKind.None)
        {
            HeldItem = held;
        }

        // ③ 남은 효과 시간 — 스테이지를 넘어 이어진다.
        //
        // 남은 시간으로 저장해 두었으므로 전환에 걸린 실제 시간과 무관하게 그대로 이어진다.
        // 정지 누적 시간도 함께 복원한다 (전환은 그 자체로 정지 구간이다).
        if (ItemInventoryPersistence.TryGetEffect(player, out float remaining, out float paused) &&
            remaining > 0f)
        {
            EffectRemainingSeconds = remaining;
            EffectPausedSeconds = paused;
        }

        if (logAcquire && (AcquiredItem != ItemKind.None || HeldItem != ItemKind.None))
        {
            Debug.Log(
                $"[Item] 상태 복원 - player={player.PlayerId}, " +
                $"보유={ItemKindRules.DisplayName(HeldItem)}, " +
                $"획득이력={ItemKindRules.DisplayName(AcquiredItem)}, " +
                $"남은 효과={EffectRemainingSeconds:F1}s", this);
        }
    }

    // ── 획득 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 로컬 플레이어가 획득을 요청한다. 권한 여부에 따라 직접 처리하거나 호스트에 넘긴다.
    ///
    /// NetworkPlayer의 RequestRevivePlant / RequestRiverbedHit 과 같은 관용구다.
    /// </summary>
    public void RequestAcquire(ItemKind kind)
    {
        if (kind == ItemKind.None)
            return;

        if (Object == null || !Object.IsValid)
        {
            Debug.LogWarning("[Item] 아직 네트워크에 연결되지 않아 획득을 보낼 수 없습니다.", this);
            return;
        }

        if (HasStateAuthority)
            ApplyAcquire(kind);
        else
            Rpc_RequestAcquire(kind);
    }

    /// <summary>클라이언트 → 호스트 요청. 판정은 호스트가 한다.</summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestAcquire(ItemKind kind, RpcInfo info = default)
    {
        ApplyAcquire(kind);
    }

    /// <summary>
    /// 호스트 전용 획득 판정. 로컬에서 이미 걸렀더라도 여기서 <b>다시</b> 검증한다.
    /// (클라이언트를 신뢰하지 않는다 — 조작된 요청이 와도 통과하지 않는다)
    /// </summary>
    private void ApplyAcquire(ItemKind kind)
    {
        if (!HasStateAuthority)
            return;

        // ① 중복 획득 방지 — 이미 들고 있으면 거절
        if (HeldItem != ItemKind.None)
        {
            if (logAcquire)
                Debug.Log($"[Item] 획득 거절 - 이미 {ItemKindRules.DisplayName(HeldItem)}을 보유 중", this);
            return;
        }

        // ② 역할 검사 — 에코는 보청기, 루멘은 약통만
        Role role = OwnerRole;

        if (!ItemKindRules.CanAcquire(role, kind))
        {
            if (logAcquire)
            {
                Debug.Log(
                    $"[Item] 획득 거절 - 역할 불일치 (role={role}, " +
                    $"item={ItemKindRules.DisplayName(kind)})", this);
            }
            return;
        }

        // ③ 다른 플레이어가 이미 가져간 종류인지
        if (IsKindTaken(kind))
        {
            if (logAcquire)
                Debug.Log($"[Item] 획득 거절 - {ItemKindRules.DisplayName(kind)}은 이미 사용됨", this);
            return;
        }

        HeldItem = kind;
        AcquiredItem = kind;   // 소비 후에도 남는 획득 이력

        // 스테이지 전환을 넘기기 위해 호스트 쪽에 한 벌 남긴다. (보유 + 획득 이력)
        ItemInventoryPersistence.Set(Object.InputAuthority, kind);

        if (logAcquire)
        {
            Debug.Log(
                $"[Item] 획득 성공 - player={Object.InputAuthority.PlayerId}, role={role}, " +
                $"item={ItemKindRules.DisplayName(kind)}", this);
        }
    }

    // ── 사용 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 로컬 플레이어가 사용을 요청한다. 권한 여부에 따라 직접 처리하거나 호스트에 넘긴다.
    /// <see cref="RequestAcquire"/> 와 같은 관용구다.
    /// </summary>
    public void RequestUse()
    {
        if (Object == null || !Object.IsValid)
        {
            Debug.LogWarning("[Item] 아직 네트워크에 연결되지 않아 사용을 보낼 수 없습니다.", this);
            return;
        }

        if (HasStateAuthority)
            ApplyUse();
        else
            Rpc_RequestUse();
    }

    /// <summary>
    /// 클라이언트 → 호스트 사용 요청. 판정은 호스트가 한다.
    ///
    /// <c>RpcSources.InputAuthority</c> 라서 <b>그 아이템을 가진 플레이어만</b> 보낼 수 있다.
    /// 다른 플레이어의 인벤토리에 사용 요청을 보내는 것은 Fusion이 막는다.
    /// </summary>
    [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
    private void Rpc_RequestUse(RpcInfo info = default)
    {
        ApplyUse();
    }

    /// <summary>
    /// 호스트 전용 사용 판정. 로컬에서 이미 걸렀더라도 여기서 <b>다시</b> 검증한다.
    /// 중복 요청(연타, RPC 중복 도착)은 ①②에서 막힌다.
    /// </summary>
    private void ApplyUse()
    {
        if (!HasStateAuthority)
            return;

        // ① 손에 없으면 쓸 것이 없다. 이미 소비했어도 여기서 걸린다.
        if (HeldItem == ItemKind.None)
        {
            if (logAcquire)
            {
                Debug.Log(
                    $"[Item] 사용 거절 - 손에 아이템이 없습니다. " +
                    $"(획득이력={ItemKindRules.DisplayName(AcquiredItem)})", this);
            }
            return;
        }

        // ② 이미 효과가 돌고 있으면 중복 사용이다.
        if (IsEffectActive)
        {
            if (logAcquire)
                Debug.Log($"[Item] 사용 거절 - 이미 효과가 진행 중입니다 ({EffectRemainingSeconds:F1}s 남음)", this);

            return;
        }

        ItemKind used = HeldItem;

        // 소비 확정 — 손에서 비우고 남은 효과 시간을 채운다.
        // AcquiredItem은 건드리지 않는다(월드 아이템·설명 UI 판정의 기준).
        HeldItem = ItemKind.None;
        EffectRemainingSeconds = effectSeconds;
        EffectPausedSeconds = 0f;

        ItemInventoryPersistence.MarkConsumed(Object.InputAuthority);
        ItemInventoryPersistence.SetEffectRemaining(
            Object.InputAuthority, EffectRemainingSeconds, EffectPausedSeconds);

        if (logAcquire)
        {
            Debug.Log(
                $"[Item] 사용 성공 - player={Object.InputAuthority.PlayerId}, " +
                $"item={ItemKindRules.DisplayName(used)}, 플레이 시간 {effectSeconds:F0}초 동안 제약 해제 " +
                $"(정지 상한 {maxPauseSeconds:F0}s). " +
                $"clock: {ItemEffectClock.DescribeState()}", this);
        }
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 이 종류의 아이템을 누군가 <b>가져간 적 있는지</b>. 월드에 놓인 아이템이 자신을 숨길지,
    /// 설명 UI를 띄울지 판정할 때 쓴다.
    ///
    /// 복제된 <see cref="AcquiredItem"/> 을 보므로 두 피어가 같은 결론에 도달하고,
    /// <b>사용(소비)한 뒤에도 계속 true</b>다. HeldItem을 보면 사용하는 순간 false가 되어
    /// 월드 아이템이 되살아나고 설명 UI가 숨어 버린다.
    ///
    /// 아직 스폰 전이라 인스턴스가 비어 있는 프레임에는 호스트가 남긴 영속 기록을 함께 본다
    /// (스테이지 전환 직후 아이템이 한 프레임 깜빡이는 것을 막는다).
    /// </summary>
    public static bool IsKindTaken(ItemKind kind)
    {
        if (kind == ItemKind.None)
            return false;

        foreach (PlayerItemInventory inventory in Instances)
        {
            if (inventory == null || inventory.Object == null || !inventory.Object.IsValid)
                continue;

            if (inventory.AcquiredItem == kind)
                return true;
        }

        return ItemInventoryPersistence.IsKindTaken(kind);
    }
}
