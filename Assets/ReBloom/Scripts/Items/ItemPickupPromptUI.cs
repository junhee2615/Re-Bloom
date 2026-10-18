using TMPro;
using UnityEngine;

/// <summary>
/// 아이템을 잡고 있을 때 "Y 버튼을 눌러 획득" 같은 안내를 띄우는 World Space UI.
///
/// 배치: 아이템 모델(HearingAid_low / Medicine_low)의 <b>자식</b>. 그래서 잡고 움직이면
/// 안내도 함께 따라온다. 위치·회전·스케일은 매 프레임 이 스크립트가 보정한다.
///
/// 읽기만 한다 —
///   · <see cref="GrabbableItem.CurrentLocalGrabbed"/> : 지금 이 기기의 손에 잡힌 아이템
///   · <see cref="PlayerItemInventory.LocalInstance"/>  : 내 역할과 보유 상태
///   · <see cref="ItemKindRules.CanAcquire"/>            : 역할 규칙
/// 어떤 상태도 쓰지 않으므로 획득 처리·네트워크 동기화에 영향을 주지 않는다.
///
/// 로컬 전용인 이유:
/// 아이템은 NetworkObject가 아니라 각 피어가 자기 씬 사본을 갖는다. XRI의 잡기 판정도
/// 로컬에서만 일어나므로 <see cref="GrabbableItem.CurrentLocalGrabbed"/> 는 자연히
/// 내 손 상태만 가리킨다. 상대가 잡아도 내 화면에는 뜨지 않고, 그 반대도 같다.
/// 네트워크 동기화를 하지 않는다.
///
/// 상태 판정 순서는 <see cref="ItemPickupInput"/> 과 <b>똑같이</b> 맞춘다. 그래야
/// 안내 문구와 실제로 Y를 눌렀을 때의 결과가 어긋나지 않는다.
/// </summary>
[RequireComponent(typeof(GrabbableItem))]
[AddComponentMenu("ReBloom/Items/Item Pickup Prompt UI")]
public class ItemPickupPromptUI : MonoBehaviour
{
    [Header("문구")]
    [Tooltip("내 역할에 맞는 아이템을 잡았고 빈손일 때.")]
    [SerializeField] private string readyText = "Y 버튼을 눌러 획득";

    [Tooltip("다른 역할의 아이템을 잡았을 때.")]
    [SerializeField] private string wrongRoleText = "획득할 수 없는 아이템";

    [Tooltip("이미 아이템을 보유한 상태로 무언가를 잡았을 때.")]
    [SerializeField] private string alreadyHoldingText = "이미 아이템을 보유 중입니다";

    [Header("배치")]
    [Tooltip("아이템 위로 띄울 높이(월드 미터).")]
    [SerializeField, Min(0f)] private float heightOffset = 0.14f;

    [Tooltip("캔버스의 목표 월드 스케일. 부모 스케일이 1이 아니어도 이 값이 유지되도록 보정한다.")]
    [SerializeField, Min(0.0001f)] private float worldScale = 0.0012f;

    [Header("페이드")]
    [SerializeField, Min(0f)] private float fadeInDuration = 0.12f;

    [Tooltip("일반적인 Grab 해제로 사라질 때의 시간.")]
    [SerializeField, Min(0f)] private float fadeOutDuration = 0.12f;

    [Tooltip("Y 버튼으로 획득했을 때 사라지는 시간. 이 동안 문구와 위치가 그대로 유지된다.")]
    [SerializeField, Min(0f)] private float acquiredFadeOutDuration = 0.3f;

    [Header("References (셋업 도구가 연결한다)")]
    [Tooltip("안내 캔버스의 CanvasGroup. 비우면 자식에서 찾는다.")]
    [SerializeField] private CanvasGroup promptGroup;

    [Tooltip("문구를 넣을 TMP. 비우면 promptGroup 하위에서 찾는다.")]
    [SerializeField] private TMP_Text promptText;

    [Header("Debug")]
    [SerializeField] private bool logStateChange;

    /// <summary>안내 상태. <see cref="ItemPickupInput"/> 의 판정 순서와 1:1로 맞춘다.</summary>
    private enum PromptState
    {
        Hidden,
        Ready,
        AlreadyHolding,
        WrongRole,
    }

    private GrabbableItem item;
    private Transform anchor;

    private PromptState appliedState = PromptState.Hidden;
    private float alpha;
    private float appliedAlpha = -1f;

    // 이 아이템이 획득되었는지. 한 번 켜지면 되돌아가지 않는다(래치).
    private bool acquiredHidden;

    // 획득 순간의 월드 포즈. 그 뒤로는 이 값을 계속 넣어 UI를 그 자리에 세워 둔다.
    // 아이템은 획득과 동시에 보이지 않는 강체가 되어 낙하하므로, 그대로 두면
    // 자식인 이 캔버스가 함께 떨어진다.
    private bool poseFrozen;
    private Vector3 frozenPosition;
    private Quaternion frozenRotation;
    private Vector3 frozenScale;

    private void Awake()
    {
        item = GetComponent<GrabbableItem>();
        anchor = transform;

        if (promptGroup == null)
            promptGroup = GetComponentInChildren<CanvasGroup>(true);

        if (promptGroup == null)
        {
            Debug.LogWarning(
                $"[Item] {name}: 안내 UI(CanvasGroup)를 찾지 못해 표시하지 않습니다. " +
                "Re_Bloom ▸ Items ▸ Stage1 Item Setup - Apply 를 실행하세요.", this);

            enabled = false;
            return;
        }

        if (promptText == null)
            promptText = promptGroup.GetComponentInChildren<TMP_Text>(true);

        if (promptText == null)
        {
            Debug.LogWarning($"[Item] {name}: 안내 UI의 TMP를 찾지 못했습니다.", this);
            enabled = false;
            return;
        }

        // 시작은 숨김. (아이템을 잡기 전에는 보이지 않는다)
        alpha = 0f;
        ApplyAlpha(0f);
        promptGroup.gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        // LateUpdate인 이유: 손 추종과 Grab 이동이 끝난 뒤의 최종 포즈를 따라가야
        // 안내가 아이템보다 한 프레임 늦게 움직이지 않는다.
        PromptState state = ResolveState();

        if (state != appliedState)
        {
            appliedState = state;
            ApplyText(state);

            if (logStateChange)
            {
                Debug.Log(
                    $"[Item] 안내 UI - {ItemKindRules.DisplayName(item != null ? item.Kind : ItemKind.None)}: {state}",
                    this);
            }
        }

        bool visible = state != PromptState.Hidden;

        if (visible && !promptGroup.gameObject.activeSelf)
        {
            ApplyAlpha(alpha);                     // 알파 0을 먼저 반영해 깜빡임을 막는다
            promptGroup.gameObject.SetActive(true);
        }

        UpdateFade(visible);

        if (promptGroup.gameObject.activeSelf)
            UpdateTransform();
    }

    // ── 상태 판정 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 지금 띄울 안내를 고른다.
    ///
    /// 판정 순서는 <see cref="ItemPickupInput.TryAcquire"/> 와 동일하다 —
    /// ①보유 중인지 ②역할이 맞는지. 그래야 "획득할 수 없는 아이템"이라고 띄운 뒤
    /// 실제로는 "이미 보유 중"으로 거절되는 식의 불일치가 생기지 않는다.
    ///
    /// 단, 그 두 검사보다 <b>먼저</b> "이 아이템이 이미 획득되었는지"를 본다.
    /// 획득 순간의 실제 호출 순서는 다음과 같다 —
    /// <code>
    /// Update  ItemPickupInput  : RequestAcquire → ApplyAcquire → HeldItem 즉시 대입
    /// Update  GrabbableItem    : IsKindTaken → RemoveFromWorld → CurrentLocalGrabbed = null
    /// LateUpdate  이 스크립트  : 상태 판정
    /// </code>
    /// 두 Update는 서로 다른 GameObject에 있고 실행 순서가 지정돼 있지 않다
    /// (ProjectSettings/MonoManager.asset에 항목 없음). GrabbableItem이 먼저 돌면
    /// HeldItem만 true가 된 채 CurrentLocalGrabbed가 아직 살아 있는 프레임이 생겨
    /// "이미 아이템을 보유 중입니다"가 한 프레임 노출된다. 아래 검사가 그 틈을 막는다.
    /// </summary>
    private PromptState ResolveState()
    {
        if (item == null)
            return PromptState.Hidden;

        // 한 번 획득으로 숨겨지면 다시는 돌아오지 않는다.
        // 페이드 도중에 Grab이 해제되거나 상태가 바뀌어도 UI가 되살아나지 않게 한다.
        if (acquiredHidden)
            return PromptState.Hidden;

        // 이 기기의 손에 "이 아이템"이 잡혀 있어야 한다.
        // (GrabbableItem이 selectEntered/selectExited에서 갱신하는 값)
        if (GrabbableItem.CurrentLocalGrabbed != item)
            return PromptState.Hidden;

        // 이 종류가 이미 획득되었다면 아이템은 곧 월드에서 사라진다.
        // GrabbableItem.Update가 RemoveFromWorld를 부르기 전이라도 여기서 먼저 입을 닫는다.
        // (GrabbableItem.Update가 쓰는 것과 같은 판정이라 결론이 어긋나지 않는다)
        if (PlayerItemInventory.IsKindTaken(item.Kind))
        {
            acquiredHidden = true;
            return PromptState.Hidden;
        }

        PlayerItemInventory inventory = PlayerItemInventory.LocalInstance;

        // 아바타가 아직 스폰되지 않아 역할을 알 수 없으면 아무 말도 하지 않는다.
        // (잘못된 안내보다 침묵이 낫다)
        if (inventory == null)
            return PromptState.Hidden;

        if (inventory.HasItem)
            return PromptState.AlreadyHolding;

        if (!ItemKindRules.CanAcquire(inventory.OwnerRole, item.Kind))
            return PromptState.WrongRole;

        return PromptState.Ready;
    }

    private void ApplyText(PromptState state)
    {
        if (promptText == null)
            return;

        switch (state)
        {
            case PromptState.Ready:
                promptText.text = readyText;
                break;
            case PromptState.AlreadyHolding:
                promptText.text = alreadyHoldingText;
                break;
            case PromptState.WrongRole:
                promptText.text = wrongRoleText;
                break;
        }
    }

    // ── 표시 ────────────────────────────────────────────────────────────────

    private void UpdateFade(bool visible)
    {
        float target = visible ? 1f : 0f;

        if (!Mathf.Approximately(alpha, target))
        {
            // 획득으로 사라질 때만 acquiredFadeOutDuration 을 쓴다.
            // 일반적인 Grab 해제는 기존 fadeOutDuration 그대로다.
            float duration = target > alpha
                ? fadeInDuration
                : (acquiredHidden ? acquiredFadeOutDuration : fadeOutDuration);

            alpha = duration > 0f
                ? Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / duration)
                : target;

            ApplyAlpha(alpha);
        }

        // 완전히 투명해진 뒤에만 끈다.
        if (!visible && alpha <= 0f && promptGroup.gameObject.activeSelf)
            promptGroup.gameObject.SetActive(false);
    }

    private void ApplyAlpha(float value)
    {
        if (promptGroup == null)
            return;

        bool endpoint = value <= 0f || value >= 1f;

        if (!endpoint && Mathf.Abs(value - appliedAlpha) < 0.01f)
            return;

        appliedAlpha = value;
        promptGroup.alpha = value;
    }

    /// <summary>
    /// 아이템 위에 띄우고 카메라를 바라보게 한다(빌보드).
    ///
    /// 위치·회전을 월드로 직접 넣는 이유:
    /// 아이템 모델은 임의의 회전을 갖고 있어서 로컬 오프셋으로는 "위"를 보장할 수 없다.
    /// 스케일은 부모의 lossyScale을 상쇄해, 모델 스케일이 1이 아니어도 글자 크기가 같게 유지한다.
    ///
    /// 회전은 Yaw·Pitch 모두 카메라를 향한다. 아이템을 손에 들고 돌려 봐도 글자가 읽힌다.
    /// </summary>
    private void UpdateTransform()
    {
        Transform canvas = promptGroup.transform;

        // 획득 뒤에는 그 순간의 포즈를 계속 다시 넣는다.
        // 아이템은 획득과 동시에 (보이지 않는) 자유 낙하 강체가 되므로,
        // 부모를 그냥 따라가게 두면 안내가 아래로 떨어진다.
        // 매 프레임 월드 포즈를 덮어써서 부모의 이동을 상쇄한다.
        if (acquiredHidden)
        {
            if (!poseFrozen)
            {
                poseFrozen = true;
                frozenPosition = canvas.position;
                frozenRotation = canvas.rotation;
                frozenScale = canvas.localScale;
            }

            canvas.SetPositionAndRotation(frozenPosition, frozenRotation);
            canvas.localScale = frozenScale;
            return;
        }

        canvas.position = anchor.position + Vector3.up * heightOffset;

        Camera cam = Camera.main;

        if (cam != null)
        {
            Vector3 fromCamera = canvas.position - cam.transform.position;

            if (fromCamera.sqrMagnitude > 0.000001f)
                canvas.rotation = Quaternion.LookRotation(fromCamera, Vector3.up);
        }

        // 부모 스케일 상쇄 — 월드에서 항상 worldScale이 되도록.
        Vector3 parentScale = canvas.parent != null ? canvas.parent.lossyScale : Vector3.one;

        canvas.localScale = new Vector3(
            SafeDivide(worldScale, parentScale.x),
            SafeDivide(worldScale, parentScale.y),
            SafeDivide(worldScale, parentScale.z));
    }

    private static float SafeDivide(float value, float divisor)
    {
        return Mathf.Abs(divisor) < 0.0001f ? value : value / divisor;
    }
}
