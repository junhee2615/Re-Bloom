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
    /// 이 플레이어가 들고 있는 아이템. None이면 빈손.
    /// StateAuthority만 쓴다. 다른 피어는 복제된 값을 읽는다.
    /// </summary>
    [Networked] public ItemKind HeldItem { get; private set; }

    /// <summary>이 기기의 로컬 플레이어 인벤토리. 없으면 null.</summary>
    public static PlayerItemInventory LocalInstance { get; private set; }

    // 월드 아이템이 "이미 누군가 가져갔는지" 물어볼 수 있도록 모든 인스턴스를 모아 둔다.
    private static readonly List<PlayerItemInventory> Instances = new List<PlayerItemInventory>();

    [Header("Debug")]
    [SerializeField] private bool logAcquire = true;

    private NetworkPlayer owner;

    public bool HasItem => HeldItem != ItemKind.None;

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

        if (HeldItem != ItemKind.None)
            return;

        if (!ItemInventoryPersistence.TryGet(Object.InputAuthority, out ItemKind saved))
            return;

        if (saved == ItemKind.None)
            return;

        HeldItem = saved;

        if (logAcquire)
        {
            Debug.Log(
                $"[Item] 보유 상태 복원 - player={Object.InputAuthority.PlayerId}, " +
                $"item={ItemKindRules.DisplayName(saved)}", this);
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

        // 스테이지 전환을 넘기기 위해 호스트 쪽에 한 벌 남긴다.
        ItemInventoryPersistence.Set(Object.InputAuthority, kind);

        if (logAcquire)
        {
            Debug.Log(
                $"[Item] 획득 성공 - player={Object.InputAuthority.PlayerId}, role={role}, " +
                $"item={ItemKindRules.DisplayName(kind)}", this);
        }
    }

    // ── 조회 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 이 종류의 아이템을 누군가 들고 있는지. 월드에 놓인 아이템이 자신을 숨길지 판정할 때 쓴다.
    ///
    /// 복제된 <see cref="HeldItem"/> 을 보므로 두 피어가 같은 결론에 도달한다.
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

            if (inventory.HeldItem == kind)
                return true;
        }

        return ItemInventoryPersistence.IsKindTaken(kind);
    }
}
