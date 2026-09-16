using System.Collections;
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
/// Hover 연출 (UI Ray Hover에만 반응, Near-Far 등 다른 Interactor는 무시):
/// - 형제 오브젝트(Base_end_1)의 Animator에 IsHovered=true/false.
///   (LobbyCharacterAnimator.controller: Idle ⇄ Thinking)
/// - Inspector에 연결한 발밑 Selection Effect(ParticleSystem)를 Play / StopEmitting.
/// - Inspector에 연결한 Glyph(Quad Renderer)를 알파 Fade In/Out 하고, Hover 중에는 천천히 회전시킨다.
///
/// Glyph 알파 처리 메모:
/// - Mental/Ear가 같은 LobbySelectionGlyph.mat(URP/Unlit, Transparent)을 공유하므로
///   MaterialPropertyBlock으로 Renderer마다 _BaseColor를 덮어써 서로 독립적으로 제어한다.
///   Material 인스턴스를 만들지 않고 원본 asset도 건드리지 않는다.
/// - RGB와 최대 알파는 원본 Material의 _BaseColor를 그대로 쓰고, 알파 비율(0~1)만 바꾼다.
///
/// 실제 선택 연출·LobbyManager 연결은 다음 단계에서 붙인다.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class LobbyCharacterSelectTarget : MonoBehaviour
{
    private static readonly int IsHoveredHash =
        Animator.StringToHash("IsHovered");

    private static readonly int BaseColorId =
        Shader.PropertyToID("_BaseColor");

    [Header("Selection Effect")]
    [SerializeField, Tooltip("발밑 파동 이펙트. Mental: MentalSelectionEffect/ActivateCircle, Ear: EarSelectionEffect/ActivateCircle")]
    private ParticleSystem selectionEffect;

    [SerializeField, Tooltip("발밑 마법진 Quad의 Renderer. Mental: MentalSelectionEffect/Glyph, Ear: EarSelectionEffect/Glyph")]
    private Renderer selectionGlyphRenderer;

    [SerializeField, Min(0f), Tooltip("Glyph가 나타나고 사라지는 데 걸리는 시간(초).")]
    private float glyphFadeDuration = 0.3f;

    [SerializeField, Tooltip("Hover 중 Glyph 회전 속도(도/초). 양수면 시계 방향, 음수면 반시계 방향.")]
    private float glyphRotationSpeed = 8f;

    private XRSimpleInteractable interactable;

    // 캐릭터 루트(Base / Base (1)) 아래 Base_end_1의 Animator. 없으면 null.
    private Animator characterAnimator;

    // 현재 이 영역을 Hover 중인 UI용 Ray Interactor. 없으면 null.
    private XRRayInteractor hoveringUIRay;

    // Glyph 알파 제어용. 원본 Material의 _BaseColor(RGB + 최대 알파)를 보존한다.
    private MaterialPropertyBlock glyphPropertyBlock;
    private Color glyphBaseColor = Color.white;
    private bool glyphReady;

    // 0 = 숨김, 1 = 원본 알파. Fade가 중간에 끊겨도 여기서 이어서 보간한다.
    private float glyphAlphaBlend;
    private Coroutine glyphFadeCoroutine;
    private bool glyphRotating;

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

        // Play On Awake가 켜져 있어도 Hover 전에는 돌지 않도록 시작 시 정리한다.
        StopSelectionEffect(ParticleSystemStopBehavior.StopEmittingAndClear);

        // 첫 프레임이 그려지기 전에 알파 0을 넣어 Glyph가 번쩍이지 않게 한다.
        InitializeGlyph();
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

        hoveringUIRay = null;
        SetHovered(false);

        // 비활성화될 때는 남은 파동까지 즉시 정리한다.
        StopSelectionEffect(ParticleSystemStopBehavior.StopEmittingAndClear);

        // 비활성 상태에서는 Coroutine을 돌릴 수 없으므로 Glyph도 즉시 숨긴다.
        glyphRotating = false;
        StopGlyphFade();
        ApplyGlyphAlphaBlend(0f);
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
        }
    }

    // ------------------------------------------------------------------
    // Hover
    // ------------------------------------------------------------------

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        Debug.Log("[Lobby Character] Hover Enter", this);

        // Near-Far 등 다른 Interactor도 함께 Hover할 수 있으므로
        // UI Interaction이 켜진 XRRayInteractor만 선택 입력·연출 대상으로 잡는다.
        if (hoveringUIRay == null && IsUIRayInteractor(args.interactorObject, out XRRayInteractor ray))
        {
            hoveringUIRay = ray;
            SetHovered(true);
            PlaySelectionEffect();
            ShowGlyph();
        }
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

    // UI Ray 캐시를 버리면서 애니메이션과 이펙트도 원래 상태로 되돌린다.
    private void ClearHoveringUIRay()
    {
        hoveringUIRay = null;
        SetHovered(false);
        StopSelectionEffect(ParticleSystemStopBehavior.StopEmitting);
        HideGlyph();
    }

    private void SetHovered(bool hovered)
    {
        if (characterAnimator == null)
            return;

        characterAnimator.SetBool(IsHoveredHash, hovered);
    }

    private static bool IsUIRayInteractor(IXRInteractor interactor, out XRRayInteractor ray)
    {
        ray = interactor as XRRayInteractor;

        return ray != null && ray.enableUIInteraction;
    }

    // ------------------------------------------------------------------
    // Selection Effect (ParticleSystem)
    // ------------------------------------------------------------------

    private void PlaySelectionEffect()
    {
        if (selectionEffect == null)
            return;

        // StopEmitting 상태에서 다시 Play하면 방출이 재개된다. 이미 재생 중이면 그대로 둔다.
        if (!selectionEffect.isEmitting)
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

        // Quad는 로컬 Z가 면의 법선이다. 바닥에 눕혀 놓았어도(X -90°) 로컬 Z 축 회전이
        // 곧 바닥 평면 안에서의 회전이므로 Quad가 기울어지지 않는다.
        selectionGlyphRenderer.transform.Rotate(
            0f,
            0f,
            glyphRotationSpeed * Time.deltaTime,
            Space.Self);
    }
}
