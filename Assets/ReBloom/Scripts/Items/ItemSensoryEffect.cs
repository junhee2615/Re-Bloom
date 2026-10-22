using UnityEngine;

/// <summary>
/// 아이템 효과가 돌고 있는 동안 <b>로컬 플레이어 자신의</b> 감각 제약을 풀어 둔다.
///
/// 배치: <see cref="PlayerItemInventory"/> 와 같은 오브젝트(Player_ear / Player_mental 프리팹).
/// <see cref="ItemPickupInput"/> 이 같은 자리에 있는 것과 같은 구성이다.
///
/// 책임 분리 —
///  · 소비·효과 시간은 <see cref="PlayerItemInventory"/> 가 네트워크 상태로 소유한다.
///  · 입력은 <see cref="ItemPickupInput"/> 이 담당한다.
///  · 이 컴포넌트는 그 네트워크 상태를 <b>읽어서 로컬 연출에 반영만</b> 한다.
///    아무 값도 되돌려 쓰지 않는다.
///
/// 왜 로컬 전용인가:
/// <see cref="ResonanceController"/> 의 시각·청각 제약은 기기마다 하나뿐인 싱글톤이
/// <c>RoleManager.LocalIsMental</c> / <c>LocalIsEar</c> 로 게이트해 적용하는
/// 로컬 연출이다. 따라서 <b>자기 아바타에서만</b> 오버라이드를 걸어야 하고,
/// 상대 아바타(원격 복제본)에서는 아무것도 하지 않아야 한다.
/// 상대가 아이템을 써도 내 화면의 제약은 바뀌지 않는다.
///
/// 종료 복구:
/// 효과가 끝나면 플래그만 내린다. 그 시점의 파트너 거리와 공명 해제 상태에 맞는 제약으로
/// <see cref="ResonanceController"/> 의 Update가 다시 수렴시키므로, 원래 값을 따로
/// 저장하지 않는다. (자세한 이유는 <c>ResonanceController.SetSensoryItemOverride</c> 주석)
///
/// 비정상 종료 대비:
/// 스테이지 전환으로 아바타가 파괴될 때 <see cref="OnDisable"/> 가 오버라이드를 내린다.
/// 이것을 빠뜨리면 제약이 영구히 풀린 채로 남는다 — 영속 리그는 씬을 넘어 살아 있기 때문이다.
/// </summary>
[RequireComponent(typeof(PlayerItemInventory))]
[AddComponentMenu("ReBloom/Items/Item Sensory Effect")]
public class ItemSensoryEffect : MonoBehaviour
{
    [Header("Debug")]
    [Tooltip("효과가 켜지고 꺼질 때 로그를 남긴다.")]
    [SerializeField] private bool logEffect = true;

    private PlayerItemInventory inventory;
    private NetworkPlayer owner;

    // 지금 ResonanceController에 걸어 둔 오버라이드. 역할이 늦게 확정되거나
    // 바뀌는 경우에도 정확히 그 역할의 플래그를 내리기 위해 기억한다.
    private Role appliedRole = Role.none;
    private bool appliedActive;

    // 어느 컨트롤러에 걸었는지. 싱글톤이 교체되면 새 인스턴스에 다시 걸어야 한다.
    // (플래그는 컨트롤러가 들고 있으므로, 인스턴스가 바뀌면 걸어 둔 것이 함께 사라진다)
    private ResonanceController appliedController;

    private void Awake()
    {
        inventory = GetComponent<PlayerItemInventory>();
        owner = GetComponent<NetworkPlayer>();
    }

    private void OnDisable()
    {
        ClearOverride("비활성화");
    }

    private void Update()
    {
        // 원격 플레이어의 아바타에서는 아무것도 하지 않는다.
        if (owner == null || !owner.IsLocalNetworkRig)
            return;

        ResonanceController controller = ResonanceController.Instance;

        if (controller == null)
            return;   // StartScene·전환 구간. 다음 프레임에 다시 본다.

        if (inventory == null)
            return;

        bool active = inventory.IsEffectActive;
        Role role = inventory.OwnerRole;

        // 컨트롤러가 교체되었으면 걸어 둔 것이 함께 사라졌으므로 다시 걸어야 한다.
        if (appliedController != controller)
        {
            appliedController = controller;
            appliedActive = false;
        }

        // 역할이 바뀌었는데 이전 역할로 걸어 둔 것이 남아 있으면 먼저 내린다.
        if (appliedActive && appliedRole != role)
        {
            controller.SetSensoryItemOverride(appliedRole, false);
            appliedActive = false;
        }

        // 역할이 아직 확정되지 않았으면 어떤 감각을 풀지 알 수 없다. 기다린다.
        if (role == Role.none)
        {
            appliedRole = role;
            return;
        }

        if (active == appliedActive && role == appliedRole)
            return;

        controller.SetSensoryItemOverride(role, active);

        if (logEffect)
        {
            Debug.Log(
                active
                    ? $"[Item] 효과 시작 - {ItemKindRules.DisplayName(inventory.AcquiredItem)}, " +
                      $"role={role}, {inventory.EffectRemainingSeconds:F1}초 남음"
                    : $"[Item] 효과 종료 - {ItemKindRules.DisplayName(inventory.AcquiredItem)}, " +
                      $"role={role}, 제약이 현재 거리 기준으로 복구됩니다",
                this);
        }

        appliedRole = role;
        appliedActive = active;
    }

    /// <summary>
    /// 걸어 둔 오버라이드를 내린다. 걸어 둔 것이 없으면 아무것도 하지 않는다.
    /// </summary>
    private void ClearOverride(string reason)
    {
        if (!appliedActive)
            return;

        // 걸어 둔 그 컨트롤러에 내린다. 이미 사라졌다면 플래그도 함께 사라졌으므로 할 일이 없다.
        ResonanceController controller = appliedController != null
            ? appliedController
            : ResonanceController.Instance;

        if (controller != null)
        {
            controller.SetSensoryItemOverride(appliedRole, false);

            if (logEffect)
                Debug.Log($"[Item] 효과 오버라이드 해제 ({reason}) - role={appliedRole}", this);
        }

        appliedActive = false;
    }
}
