using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// 왼손 Y 버튼으로 아이템을 획득하는 입력. 로컬 플레이어에서만 동작한다.
///
/// 배치: <see cref="PlayerItemInventory"/> 와 같은 오브젝트(Player_ear / Player_mental 프리팹).
/// 아바타는 스테이지마다 스폰되므로 Stage1·2·3 어디서나 자동으로 존재한다.
/// XR Origin 프리팹을 건드리지 않기 위해 이쪽에 둔다.
///
/// 왜 Y(secondaryButton)인가:
/// 왼손 X(primaryButton)는 UIPanel이 미션 패널 토글에 쓰고 있다. 왼손 Y는
/// OpeningSkipInput이 Opening 씬에서만 쓰며, 그 스크립트는 Opening 씬 소유라
/// 스테이지에서는 존재하지 않는다. XRI Input Action 에셋에는 왼손
/// primaryButton/secondaryButton 바인딩이 아예 없으므로 액션 충돌도 없다.
///
/// Down 엣지만 처리하는 이유:
/// TryGetFeatureValue는 누르고 있는 동안 매 프레임 true를 돌려준다. 그대로 쓰면
/// 한 번 누름이 수십 번의 획득 요청이 된다. UIPanel의 previousXButton,
/// OpeningSkipInput의 skipSent와 같은 방식으로 상태 전이만 본다.
///
/// 획득과 사용을 같은 Y 버튼이 담당한다. 분기는 "손에 들고 있는가" 하나로 정한다 —
/// 빈손이면 획득 시도, 들고 있으면 사용 시도다.
///
/// 획득 입력이 그대로 사용으로 이어지지 않게 하는 장치가 셋이다.
///  ① <b>Down 엣지</b>만 처리하므로 한 번 누름은 한 번의 동작만 만든다.
///     획득으로 소비된 그 누름은 사용 판정까지 가지 않는다.
///  ② <see cref="releaseRequiredBeforeUse"/> — 획득 요청을 보낸 뒤에는 Y를 <b>완전히 놓을 때까지</b>
///     사용 입력을 받지 않는다. 클라이언트는 획득이 RPC 왕복 뒤에 확정되므로,
///     Down 엣지만으로는 "누른 채 기다리다 확정되는" 순간을 막지 못한다.
///  ③ <see cref="acquireCooldownSeconds"/> — 놓았다 바로 다시 누르는 연타로 1회용 아이템이
///     실수로 소모되는 것을 막는다.
/// </summary>
[RequireComponent(typeof(PlayerItemInventory))]
[AddComponentMenu("ReBloom/Items/Item Pickup Input")]
public class ItemPickupInput : MonoBehaviour
{
    [Header("입력")]
    [Tooltip("획득 직후 이 시간 동안 같은 버튼 입력을 받지 않는다. " +
             "같은 Y 버튼이 '사용'으로도 쓰이므로, 놓았다 바로 다시 누르는 연타로 " +
             "1회용 아이템이 실수로 소모되는 것을 막는다.")]
    [SerializeField, Min(0f)] private float acquireCooldownSeconds = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logInput = true;

    private PlayerItemInventory inventory;
    private NetworkPlayer owner;

    private InputDevice leftController;
    private bool previousYButton;
    private float cooldownRemaining;

    // 획득 요청을 보낸 뒤 켜진다. Y를 완전히 놓으면 꺼진다.
    // 켜져 있는 동안 사용 입력을 받지 않는다.
    private bool releaseRequiredBeforeUse;

    private void Awake()
    {
        inventory = GetComponent<PlayerItemInventory>();
        owner = GetComponent<NetworkPlayer>();
    }

    private void Update()
    {
        // 원격 플레이어의 아바타에서는 입력을 보지 않는다.
        if (owner == null || !owner.IsLocalNetworkRig)
            return;

        if (cooldownRemaining > 0f)
            cooldownRemaining -= Time.unscaledDeltaTime;

        if (!TryReadYButton(out bool pressed))
            return;

        bool pressedDown = pressed && !previousYButton;
        previousYButton = pressed;

        // 버튼을 완전히 놓았다 — 이제 사용 입력을 받을 수 있다.
        if (!pressed)
            releaseRequiredBeforeUse = false;

        if (!pressedDown)
            return;

        if (cooldownRemaining > 0f)
            return;

        HandleYPress();
    }

    /// <summary>
    /// Y Down 한 번을 처리한다. 손에 들고 있으면 사용, 빈손이면 획득이다.
    /// </summary>
    private void HandleYPress()
    {
        if (inventory.HasItem)
            TryUse();
        else
            TryAcquire();
    }

    /// <summary>
    /// 보유한 아이템을 사용한다. 판정은 호스트가 다시 한다.
    /// </summary>
    private void TryUse()
    {
        // 획득에 쓴 그 누름이 사용으로 이어지지 않게 한다.
        // (클라이언트는 획득이 RPC 왕복 뒤에 확정되므로 Down 엣지만으로는 부족하다)
        if (releaseRequiredBeforeUse)
        {
            if (logInput)
                Debug.Log("[Item] Y 입력 무시 - 획득 직후입니다. Y를 놓고 다시 누르세요.", this);

            return;
        }

        if (inventory.IsEffectActive)
        {
            if (logInput)
            {
                Debug.Log(
                    $"[Item] Y 입력 무시 - 이미 효과가 진행 중입니다 " +
                    $"({inventory.EffectRemainingSeconds:F1}초 남음)", this);
            }

            return;
        }

        if (logInput)
            Debug.Log($"[Item] 사용 요청 - {ItemKindRules.DisplayName(inventory.HeldItem)}", this);

        inventory.RequestUse();

        // 사용 요청 직후에도 같은 쿨다운을 둔다. 연타로 다음 동작이 바로 나가지 않게 한다.
        cooldownRemaining = acquireCooldownSeconds;
    }

    /// <summary>
    /// 왼손 Y 버튼 상태를 읽는다. 장치가 일시적으로 무효해지면(슬립/재연결)
    /// 이전 상태를 지워 다시 누를 때 Down 엣지가 제대로 잡히게 한다.
    /// </summary>
    private bool TryReadYButton(out bool pressed)
    {
        pressed = false;

        if (!leftController.isValid)
        {
            leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

            if (!leftController.isValid)
            {
                previousYButton = false;
                return false;
            }
        }

        if (!leftController.TryGetFeatureValue(CommonUsages.secondaryButton, out pressed))
        {
            previousYButton = false;
            return false;
        }

        return true;
    }

    private void TryAcquire()
    {
        // 이미 써 버린 뒤라면 다시 획득할 수 없다. 호스트도 같은 이유로 거절하지만
        // 여기서 먼저 걸러 "잡은 아이템이 없다"는 엉뚱한 안내가 나가지 않게 한다.
        if (inventory.IsConsumed)
        {
            if (logInput)
            {
                Debug.Log(
                    $"[Item] Y 입력 무시 - {ItemKindRules.DisplayName(inventory.AcquiredItem)}은 " +
                    "이미 사용했습니다. 재획득·재사용은 불가합니다.", this);
            }

            return;
        }

        GrabbableItem grabbed = GrabbableItem.CurrentLocalGrabbed;

        if (grabbed == null)
        {
            if (logInput)
                Debug.Log("[Item] Y 입력 무시 - 손에 잡은 아이템이 없습니다.", this);
            return;
        }

        Role role = inventory.OwnerRole;

        // 로컬 1차 검사. 호스트가 ApplyAcquire에서 같은 규칙으로 다시 검증한다.
        if (!ItemKindRules.CanAcquire(role, grabbed.Kind))
        {
            if (logInput)
            {
                Debug.Log(
                    $"[Item] 획득 불가 - {role} 역할은 {ItemKindRules.DisplayName(grabbed.Kind)}을 " +
                    "획득할 수 없습니다. (잡는 것은 가능)", this);
            }
            return;
        }

        if (logInput)
            Debug.Log($"[Item] 획득 요청 - {ItemKindRules.DisplayName(grabbed.Kind)}", this);

        inventory.RequestAcquire(grabbed.Kind);

        cooldownRemaining = acquireCooldownSeconds;

        // 이 누름은 획득에 썼다. Y를 완전히 놓기 전에는 사용으로 이어지지 않게 한다.
        releaseRequiredBeforeUse = true;
    }
}
