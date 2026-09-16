using System;
using System.Collections;
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

    [Header("Messages")]
    [SerializeField]
    private string mentalMessage = "정신 제약 캐릭터로 선택하시겠습니까?";

    [SerializeField]
    private string earMessage = "청각 제약 캐릭터로 선택하시겠습니까?";

    // 현재 확인을 기다리는 캐릭터. 열려 있지 않으면 null.
    private LobbyCharacterSelectTarget currentTarget;

    // Confirm이 한 번 끝나면 다시 캐릭터를 고르게 하지 않는다.
    private bool selectionConfirmed;

    // Fade Out이 진행 중인 동안 Cancel/Confirm 재입력을 막는다.
    private bool closing;

    private bool subscribed;

    private Coroutine fadeCoroutine;

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

        // 이미 열려 있거나(닫히는 중 포함) 선택이 끝났으면 무시한다.
        if (currentTarget != null || closing || selectionConfirmed)
            return;

        Open(target);
    }

    private void Open(LobbyCharacterSelectTarget target)
    {
        currentTarget = target;

        // 대상은 Pending으로 강조를 유지하고, 두 캐릭터 모두 추가 입력을 막는다.
        currentTarget.EnterPending();
        SetTargetsLocked(true);

        if (messageText != null)
            messageText.text = GetMessage(currentTarget.Role);

        // 버튼은 Fade In이 끝난 뒤에만 눌리게 한다.
        SetCanvasGroupInteractable(false);
        SetCanvasActive(true);
        StartFade(1f, fadeInDuration, OnFadeInComplete);

        Debug.Log($"[Lobby Confirmation] Open - role={currentTarget.Role}", this);
    }

    private void OnFadeInComplete()
    {
        SetCanvasGroupInteractable(true);
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
