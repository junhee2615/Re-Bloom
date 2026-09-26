using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Lobby 캐릭터 선택 영역(CharacterSelectArea) 스크립트.
///
/// 선택 입력 기준: "UI용 XRRayInteractor가 이 영역을 Hover하는 동안 UI Press(오른손 Trigger)".
///
/// - Persistent XR Rig의 UI Interactor는 Select Input이 Teleport Mode(썸스틱)라
///   XRSimpleInteractable.selectEntered는 Trigger로 발생하지 않는다.
/// - 그래서 Hover 이벤트로 넘어온 Interactor가 UI용 XRRayInteractor일 때만 캐시해 두고,
///   그 Interactor의 uiPressInput(UI 버튼을 누를 때와 같은 액션)을 직접 읽는다.
/// - XR Origin / Interactor는 Inspector나 Find로 참조하지 않는다.
///
/// 상태:
/// - Idle     : Hover 가능. UI Ray Hover 중이면 Thinking + Outline.
/// - Pending  : Confirmation UI가 열린 상태. Ray가 UI로 옮겨 가 hoverExited가 와도 Thinking/Outline 유지.
/// - Selected : Confirm 완료. Thankful 1회 재생, Outline 유지, DetailPanel OFF, Glyph + Selection Effect 재생.
///
/// Trigger(UI Press)는 Idle + 입력 잠금 아님 + Role 지정 상태에서만 SelectRequested 이벤트를 발행한다.
/// 실제 확인/취소와 LobbyManager 호출은 LobbyCharacterConfirmationUI가 담당한다.
///
/// Hover 연출 (UI Ray Hover에만 반응, Near-Far 등 다른 Interactor는 무시):
/// - 형제 오브젝트(Base_end_1)의 Animator에 IsHovered=true/false.
///   (LobbyCharacterAnimator.controller: Idle ⇄ Thinking)
/// - 캐릭터 루트(Base / Base (1))의 QuickOutline `Outline` 컴포넌트를 enabled ON/OFF.
///   색·두께는 Inspector 값을 그대로 쓰고 코드는 켜고 끄기만 한다.
///
/// Confirm 연출 (PlaySelectedEffect):
/// - Selection Effect(ParticleSystem) Play.
/// - Glyph(Quad Renderer) 알파 Fade In + 회전.
///
/// Glyph 알파 처리 메모:
/// - Mental/Ear가 같은 LobbySelectionGlyph.mat(URP/Unlit, Transparent)을 공유하므로
///   MaterialPropertyBlock으로 Renderer마다 _BaseColor를 덮어써 서로 독립적으로 제어한다.
///   Material 인스턴스를 만들지 않고 원본 asset도 건드리지 않는다.
/// - RGB와 최대 알파는 원본 Material의 _BaseColor를 그대로 쓰고, 알파 비율(0~1)만 바꾼다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class LobbyCharacterSelectTarget : MonoBehaviour
{
    private enum SelectState
    {
        Idle,
        Pending,
        Selected
    }

    private static readonly int IsHoveredHash =
        Animator.StringToHash("IsHovered");

    // LobbyCharacterAnimator.controller: Any State → Thankful (Trigger). Thankful은 나가는 전환이 없어
    // 마지막 포즈를 유지하므로, 선택을 취소하면 Idle 상태로 직접 CrossFade 한다.
    private static readonly int SelectedHash =
        Animator.StringToHash("Selected");

    private static readonly int IdleStateHash =
        Animator.StringToHash("Idle");

    // Thankful → Idle 복귀 시 짧게 섞어 자세가 튀지 않게 한다.
    private const float IdleReturnCrossFade = 0.18f;

    private static readonly int BaseColorId =
        Shader.PropertyToID("_BaseColor");

    [Header("Role")]
    [SerializeField, Tooltip("이 캐릭터가 나타내는 역할. Mental: mental / Ear: ear. none이면 선택 요청을 보내지 않는다.")]
    private Role role = Role.none;

    [Header("Hover Outline")]
    [SerializeField, Tooltip("캐릭터 루트(Base / Base (1))의 QuickOutline Outline 컴포넌트. 비워 두면 부모에서 자동으로 찾는다.")]
    private Outline characterOutline;

    [Header("Selection Effect (Confirm 연출)")]
    [SerializeField, Tooltip("발밑 파동 이펙트. Mental: MentalSelectionEffect/ActivateCircle, Ear: EarSelectionEffect/ActivateCircle")]
    private ParticleSystem selectionEffect;

    [SerializeField, Tooltip("발밑 마법진 Quad의 Renderer. Mental: MentalSelectionEffect/Glyph, Ear: EarSelectionEffect/Glyph")]
    private Renderer selectionGlyphRenderer;

    [SerializeField, Min(0f), Tooltip("Glyph가 나타나고 사라지는 데 걸리는 시간(초).")]
    private float glyphFadeDuration = 0.3f;

    [SerializeField, Tooltip("Glyph 회전 속도(도/초). 양수면 시계 방향, 음수면 반시계 방향.")]
    private float glyphRotationSpeed = 8f;

    [Header("Unavailable (상대가 선택한 캐릭터)")]
    [SerializeField, Range(0f, 1f), Tooltip("회색으로 만들 때 채도를 얼마나 뺄지. 1이면 완전 무채색.")]
    private float unavailableDesaturation = 0.85f;

    [SerializeField, Range(0f, 1f), Tooltip("회색으로 만들 때 밝기 배율. 낮을수록 어둡다.")]
    private float unavailableBrightness = 0.55f;

    [Header("Detail Panel (Hover 설명 UI)")]
    [SerializeField, Tooltip("이 캐릭터의 설명 패널 CanvasGroup. Mental: MentalDetailPanel, Ear: EarDetailPanel")]
    private CanvasGroup detailPanel;

    [SerializeField, Min(0f), Tooltip("설명 패널이 나타나고 사라지는 데 걸리는 시간(초).")]
    private float detailFadeDuration = 0.25f;

    /// <summary>이 캐릭터의 역할. Confirmation UI가 LobbyManager 진입점을 고를 때 사용한다.</summary>
    public Role Role => role;

    /// <summary>Confirmation UI가 열려 있는지(Pending) 여부.</summary>
    public bool IsPending => state == SelectState.Pending;

    /// <summary>Confirm이 끝났는지 여부.</summary>
    public bool IsSelected => state == SelectState.Selected;

    /// <summary>UI Ray Hover 중 Trigger(UI Press)가 들어왔을 때 발행. 인자는 요청한 캐릭터.</summary>
    public event Action<LobbyCharacterSelectTarget> SelectRequested;

    private XRSimpleInteractable interactable;

    // 캐릭터 루트(Base / Base (1)) 아래 Base_end_1의 Animator. 없으면 null.
    private Animator characterAnimator;

    // 현재 이 영역을 Hover 중인 UI용 Ray Interactor. 없으면 null.
    private XRRayInteractor hoveringUIRay;

    private SelectState state = SelectState.Idle;

    // Confirmation UI가 열려 있는 동안 다른 캐릭터가 반응하지 않도록 UI가 걸어 주는 잠금.
    private bool inputLocked;

    // 상대 플레이어가 이 역할을 가져가서 선택할 수 없는 상태.
    private bool unavailable;

    // 회색 처리용. 캐릭터 루트 아래 Renderer들의 원본 머티리얼 색을 Awake에서 한 번만 캐시한다.
    private struct GrayTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public bool HasBaseColor;
        public Color BaseColor;
        public bool HasColorDim;
        public Color ColorDim;
    }

    private static readonly int BaseColorMatId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorDimId = Shader.PropertyToID("_ColorDim");

    private GrayTarget[] grayTargets;
    private MaterialPropertyBlock grayPropertyBlock;

    // Glyph 알파 제어용. 원본 Material의 _BaseColor(RGB + 최대 알파)를 보존한다.
    private MaterialPropertyBlock glyphPropertyBlock;
    private Color glyphBaseColor = Color.white;
    private bool glyphReady;

    // 0 = 숨김, 1 = 원본 알파. Fade가 중간에 끊겨도 여기서 이어서 보간한다.
    private float glyphAlphaBlend;
    private Coroutine glyphFadeCoroutine;
    private bool glyphRotating;

    // Detail Panel 알파. Fade가 중간에 끊겨도 현재 alpha에서 이어서 보간한다.
    private Coroutine detailFadeCoroutine;

    private void Awake()
    {
        interactable = GetComponent<XRSimpleInteractable>();

        // CharacterSelectArea와 Base_end_1은 형제 관계이므로 부모에서 내려가며 찾는다.
        if (transform.parent != null)
            characterAnimator = transform.parent.GetComponentInChildren<Animator>(true);

        if (characterAnimator == null)
        {
            Debug.LogWarning(
                "[Lobby Character] 부모 아래에서 Animator를 찾지 못해 Hover 애니메이션을 재생할 수 없습니다.",
                this);
        }

        // Outline은 캐릭터 루트(부모)에 붙어 있다. Inspector 연결이 없으면 부모에서 찾는다.
        // (QuickOutline의 Outline은 전역 네임스페이스. 이 파일은 UnityEngine.UI를 쓰지 않으므로 충돌 없음.)
        if (characterOutline == null && transform.parent != null)
            characterOutline = transform.parent.GetComponent<Outline>();

        if (characterOutline == null)
        {
            Debug.LogWarning(
                "[Lobby Character] Outline 컴포넌트를 찾지 못해 Hover 외곽선을 표시할 수 없습니다.",
                this);
        }

        if (role == Role.none)
        {
            Debug.LogWarning(
                "[Lobby Character] Role이 none입니다. Inspector에서 mental / ear를 지정해야 선택 요청을 보낼 수 있습니다.",
                this);
        }

        // 시작 시 외곽선은 꺼 둔다.
        SetOutline(false);

        // Play On Awake가 켜져 있어도 Confirm 전에는 돌지 않도록 시작 시 정리한다.
        StopSelectionEffect(ParticleSystemStopBehavior.StopEmittingAndClear);

        // 첫 프레임이 그려지기 전에 알파 0을 넣어 Glyph가 번쩍이지 않게 한다.
        InitializeGlyph();

        // 설명 패널도 첫 프레임 전에 숨긴다. (GameObject는 활성 유지, CanvasGroup alpha만 제어)
        InitializeDetailPanel();

        // 회색 처리 대상(Renderer × 머티리얼 슬롯)과 원본 색을 한 번만 캐시한다.
        CacheGrayTargets();
    }

    private void OnEnable()
    {
        if (interactable == null)
            return;

        interactable.hoverEntered.AddListener(OnHoverEntered);
        interactable.hoverExited.AddListener(OnHoverExited);
    }

    private void OnDisable()
    {
        if (interactable != null)
        {
            interactable.hoverEntered.RemoveListener(OnHoverEntered);
            interactable.hoverExited.RemoveListener(OnHoverExited);
        }

        // 상태와 연출을 전부 초기 상태로 되돌린다. (입력 잠금은 UI가 관리하므로 건드리지 않는다.)
        state = SelectState.Idle;
        hoveringUIRay = null;
        SetHovered(false);
        SetOutline(false);

        // 비활성화될 때는 남은 파동까지 즉시 정리한다.
        StopSelectionEffect(ParticleSystemStopBehavior.StopEmittingAndClear);

        // 비활성 상태에서는 Coroutine을 돌릴 수 없으므로 Glyph도 즉시 숨긴다.
        glyphRotating = false;
        StopGlyphFade();
        ApplyGlyphAlphaBlend(0f);

        // 설명 패널도 즉시 숨긴다.
        StopDetailFade();
        ApplyDetailAlpha(0f);
    }

    private void Update()
    {
        if (glyphRotating)
            RotateGlyph();

        if (hoveringUIRay == null)
            return;

        // RightRayModeToggle이 UI Interactor를 끄면 XRI가 hoverExited를 보내 주지만,
        // 혹시 남아 있는 참조가 있어도 입력을 읽지 않도록 한 번 더 막는다.
        if (!hoveringUIRay.isActiveAndEnabled)
        {
            ClearHoveringUIRay();
            return;
        }

        // UI 버튼을 누를 때와 같은 UI Press 액션. 눌린 프레임에만 true.
        if (hoveringUIRay.uiPressInput.ReadWasPerformedThisFrame())
        {
            Debug.Log("[Lobby Character] Select", this);
            RequestSelect();
        }
    }

    // ------------------------------------------------------------------
    // Select Request
    // ------------------------------------------------------------------

    private void RequestSelect()
    {
        // Confirmation UI가 열려 있거나(Pending/잠금) 상대가 가져간(unavailable) 캐릭터는 요청하지 않는다.
        // Idle = 선택 요청, Selected = 선택 취소 요청. (모드 판단은 LobbyCharacterConfirmationUI가 한다.)
        if (inputLocked || unavailable)
            return;

        if (state != SelectState.Idle && state != SelectState.Selected)
            return;

        if (role == Role.none)
        {
            Debug.LogWarning(
                "[Lobby Character] Role이 none이라 선택 요청을 보내지 않습니다. Inspector에서 Role을 지정하세요.",
                this);
            return;
        }

        SelectRequested?.Invoke(this);
    }

    // ------------------------------------------------------------------
    // Public State API (LobbyCharacterConfirmationUI가 호출)
    // ------------------------------------------------------------------

    /// <summary>Confirmation UI가 열림. Thinking/Outline을 유지한 채 추가 요청을 막는다.</summary>
    public void EnterPending()
    {
        if (state == SelectState.Selected)
            return;

        state = SelectState.Pending;

        // Ray가 이미 UI로 옮겨 갔더라도 대상 캐릭터는 계속 강조한다.
        SetHovered(true);
        SetOutline(true);
        ShowDetailPanel();
    }

    /// <summary>취소. Idle로 돌아가며 Thinking/Outline을 끄고 다시 Hover 가능하게 한다.</summary>
    public void CancelPending()
    {
        if (state == SelectState.Selected)
            return;

        state = SelectState.Idle;
        hoveringUIRay = null;
        SetHovered(false);
        SetOutline(false);
        HideDetailPanel();

        // Ray가 여전히 이 캐릭터 위에 있으면 hoverEntered가 다시 오지 않으므로 직접 복구한다.
        // (복구되면 TryBeginHover가 설명 패널을 현재 alpha에서 다시 Fade In 한다.)
        TryResumeHover();
    }

    /// <summary>Confirm. Thankful 1회 재생, Outline 유지, DetailPanel OFF, Glyph + Selection Effect 재생.</summary>
    public void PlaySelectedEffect()
    {
        // 두 번 호출되어도 Thankful이 다시 트리거되지 않게 한다.
        if (state == SelectState.Selected)
            return;

        state = SelectState.Selected;
        hoveringUIRay = null;

        PlaySelectedAnimation();

        // 선택된 캐릭터는 씬이 넘어갈 때까지 외곽선을 유지한다. (Thankful 마지막 포즈에서도 ON)
        SetOutline(true);
        HideDetailPanel();

        ShowGlyph();
        PlaySelectionEffect();
    }

    /// <summary>
    /// 선택 취소가 네트워크에 반영된 뒤 Selected 연출을 끝내고 Available(Idle)로 되돌린다.
    /// Host가 Owner를 None으로 바꾼 것을 확인한 뒤에만 호출한다.
    /// </summary>
    public void ClearSelected()
    {
        if (state != SelectState.Selected)
            return;

        state = SelectState.Idle;
        hoveringUIRay = null;

        // Thankful 마지막 포즈 → Breathing Idle 로 명시 복귀 (Thankful에는 나가는 전환이 없다).
        ReturnToIdleAnimation();

        SetOutline(false);
        HideDetailPanel();

        // Selected 연출 정리.
        HideGlyph();
        StopSelectionEffect(ParticleSystemStopBehavior.StopEmitting);

        // Ray가 여전히 이 캐릭터 위에 있으면 hoverEntered가 다시 오지 않으므로 직접 복구한다.
        TryResumeHover();
    }

    private void ReturnToIdleAnimation()
    {
        if (characterAnimator == null)
            return;

        // 남아 있는 Selected 트리거가 Any State → Thankful 로 다시 들어가지 않게 지운다.
        characterAnimator.ResetTrigger(SelectedHash);
        characterAnimator.SetBool(IsHoveredHash, false);
        characterAnimator.CrossFade(IdleStateHash, IdleReturnCrossFade, 0);
    }

    // Thankful 트리거와 IsHovered=false를 같은 프레임에 넣는다.
    // Any State → Thankful 전환이 상태 전환(Thinking → Idle)보다 우선 평가되므로
    // Thinking에서 Idle을 거치지 않고 바로 Thankful로 들어가고, Thankful이 끝난 뒤에는
    // IsHovered가 false라 Idle에 머문다(Thinking으로 돌아가지 않음).
    private void PlaySelectedAnimation()
    {
        if (characterAnimator == null)
            return;

        characterAnimator.ResetTrigger(SelectedHash);
        characterAnimator.SetTrigger(SelectedHash);
        characterAnimator.SetBool(IsHoveredHash, false);
    }

    /// <summary>
    /// Confirmation UI가 열려 있는 동안 Hover/Trigger 반응을 막는다.
    /// Pending/Selected 대상의 시각 상태는 건드리지 않는다.
    /// </summary>
    public void SetInputLocked(bool locked)
    {
        if (inputLocked == locked)
            return;

        inputLocked = locked;

        if (state != SelectState.Idle)
            return;

        if (locked)
        {
            // 잠기는 순간 Hover 중이던 다른 캐릭터는 강조를 내린다.
            if (hoveringUIRay != null)
                ClearHoveringUIRay();
        }
        else
        {
            // 잠금이 풀렸는데 Ray가 이미 위에 있으면 hoverEntered가 다시 오지 않으므로 직접 복구한다.
            TryResumeHover();
        }
    }

    // ------------------------------------------------------------------
    // Unavailable (상대가 선택한 캐릭터)
    // ------------------------------------------------------------------

    /// <summary>상대 플레이어가 이 역할을 가져가 선택할 수 없는 상태인지.</summary>
    public bool IsUnavailable => unavailable;

    /// <summary>
    /// 상대가 이 역할을 가져갔는지 반영한다. 같은 값이면 아무 작업도 하지 않는다.
    /// 내가 이미 선택한(Selected) 캐릭터에는 적용하지 않는다.
    /// </summary>
    public void SetUnavailable(bool value)
    {
        if (unavailable == value)
            return;

        // 내 Selected 연출(Thankful/Outline/Glyph)은 절대 회색으로 덮지 않는다.
        if (value && state == SelectState.Selected)
            return;

        unavailable = value;

        if (value)
        {
            // Hover 캐시와 연출을 먼저 정리한 뒤 입력을 막는다.
            hoveringUIRay = null;
            SetHovered(false);
            SetOutline(false);
            HideDetailPanel();

            SetInteractableEnabled(false);
            ApplyGray(true);
        }
        else
        {
            ApplyGray(false);
            SetInteractableEnabled(true);

            // Ray가 아직 이 영역을 가리키고 있으면 hoverEntered가 다시 오지 않으므로 직접 복구한다.
            TryResumeHover();
        }
    }

    // Collider는 그대로 두고 XRSimpleInteractable만 껐다 켠다.
    // (끄면 XRI가 등록을 해제하며 hoverExited를 보내 주고, Ray는 이 캐릭터를 그냥 지나간다.)
    private void SetInteractableEnabled(bool value)
    {
        if (interactable == null)
            return;

        if (interactable.enabled != value)
            interactable.enabled = value;
    }

    // 캐릭터 루트(부모) 아래 Renderer × 머티리얼 슬롯의 원본 색을 한 번만 캐시한다.
    private void CacheGrayTargets()
    {
        grayPropertyBlock = new MaterialPropertyBlock();

        Transform root = transform.parent;

        if (root == null)
        {
            grayTargets = new GrayTarget[0];
            return;
        }

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        List<GrayTarget> targets = new List<GrayTarget>();

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            // 최초 시점의 슬롯만 다룬다. (QuickOutline이 뒤에 붙이는 Mask/Fill 슬롯은 건드리지 않는다.)
            Material[] materials = renderer.sharedMaterials;

            if (materials == null)
                continue;

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];

                if (material == null)
                    continue;

                bool hasBaseColor = material.HasProperty(BaseColorMatId);
                bool hasColorDim = material.HasProperty(ColorDimId);

                if (!hasBaseColor && !hasColorDim)
                    continue;

                targets.Add(new GrayTarget
                {
                    Renderer = renderer,
                    MaterialIndex = i,
                    HasBaseColor = hasBaseColor,
                    BaseColor = hasBaseColor ? material.GetColor(BaseColorMatId) : Color.white,
                    HasColorDim = hasColorDim,
                    ColorDim = hasColorDim ? material.GetColor(ColorDimId) : Color.white
                });
            }
        }

        grayTargets = targets.ToArray();
    }

    // 상태가 바뀔 때만 호출된다. 해제는 MPB 자체를 제거해 원본 머티리얼 값이 그대로 드러나게 한다.
    private void ApplyGray(bool gray)
    {
        if (grayTargets == null)
            return;

        foreach (GrayTarget target in grayTargets)
        {
            if (target.Renderer == null)
                continue;

            if (!gray)
            {
                target.Renderer.SetPropertyBlock(null, target.MaterialIndex);
                continue;
            }

            grayPropertyBlock.Clear();

            if (target.HasBaseColor)
                grayPropertyBlock.SetColor(BaseColorMatId, ToGray(target.BaseColor));

            if (target.HasColorDim)
                grayPropertyBlock.SetColor(ColorDimId, ToGray(target.ColorDim));

            target.Renderer.SetPropertyBlock(grayPropertyBlock, target.MaterialIndex);
        }
    }

    // 원본 색 → 무채색으로 desaturation 만큼 섞고, brightness 를 곱한다. 알파는 원본 유지.
    private Color ToGray(Color source)
    {
        float luminance = source.grayscale;
        Color gray = new Color(luminance, luminance, luminance, source.a);

        Color result = Color.Lerp(source, gray, Mathf.Clamp01(unavailableDesaturation));
        float brightness = Mathf.Clamp01(unavailableBrightness);

        return new Color(result.r * brightness, result.g * brightness, result.b * brightness, source.a);
    }

    // ------------------------------------------------------------------
    // Hover
    // ------------------------------------------------------------------

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        Debug.Log("[Lobby Character] Hover Enter", this);

        // Near-Far 등 다른 Interactor도 함께 Hover할 수 있으므로
        // UI Interaction이 켜진 XRRayInteractor만 선택 입력·연출 대상으로 잡는다.
        if (!IsUIRayInteractor(args.interactorObject, out XRRayInteractor ray))
            return;

        TryBeginHover(ray);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        Debug.Log("[Lobby Character] Hover Exit", this);

        if (hoveringUIRay != null &&
            IsUIRayInteractor(args.interactorObject, out XRRayInteractor ray) &&
            ray == hoveringUIRay)
        {
            ClearHoveringUIRay();
        }
    }

    // UI Ray Hover를 시작한다. 잠금/Pending/Selected 상태에서는 연출을 켜지 않는다.
    private void TryBeginHover(XRRayInteractor ray)
    {
        // Selected 캐릭터도 Trigger(선택 취소)를 받기 위해 Ray는 캐시하지만 Hover 연출은 켜지 않는다.
        if (hoveringUIRay != null || inputLocked || unavailable)
            return;

        if (state != SelectState.Idle && state != SelectState.Selected)
            return;

        hoveringUIRay = ray;

        if (state != SelectState.Idle)
            return;

        SetHovered(true);
        SetOutline(true);
        ShowDetailPanel();
    }

    // 현재 이 영역을 Hover 중인 Interactor 중 UI Ray가 있으면 Hover 상태를 복구한다.
    private void TryResumeHover()
    {
        if (interactable == null || !isActiveAndEnabled || unavailable)
            return;

        foreach (IXRHoverInteractor hoverInteractor in interactable.interactorsHovering)
        {
            if (IsUIRayInteractor(hoverInteractor, out XRRayInteractor ray) && ray.isActiveAndEnabled)
            {
                TryBeginHover(ray);
                return;
            }
        }
    }

    // UI Ray 캐시를 버린다. Idle일 때만 애니메이션과 외곽선도 원래 상태로 되돌린다.
    // (Pending 중에는 Ray가 Confirmation UI로 옮겨 가도 강조를 유지해야 한다.)
    private void ClearHoveringUIRay()
    {
        hoveringUIRay = null;

        if (state != SelectState.Idle)
            return;

        SetHovered(false);
        SetOutline(false);
        HideDetailPanel();
    }

    private void SetHovered(bool hovered)
    {
        if (characterAnimator == null)
            return;

        characterAnimator.SetBool(IsHoveredHash, hovered);
    }

    // QuickOutline은 enabled 토글만으로 Mask/Fill 머티리얼을 붙였다 뗀다. 색·두께는 Inspector 값 그대로.
    private void SetOutline(bool enabled)
    {
        if (characterOutline == null)
            return;

        if (characterOutline.enabled != enabled)
            characterOutline.enabled = enabled;
    }

    private static bool IsUIRayInteractor(IXRInteractor interactor, out XRRayInteractor ray)
    {
        ray = interactor as XRRayInteractor;

        return ray != null && ray.enableUIInteraction;
    }

    // ------------------------------------------------------------------
    // Detail Panel (CanvasGroup Fade)
    // ------------------------------------------------------------------

    private void InitializeDetailPanel()
    {
        if (detailPanel == null)
            return;

        // 설명 전용 패널이라 Ray를 막거나 입력을 받을 필요가 없다. 항상 꺼 둔다.
        detailPanel.interactable = false;
        detailPanel.blocksRaycasts = false;

        ApplyDetailAlpha(0f);
    }

    private void ShowDetailPanel()
    {
        StartDetailFade(1f);
    }

    private void HideDetailPanel()
    {
        StartDetailFade(0f);
    }

    private void StartDetailFade(float targetAlpha)
    {
        if (detailPanel == null)
            return;

        StopDetailFade();

        if (!isActiveAndEnabled ||
            detailFadeDuration <= 0f ||
            Mathf.Approximately(detailPanel.alpha, targetAlpha))
        {
            ApplyDetailAlpha(targetAlpha);
            return;
        }

        detailFadeCoroutine = StartCoroutine(FadeDetailPanel(targetAlpha));
    }

    private void StopDetailFade()
    {
        if (detailFadeCoroutine == null)
            return;

        StopCoroutine(detailFadeCoroutine);
        detailFadeCoroutine = null;
    }

    // 현재 alpha에서 목표값으로 이어서 보간하므로 Fade 도중 방향이 바뀌어도 튀지 않는다.
    private IEnumerator FadeDetailPanel(float targetAlpha)
    {
        float startAlpha = detailPanel.alpha;

        // 남은 거리만큼만 시간을 써서 전체 왕복 속도를 일정하게 유지한다.
        float duration = detailFadeDuration * Mathf.Abs(targetAlpha - startAlpha);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            ApplyDetailAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));

            yield return null;
        }

        ApplyDetailAlpha(targetAlpha);
        detailFadeCoroutine = null;
    }

    private void ApplyDetailAlpha(float alpha)
    {
        if (detailPanel == null)
            return;

        detailPanel.alpha = alpha;
    }

    // ------------------------------------------------------------------
    // Selection Effect (ParticleSystem)
    // ------------------------------------------------------------------

    private void PlaySelectionEffect()
    {
        if (selectionEffect == null)
            return;

        // Burst(Time 0) 방식이라 이미 재생 중이어도 시간을 0으로 되돌려야 Burst가 다시 나온다.
        // 매번 Clear 후 재시작해 선택 순간에 Burst가 확실히 1회 발생하게 한다.
        selectionEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        selectionEffect.Play(true);
    }

    private void StopSelectionEffect(ParticleSystemStopBehavior stopBehavior)
    {
        if (selectionEffect == null)
            return;

        selectionEffect.Stop(true, stopBehavior);
    }

    // ------------------------------------------------------------------
    // Selection Glyph (Fade + Rotation)
    // ------------------------------------------------------------------

    private void InitializeGlyph()
    {
        glyphReady = false;

        if (selectionGlyphRenderer == null)
            return;

        // sharedMaterial은 인스턴스를 만들지 않는다. 원본 색을 읽기만 한다.
        Material sharedMaterial = selectionGlyphRenderer.sharedMaterial;

        if (sharedMaterial == null || !sharedMaterial.HasProperty(BaseColorId))
        {
            Debug.LogWarning(
                "[Lobby Character] Glyph Material에 _BaseColor가 없어 Fade를 적용할 수 없습니다.",
                selectionGlyphRenderer);
            return;
        }

        glyphBaseColor = sharedMaterial.GetColor(BaseColorId);
        glyphPropertyBlock = new MaterialPropertyBlock();
        glyphReady = true;

        ApplyGlyphAlphaBlend(0f);
    }

    private void ShowGlyph()
    {
        glyphRotating = true;
        StartGlyphFade(1f);
    }

    private void HideGlyph()
    {
        glyphRotating = false;
        StartGlyphFade(0f);
    }

    private void StartGlyphFade(float targetBlend)
    {
        if (!glyphReady)
            return;

        StopGlyphFade();

        if (!isActiveAndEnabled ||
            glyphFadeDuration <= 0f ||
            Mathf.Approximately(glyphAlphaBlend, targetBlend))
        {
            ApplyGlyphAlphaBlend(targetBlend);
            return;
        }

        glyphFadeCoroutine = StartCoroutine(FadeGlyph(targetBlend));
    }

    private void StopGlyphFade()
    {
        if (glyphFadeCoroutine == null)
            return;

        StopCoroutine(glyphFadeCoroutine);
        glyphFadeCoroutine = null;
    }

    // 현재 값에서 목표값으로 이어서 보간하므로 Enter/Exit가 빠르게 반복되어도 튀지 않는다.
    private IEnumerator FadeGlyph(float targetBlend)
    {
        float startBlend = glyphAlphaBlend;

        // 남은 거리만큼만 시간을 써서 전체 왕복 속도를 일정하게 유지한다.
        float duration = glyphFadeDuration * Mathf.Abs(targetBlend - startBlend);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            ApplyGlyphAlphaBlend(Mathf.Lerp(startBlend, targetBlend, t));

            yield return null;
        }

        ApplyGlyphAlphaBlend(targetBlend);
        glyphFadeCoroutine = null;
    }

    private void ApplyGlyphAlphaBlend(float blend)
    {
        glyphAlphaBlend = blend;

        if (!glyphReady)
            return;

        // RGB는 원본 그대로, 알파만 원본 알파 × blend.
        Color color = glyphBaseColor;
        color.a = glyphBaseColor.a * blend;

        selectionGlyphRenderer.GetPropertyBlock(glyphPropertyBlock);
        glyphPropertyBlock.SetColor(BaseColorId, color);
        selectionGlyphRenderer.SetPropertyBlock(glyphPropertyBlock);
    }

    private void RotateGlyph()
    {
        if (selectionGlyphRenderer == null)
            return;

        // Quad는 로컬 Z가 면의 법선이다. 바닥에 눕혀 놓았어도 로컬 Z 축 회전이
        // 곧 바닥 평면 안에서의 회전이므로 Quad가 기울어지지 않는다.
        selectionGlyphRenderer.transform.Rotate(
            0f,
            0f,
            glyphRotationSpeed * Time.deltaTime,
            Space.Self);
    }
}
