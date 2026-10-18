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
/// 이번 단계에서는 "획득"만 한다. 같은 버튼으로 하게 될 "사용"은 다음 단계이며,
/// 획득 직후 같은 손짓이 사용으로 이어지지 않도록 <see cref="acquireCooldownSeconds"/> 를 둔다.
/// </summary>
[RequireComponent(typeof(PlayerItemInventory))]
[AddComponentMenu("ReBloom/Items/Item Pickup Input")]
public class ItemPickupInput : MonoBehaviour
{
    [Header("입력")]
    [Tooltip("획득 직후 이 시간 동안 같은 버튼 입력을 받지 않는다. " +
             "다음 단계에서 같은 Y 버튼이 '사용'으로 쓰이므로, 한 번 누름이 " +
             "획득과 사용으로 연달아 처리되는 것을 막는다.")]
    [SerializeField, Min(0f)] private float acquireCooldownSeconds = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool logInput = true;

    private PlayerItemInventory inventory;
    private NetworkPlayer owner;

    private InputDevice leftController;
    private bool previousYButton;
    private float cooldownRemaining;

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

        if (!pressedDown)
            return;

        if (cooldownRemaining > 0f)
            return;

        TryAcquire();
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
        // 이미 보유 중이면 획득할 것이 없다. (사용은 다음 단계에서 이 자리에 들어온다)
        if (inventory.HasItem)
        {
            if (logInput)
            {
                Debug.Log(
                    $"[Item] Y 입력 무시 - 이미 {ItemKindRules.DisplayName(inventory.HeldItem)} 보유 중",
                    this);
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
    }
}
