using System;
using System.Collections;
using Fusion;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lobby 캐릭터 선택 확인 UI 컨트롤러.
///
/// 흐름:
///   LobbyCharacterSelectTarget.SelectRequested (UI Ray Hover + Trigger)
///   → 대상 캐릭터 EnterPending + 두 캐릭터 입력 잠금 + ConfirmationCanvas Fade In
///   → [취소] 버튼 즉시 잠금 → Fade Out → 캔버스 비활성 → CancelPending + 잠금 해제(Hover 복귀)
///   → [선택] 버튼 즉시 잠금 → LobbyManager 기존 public 진입점 호출(즉시)
///            → Fade Out → 캔버스 비활성 → PlaySelectedEffect
///
/// 네트워크 호출 시점 메모:
/// - OnMentalButtonClicked / OnEarButtonClicked는 Fade Out을 기다리지 않고 [선택]을 누른 즉시 호출한다.
///   Multi에서 상대가 같은 역할을 먼저 가져가는 경쟁을 0.2초 더 늦출 이유가 없고,
///   Fade 코루틴이 씬 전환/비활성화로 끊겨도 선택 요청이 유실되지 않기 때문이다.
///   Selected 연출(Glyph + Selection Effect)만 UI가 사라진 뒤에 시작한다.
///
/// 이 컴포넌트는 항상 활성인 GameObject(예: LobbyManager 또는 ConfirmationController)에 두고,
/// confirmationCanvas GameObject와 CanvasGroup만 제어한다. (비활성 오브젝트는 이벤트를 받지 못하므로)
/// </summary>
public class LobbyCharacterConfirmationUI : MonoBehaviour
{
    [Header("Confirmation UI")]
    [SerializeField, Tooltip("ConfirmationCanvas GameObject. 열려 있는 동안만 활성화된다.")]
    private GameObject confirmationCanvas;

    [SerializeField, Tooltip("ConfirmationCanvas(또는 ConfirmationPanel)의 CanvasGroup. Fade와 버튼 입력 잠금에 사용한다.")]
    private CanvasGroup confirmationCanvasGroup;

    [SerializeField, Tooltip("ConfirmationPanel/MessageText")]
    private TMP_Text messageText;

    [SerializeField, Tooltip("ConfirmationPanel/ConfirmBtn")]
    private Button confirmButton;

    [SerializeField, Tooltip("ConfirmationPanel/CancelBtn")]
    private Button cancelButton;

    [Header("Fade")]
    [SerializeField, Min(0f), Tooltip("확인 창이 나타나는 시간(초). Fade In이 끝나야 버튼이 눌린다.")]
    private float fadeInDuration = 0.2f;

    [SerializeField, Min(0f), Tooltip("확인 창이 사라지는 시간(초).")]
    private float fadeOutDuration = 0.2f;

    [Header("Character Targets")]
    [SerializeField, Tooltip("Base (1)/CharacterSelectArea 의 LobbyCharacterSelectTarget")]
    private LobbyCharacterSelectTarget mentalTarget;

    [SerializeField, Tooltip("Base/CharacterSelectArea 의 LobbyCharacterSelectTarget")]
    private LobbyCharacterSelectTarget earTarget;

    [Header("Button Labels")]
    [SerializeField, Tooltip("ConfirmationPanel/ConfirmBtn/Text (TMP)")]
    private TMP_Text confirmButtonLabel;

    [SerializeField, Tooltip("ConfirmationPanel/CancelBtn/Text (TMP)")]
    private TMP_Text cancelButtonLabel;

    [Header("Messages")]
    [SerializeField]
    private string mentalMessage = "정신 제약 캐릭터로 선택하시겠습니까?";

    [SerializeField]
    private string earMessage = "청각 제약 캐릭터로 선택하시겠습니까?";

    [SerializeField]
    private string mentalDeselectMessage = "루멘 선택을 취소하시겠습니까?";

    [SerializeField]
    private string earDeselectMessage = "에코 선택을 취소하시겠습니까?";

    [SerializeField]
    private string selectConfirmLabel = "선택";

    [SerializeField]
    private string selectCancelLabel = "취소";

    [SerializeField]
    private string deselectConfirmLabel = "선택 취소";

    [SerializeField]
    private string deselectCancelLabel = "돌아가기";

    private enum ConfirmationMode
    {
        Select,
        Deselect
    }

    // 현재 확인을 기다리는 캐릭터. 열려 있지 않으면 null.
    private LobbyCharacterSelectTarget currentTarget;

    // 현재 확인 창이 선택인지 선택 취소인지.
    private ConfirmationMode mode = ConfirmationMode.Select;

    // Confirm이 한 번 끝나면 다시 캐릭터를 고르게 하지 않는다.
    private bool selectionConfirmed;

    // Fade Out이 진행 중인 동안 Cancel/Confirm 재입력을 막는다.
    private bool closing;

    private bool subscribed;

    private Coroutine fadeCoroutine;

    // 지난 프레임의 Networked Owner. 값이 바뀐 프레임에만 Target 상태를 갱신한다.
    private PlayerRef lastMentalOwner = PlayerRef.None;
    private PlayerRef lastEarOwner = PlayerRef.None;

    public bool IsOpen => currentTarget != null;

    private void Awake()
    {
        // 시작 시에는 항상 닫혀 있어야 한다.
        SetCanvasGroupInteractable(false);
        ApplyAlpha(0f);
        SetCanvasActive(false);
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void Update()
    {
        RefreshRoleOwnership();
    }

    // ------------------------------------------------------------------
    // 상대 점유 반영 (Networked MentalOwner/EarOwner 폴링)
    // ------------------------------------------------------------------

    // 값이 바뀐 프레임에만 Target에 반영한다. (매 프레임 MPB/Interactor를 건드리지 않는다.)
    private void RefreshRoleOwnership()
    {
        LobbyManager lobbyManager = LobbyManager.Instance;

        if (lobbyManager == null || lobbyManager.Runner == null)
            return;

        PlayerRef me = lobbyManager.Runner.LocalPlayer;
        PlayerRef mentalOwner = lobbyManager.MentalOwner;
        PlayerRef earOwner = lobbyManager.EarOwner;

        if (mentalOwner == lastMentalOwner && earOwner == lastEarOwner)
            return;

        PlayerRef previousMental = lastMentalOwner;
        PlayerRef previousEar = lastEarOwner;

        lastMentalOwner = mentalOwner;
        lastEarOwner = earOwner;

        // 상대가 가져간 역할만 Unavailable. 내가 고른 역할은 Selected 연출을 유지해야 하므로 제외한다.
        ApplyOwnership(mentalTarget, mentalOwner, me, previousMental);
        ApplyOwnership(earTarget, earOwner, me, previousEar);
    }

    // Multi(2인 협동) 세션인지. (LobbyManager.IsSelectionComplete와 같은 기준)
    private static bool IsMultiSession =>
        NetworkManager.Instance == null || NetworkManager.Instance.Mode == SessionMode.Multi;

    // 해당 역할을 이미 다른 플레이어가 가져갔는지.
    private static bool IsTakenByOther(LobbyManager lobbyManager, Role role)
    {
        if (lobbyManager.Runner == null)
            return false;

        PlayerRef owner = role == Role.mental ? lobbyManager.MentalOwner : lobbyManager.EarOwner;

        return owner != PlayerRef.None && owner != lobbyManager.Runner.LocalPlayer;
    }

    private void ApplyOwnership(LobbyCharacterSelectTarget target, PlayerRef owner, PlayerRef me, PlayerRef previousOwner)
    {
        if (target == null)
            return;

        // 내 역할이 해제됐다(내가 취소했거나 Host가 회수했다) → Selected 연출을 끝내고 Available로 되돌린다.
        if (previousOwner == me && owner == PlayerRef.None)
        {
            selectionConfirmed = false;   // 다시 선택할 수 있게 한다.
            target.ClearSelected();
        }

        bool takenByOther = owner != PlayerRef.None && owner != me;

        // 내가 확인 창을 열어 둔 캐릭터를 상대가 먼저 가져갔으면 창을 닫는다.
        if (takenByOther && currentTarget == target && !selectionConfirmed)
            Cancel();

        target.SetUnavailable(takenByOther);
    }

    private void OnDisable()
    {
        Unsubscribe();

        // 진행 중인 Fade는 끊고, 열려 있던 확인 창은 즉시 정리한다.
        // 선택 요청을 이미 보냈다면(Confirm Fade Out 중) 취소가 아니라 Selected로 마무리한다.
        StopFade();

        if (currentTarget != null)
        {
            if (selectionConfirmed)
                FinishConfirm();
            else
                FinishCancel();
        }

        closing = false;

        SetCanvasGroupInteractable(false);
        ApplyAlpha(0f);
        SetCanvasActive(false);
    }

    // ------------------------------------------------------------------
    // Subscription
    // ------------------------------------------------------------------

    private void Subscribe()
    {
        if (subscribed)
            return;

        if (mentalTarget != null)
            mentalTarget.SelectRequested += OnSelectRequested;
        else
            Debug.LogWarning("[Lobby Confirmation] Mental Target이 연결되지 않았습니다.", this);

        if (earTarget != null)
            earTarget.SelectRequested += OnSelectRequested;
        else
            Debug.LogWarning("[Lobby Confirmation] Ear Target이 연결되지 않았습니다.", this);

        if (confirmButton != null)
            confirmButton.onClick.AddListener(Confirm);
        else
            Debug.LogWarning("[Lobby Confirmation] Confirm Button이 연결되지 않았습니다.", this);

        if (cancelButton != null)
            cancelButton.onClick.AddListener(Cancel);
        else
            Debug.LogWarning("[Lobby Confirmation] Cancel Button이 연결되지 않았습니다.", this);

        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;

        if (mentalTarget != null)
            mentalTarget.SelectRequested -= OnSelectRequested;

        if (earTarget != null)
            earTarget.SelectRequested -= OnSelectRequested;

        if (confirmButton != null)
            confirmButton.onClick.RemoveListener(Confirm);

        if (cancelButton != null)
            cancelButton.onClick.RemoveListener(Cancel);

        subscribed = false;
    }

    // ------------------------------------------------------------------
    // Open / Cancel / Confirm
    // ------------------------------------------------------------------

    private void OnSelectRequested(LobbyCharacterSelectTarget target)
    {
        if (target == null)
            return;

        // 이미 열려 있거나(닫히는 중 포함) 무시한다.
        if (currentTarget != null || closing)
            return;

        if (target.IsSelected)
        {
            // 선택 취소는 Multi에서만. (LobbyManager.RequestDeselect도 Single을 막지만 창부터 띄우지 않는다.)
            if (!IsMultiSession)
                return;
        }
        else if (selectionConfirmed)
        {
            // 이미 역할을 확정한 상태에서 다른 캐릭터를 새로 고르지는 않는다.
            return;
        }

        Open(target);
    }

    private void Open(LobbyCharacterSelectTarget target)
    {
        currentTarget = target;

        // Selected 캐릭터에서 온 요청이면 선택 취소 확인이다.
        mode = target.IsSelected ? ConfirmationMode.Deselect : ConfirmationMode.Select;

        // Select: 대상을 Pending으로 강조 유지.
        // Deselect: Selected 상태(Thankful/Outline/Glyph)를 그대로 두고 입력만 잠근다.
        if (mode == ConfirmationMode.Select)
            currentTarget.EnterPending();

        SetTargetsLocked(true);

        ApplyModeTexts(currentTarget.Role);

        // 열리는 즉시 입력을 받는다. (Fade In 중 첫 Trigger가 유실되지 않도록) alpha만 페이드한다.
        SetCanvasActive(true);
        SetCanvasGroupInteractable(true);
        StartFade(1f, fadeInDuration, null);

        Debug.Log($"[Lobby Confirmation] Open - role={currentTarget.Role}, mode={mode}", this);
    }

    // 창을 열 때마다 모드에 맞는 문구와 버튼 라벨을 다시 설정한다.
    private void ApplyModeTexts(Role role)
    {
        if (messageText != null)
            messageText.text = GetMessage(role);

        if (confirmButtonLabel != null)
            confirmButtonLabel.text = mode == ConfirmationMode.Deselect ? deselectConfirmLabel : selectConfirmLabel;

        if (cancelButtonLabel != null)
            cancelButtonLabel.text = mode == ConfirmationMode.Deselect ? deselectCancelLabel : selectCancelLabel;
    }

    /// <summary>취소 버튼. 창을 Fade Out한 뒤 대상을 Idle로 되돌리고 다시 캐릭터를 고를 수 있게 한다.</summary>
    public void Cancel()
    {
        if (currentTarget == null || closing)
            return;

        closing = true;

        // 버튼 입력을 즉시 막고 사라지는 동안 재입력을 받지 않는다.
        SetCanvasGroupInteractable(false);
        StartFade(0f, fadeOutDuration, OnCancelFadeOutComplete);

        Debug.Log("[Lobby Confirmation] Cancel", this);
    }

    private void OnCancelFadeOutComplete()
    {
        SetCanvasActive(false);
        FinishCancel();
        closing = false;
    }

    // 창이 사라진 뒤 캐릭터 상태를 되돌린다. (OnDisable에서도 즉시 호출된다.)
    private void FinishCancel()
    {
        LobbyCharacterSelectTarget target = currentTarget;
        currentTarget = null;

        if (target != null)
            target.CancelPending();

        SetTargetsLocked(false);
    }

    /// <summary>선택 버튼. LobbyManager 진입점을 즉시 호출하고, 창을 Fade Out한 뒤 Selected 연출을 켠다.</summary>
    public void Confirm()
    {
        if (currentTarget == null || closing)
            return;

        if (selectionConfirmed)
        {
            SetCanvasGroupInteractable(false);
            SetCanvasActive(false);
            return;
        }

        Role role = currentTarget.Role;

        if (role == Role.none)
        {
            Debug.LogWarning("[Lobby Confirmation] Role이 none이라 선택을 취소합니다.", this);
            Cancel();
            return;
        }

        LobbyManager lobbyManager = LobbyManager.Instance;

        if (lobbyManager == null)
        {
            Debug.LogWarning("[Lobby Confirmation] LobbyManager가 아직 준비되지 않아 선택을 취소합니다.", this);
            Cancel();
            return;
        }

        // 선택 취소는 별도 경로로 처리한다. (Selected 연출은 Owner가 None으로 복제된 뒤에 정리)
        if (mode == ConfirmationMode.Deselect)
        {
            ConfirmDeselect(lobbyManager, role);
            return;
        }

        // 내가 확인 창을 보는 사이에 상대가 먼저 가져갔으면 선택 요청을 보내지 않는다.
        // (최종 방어는 Host의 ApplySelect 가드. 여기서는 UX만 보완한다.)
        if (IsTakenByOther(lobbyManager, role))
        {
            Debug.Log($"[Lobby Confirmation] 상대가 먼저 {role}을 선택해 요청을 보내지 않습니다.", this);
            Cancel();
            return;
        }

        closing = true;
        selectionConfirmed = true;

        // 중복 입력 방지.
        SetCanvasGroupInteractable(false);

        // 두 캐릭터 모두 잠금 유지. Single은 곧 씬이 넘어가고, Multi는 네트워크 상태가 버튼과 같이 잠근다.
        SetTargetsLocked(true);

        // 기존 버튼과 같은 public 진입점을 즉시 호출한다. (Fade와 무관하게 요청이 유실되지 않도록)
        // 이후 RPC / ApplySelect / 씬 이동은 LobbyManager 그대로.
        switch (role)
        {
            case Role.mental:
                lobbyManager.OnMentalButtonClicked();
                break;

            case Role.ear:
                lobbyManager.OnEarButtonClicked();
                break;
        }

        Debug.Log($"[Lobby Confirmation] Confirm - role={role}", this);

        // 창이 사라진 뒤 Selected 연출을 시작한다. 그동안 대상은 Pending 강조를 유지한다.
        StartFade(0f, fadeOutDuration, OnConfirmFadeOutComplete);
    }

    // 선택 취소 확정: 창만 닫고 역할 해제를 요청한다.
    // 캐릭터의 Selected 연출은 Owner가 None으로 복제된 뒤 RefreshRoleOwnership에서 정리한다.
    private void ConfirmDeselect(LobbyManager lobbyManager, Role role)
    {
        closing = true;

        SetCanvasGroupInteractable(false);

        switch (role)
        {
            case Role.mental:
                lobbyManager.OnMentalDeselectClicked();
                break;

            case Role.ear:
                lobbyManager.OnEarDeselectClicked();
                break;
        }

        Debug.Log($"[Lobby Confirmation] Deselect 요청 - role={role}", this);

        StartFade(0f, fadeOutDuration, OnDeselectFadeOutComplete);
    }

    private void OnDeselectFadeOutComplete()
    {
        SetCanvasActive(false);

        // Selected 상태는 건드리지 않는다. 잠금만 풀어 다른 캐릭터를 다시 고를 수 있게 한다.
        currentTarget = null;
        SetTargetsLocked(false);
        closing = false;
    }

    private void OnConfirmFadeOutComplete()
    {
        SetCanvasActive(false);
        FinishConfirm();
        closing = false;
    }

    // Selected 연출 (Glyph + Selection Effect). Thinking/Outline/DetailPanel은 여기서 꺼진다.
    private void FinishConfirm()
    {
        LobbyCharacterSelectTarget target = currentTarget;
        currentTarget = null;

        if (target != null)
            target.PlaySelectedEffect();
    }

    // ------------------------------------------------------------------
    // Fade
    // ------------------------------------------------------------------

    private void StartFade(float targetAlpha, float duration, Action onComplete)
    {
        StopFade();

        if (confirmationCanvasGroup == null ||
            duration <= 0f ||
            Mathf.Approximately(confirmationCanvasGroup.alpha, targetAlpha))
        {
            ApplyAlpha(targetAlpha);
            onComplete?.Invoke();
            return;
        }

        fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha, duration, onComplete));
    }

    private void StopFade()
    {
        if (fadeCoroutine == null)
            return;

        StopCoroutine(fadeCoroutine);
        fadeCoroutine = null;
    }

    // 현재 alpha에서 목표값으로 이어서 보간하므로 도중에 방향이 바뀌어도 튀지 않는다.
    private IEnumerator FadeRoutine(float targetAlpha, float duration, Action onComplete)
    {
        float startAlpha = confirmationCanvasGroup.alpha;

        // 남은 거리만큼만 시간을 써서 전체 속도를 일정하게 유지한다.
        float scaledDuration = duration * Mathf.Abs(targetAlpha - startAlpha);
        float elapsed = 0f;

        while (elapsed < scaledDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / scaledDuration);
            ApplyAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));

            yield return null;
        }

        ApplyAlpha(targetAlpha);
        fadeCoroutine = null;

        onComplete?.Invoke();
    }

    private void ApplyAlpha(float alpha)
    {
        if (confirmationCanvasGroup == null)
            return;

        confirmationCanvasGroup.alpha = alpha;
    }

    private void SetCanvasGroupInteractable(bool value)
    {
        if (confirmationCanvasGroup == null)
            return;

        confirmationCanvasGroup.interactable = value;
        confirmationCanvasGroup.blocksRaycasts = value;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private void SetTargetsLocked(bool locked)
    {
        if (mentalTarget != null)
            mentalTarget.SetInputLocked(locked);

        if (earTarget != null)
            earTarget.SetInputLocked(locked);
    }

    private void SetCanvasActive(bool active)
    {
        if (confirmationCanvas == null)
        {
            if (active)
                Debug.LogWarning("[Lobby Confirmation] Confirmation Canvas가 연결되지 않았습니다.", this);
            return;
        }

        if (confirmationCanvas.activeSelf != active)
            confirmationCanvas.SetActive(active);
    }

    private string GetMessage(Role role)
    {
        if (mode == ConfirmationMode.Deselect)
        {
            switch (role)
            {
                case Role.mental:
                    return mentalDeselectMessage;

                case Role.ear:
                    return earDeselectMessage;

                default:
                    return "이 캐릭터 선택을 취소하시겠습니까?";
            }
        }

        switch (role)
        {
            case Role.mental:
                return mentalMessage;

            case Role.ear:
                return earMessage;

            default:
                return "이 캐릭터로 선택하시겠습니까?";
        }
    }
}
