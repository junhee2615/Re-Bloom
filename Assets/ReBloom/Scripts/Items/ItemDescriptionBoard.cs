using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 아이템 상자 위에 떠 있는 설명 보드.
/// <b>해당 아이템이 획득된 뒤</b>, 로컬 카메라가 가까이 오면 나타나고 멀어지면 사라진다.
///
/// 표시 조건은 둘이고 둘 다 만족해야 한다 —
///  ① <see cref="PlayerItemInventory.IsKindTaken"/>(<see cref="kind"/>) 이 true
///     (= 호스트가 획득을 확정해 복제된 상태)
///  ② 로컬 카메라가 <see cref="showDistance"/> 안에 있음
/// 한 번 획득되면 ①은 계속 true이므로, 그 뒤에는 거리만으로 반복해서 보고 닫을 수 있다.
///
/// 획득 판정을 <c>IsKindTaken</c> 에 맡기는 이유:
/// <c>HeldItem</c> 은 StateAuthority(호스트)만 쓰는 [Networked] 값이라, 그것이 true로
/// 보이는 시점이 곧 <b>서버 확정 시점</b>이다. 클라이언트가 임의로 앞당길 수 없다.
/// 또 이 함수는 로컬·원격 아바타의 인벤토리를 모두 훑으므로, <b>상대가 획득한 아이템</b>도
/// 내 화면에서 설명을 볼 수 있다. <c>GrabbableItem.Update</c> 와 <c>ItemPickupPromptUI</c> 가
/// 쓰는 것과 같은 판정이라 세 UI의 결론이 어긋나지 않는다 — 아이템 모델이 월드에서
/// 사라지는 바로 그 순간부터 이 보드가 켜질 자격을 얻는다.
///
/// 이 컴포넌트가 여전히 <b>건드리지 않는 것</b> — 획득 로직, 손목 인벤토리, 네트워크 상태.
/// <c>IsKindTaken</c> 을 읽기만 하고 아무것도 쓰지 않는다.
///
/// 왜 거리 계산이고 Trigger Collider가 아닌가:
/// 네트워크 아바타는 양쪽 피어에 모두 존재하므로, 트리거를 쓰면 <b>상대 플레이어</b>가
/// 상자에 다가가도 내 화면의 보드가 켜진다. <see cref="Camera.main"/> 은 각 기기의
/// XR Origin 안에 하나뿐이고 네트워크 오브젝트가 아니므로, 카메라 기준 거리는
/// 본질적으로 로컬 연산이다. 그래서 네트워크 코드가 한 줄도 필요하지 않다.
/// 덧붙여 아이템 획득 시 <c>GrabbableItem.RemoveFromWorld</c> 가 하위 Collider를 모두
/// 끄므로, 트리거 방식은 획득 후 판정이 죽을 위험도 있었다.
///
/// 배치: <c>Item_HearingAid</c> / <c>Item_Medicine</c> 의 직속 자식.
/// 아이템 모델(<c>HearingAid_low</c> 등)의 자식으로 두면 <b>잡았을 때 손에 끌려간다</b>.
/// 상자(<c>ItemBox_low</c>)는 보청기·약통이 같은 프리팹을 공유하므로 그 안에 넣으면
/// 두 상자가 같은 문구를 띄운다.
///
/// 거리 기준점은 <see cref="anchor"/> 로 상자 Transform을 지정한다. 보드는 상자보다
/// 위에 떠 있어서, 비워 두면 "상자에서 3m"가 보드 높이만큼 어긋난다.
/// </summary>
[AddComponentMenu("ReBloom/Items/Item Description Board")]
public class ItemDescriptionBoard : MonoBehaviour
{
    [Header("참조")]
    [Tooltip("페이드에 쓰는 CanvasGroup. 비우면 이 오브젝트에서 찾는다.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("아이템 아이콘 Image. 인스턴스별로 스프라이트를 바꾼다.")]
    [SerializeField] private Image iconImage;

    [Tooltip("제목 텍스트. 문구는 이 컴포넌트에서 쓰지 않고 TMP에 직접 입력한다.")]
    [SerializeField] private TMP_Text titleText;

    [Tooltip("설명 텍스트.")]
    [SerializeField] private TMP_Text descriptionText;

    [Tooltip("거리를 재는 기준 Transform. 보통 ItemBox_low를 연결한다. " +
             "비우면 이 보드 자신의 위치를 쓴다(보드가 상자보다 위에 있어 그만큼 오차가 난다).")]
    [SerializeField] private Transform anchor;

    [Header("아이템")]
    [Tooltip("이 상자가 담고 있던 아이템. 이 아이템이 획득되기 전에는 보드를 띄우지 않는다. " +
             "보청기 상자는 HearingAid, 약통 상자는 Medicine. " +
             "None이면 판정할 수 없으므로 아무것도 띄우지 않는다(잘못 띄우는 것보다 안전하다).")]
    [SerializeField] private ItemKind kind = ItemKind.None;

    [Header("거리")]
    [Tooltip("이 거리(m) 안으로 들어오면 나타난다.")]
    [SerializeField, Min(0f)] private float showDistance = 3f;

    [Tooltip("사라지는 거리를 이만큼(m) 더 멀게 잡는다. 경계에 서 있을 때 깜빡이는 것을 막는다.")]
    [SerializeField, Min(0f)] private float hideHysteresis = 0.25f;

    [Header("페이드")]
    [Tooltip("나타나고 사라지는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeDuration = 0.3f;

    [Tooltip("완전히 숨겨졌을 때 Canvas 렌더링까지 끈다. 보이지 않는 UI를 그리지 않게 한다.")]
    [SerializeField] private bool disableCanvasWhenHidden = true;

    [Header("Billboard")]
    [Tooltip("로컬 카메라를 향해 회전한다.")]
    [SerializeField] private bool billboard = true;

    [Tooltip("수평을 유지한다. 상자가 발밑에 있어 끄면 보드가 위로 꺾여 읽기 어렵다.")]
    [SerializeField] private bool lockPitch = true;

    [Header("테마")]
    [Tooltip("강조색을 적용할 Graphic들. AccentBar의 테두리 조각과 구분선을 넣는다. " +
             "비워 두면 각 Image의 색을 직접 쓴다.")]
    [SerializeField] private Graphic[] accentGraphics;

    [Tooltip("역할 강조색. 에코(보청기) #3DB3FF, 루멘(약통) #B16BF3 — Lobby의 EarBtn / MentalBtn과 같은 값이다.")]
    [SerializeField] private Color accentColor = new Color(0.24056602f, 0.7022988f, 1f, 1f);

    [Tooltip("제목에도 강조색을 쓸지. 끄면 제목은 TMP에 지정된 색을 유지한다.")]
    [SerializeField] private bool tintTitle;

    [Header("디버그")]
    [Tooltip("표시/숨김이 바뀔 때 로그를 남긴다.")]
    [SerializeField] private bool logVisibility;

    // 지금 보여야 하는지. 히스테리시스 때문에 이전 상태를 들고 있어야 한다.
    private bool visible;

    // kind 미설정 경고는 한 번만 낸다. LateUpdate에서 보므로 그냥 두면 매 프레임 쏟아진다.
    private bool warnedMissingKind;

    private Canvas ownCanvas;
    private Camera cachedCamera;
    private float alpha;

    private void Reset()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();

        ownCanvas = GetComponent<Canvas>();

        if (canvasGroup != null)
        {
            // 읽기 전용 보드다. Poke / Near-Far / Teleport Interactor의 판정을
            // 가로채지 않도록 런타임에도 한 번 더 못 박는다.
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        DisableRaycastTargets();
        ApplyAccent();

        // 멀리서 시작한다. 가까이 있으면 첫 프레임부터 페이드인된다.
        visible = false;
        alpha = 0f;
        ApplyAlpha(0f);
    }

    /// <summary>
    /// 카메라 포즈가 갱신된 뒤에 보려고 LateUpdate를 쓴다.
    /// TrackedPoseDriver가 Update 단계에서 HMD 위치를 옮기므로, 여기서 읽는 값이 최신이다.
    /// (<c>ItemPickupPromptUI</c> 도 같은 이유로 LateUpdate를 쓴다)
    /// </summary>
    private void LateUpdate()
    {
        Camera cam = ResolveCamera();

        if (cam == null)
        {
            // 카메라를 못 찾으면 안전하게 숨긴다. 뚝 끊지 않고 페이드아웃시킨다.
            SetVisible(false);
            UpdateFade();
            return;
        }

        UpdateVisibility(cam);
        UpdateFade();

        if (billboard)
            UpdateBillboard(cam);
    }

    /// <summary>
    /// 로컬 XR 카메라. null이 될 때만 다시 찾는다.
    /// </summary>
    /// <remarks>
    /// Opening에서는 <c>OpeningEnvironmentController</c> 가 MainCamera 태그를 잠시 떼므로
    /// <see cref="Camera.main"/> 이 null이 될 수 있다. 그 구간에도 예외가 나지 않아야 한다.
    /// </remarks>
    private Camera ResolveCamera()
    {
        if (cachedCamera != null)
            return cachedCamera;

        cachedCamera = Camera.main;
        return cachedCamera;
    }

    private void UpdateVisibility(Camera cam)
    {
        // 획득 전에는 거리를 보지 않는다. 서버가 확정하기 전에는 절대 뜨지 않아야 한다.
        if (!IsAcquired())
        {
            SetVisible(false);
            return;
        }

        Vector3 origin = anchor != null ? anchor.position : transform.position;

        // 보이는 중이면 조금 더 멀어져야 꺼진다(히스테리시스).
        float threshold = visible ? showDistance + hideHysteresis : showDistance;

        // sqrt를 피한다. 임계값도 함께 제곱해 비교한다.
        float sqrDistance = (cam.transform.position - origin).sqrMagnitude;

        SetVisible(sqrDistance <= threshold * threshold);
    }

    private void SetVisible(bool value)
    {
        if (visible == value)
            return;

        visible = value;

        if (logVisibility)
            Debug.Log($"[ItemDescriptionBoard] {name}: {(value ? "표시" : "숨김")}", this);
    }

    /// <summary>이 보드가 설명하는 아이템. 배치 도구와 검증이 읽는다.</summary>
    public ItemKind Kind => kind;

    /// <summary>
    /// 이 아이템이 획득되어 설명을 띄울 자격이 생겼는지.
    /// </summary>
    /// <remarks>
    /// <see cref="PlayerItemInventory.IsKindTaken"/> 은 static이고 내부에서 인스턴스·
    /// NetworkObject 유효성을 모두 걸러내므로, 아바타가 없는 프레임(스테이지 전환·재접속)에도
    /// 예외가 나지 않고 그냥 false를 돌려준다. 호스트 쪽은 ItemInventoryPersistence 기록까지
    /// 함께 보기 때문에 전환 직후에도 결론이 흔들리지 않는다.
    /// </remarks>
    private bool IsAcquired()
    {
        if (kind == ItemKind.None)
        {
            if (!warnedMissingKind)
            {
                warnedMissingKind = true;
                Debug.LogWarning(
                    $"[ItemDescriptionBoard] {name}: Kind가 None입니다. 획득 여부를 판정할 수 없어 " +
                    "보드를 띄우지 않습니다. 인스펙터에서 HearingAid 또는 Medicine을 지정하거나, " +
                    "'Re_Bloom ▸ Items ▸ Stage1 Item Description - Apply Kind Only' 를 실행하세요.",
                    this);
            }

            return false;
        }

        return PlayerItemInventory.IsKindTaken(kind);
    }

    private void UpdateFade()
    {
        float target = visible ? 1f : 0f;

        if (!Mathf.Approximately(alpha, target))
        {
            float step = fadeDuration <= 0f
                ? 1f
                : Time.deltaTime / fadeDuration;

            alpha = Mathf.MoveTowards(alpha, target, step);
        }

        ApplyAlpha(alpha);
    }

    private void ApplyAlpha(float value)
    {
        if (canvasGroup != null)
            canvasGroup.alpha = value;

        if (disableCanvasWhenHidden && ownCanvas != null)
        {
            bool shown = value > 0.001f;
            if (ownCanvas.enabled != shown)
                ownCanvas.enabled = shown;
        }
    }

    /// <summary>
    /// 카메라를 향하도록 회전한다.
    /// </summary>
    /// <remarks>
    /// 카메라 <b>반대</b> 방향을 LookRotation에 넣는다. UI의 앞면은 로컬 -Z를 보고 있어,
    /// +Z를 카메라에서 멀어지는 쪽으로 돌려야 앞면이 카메라를 향한다.
    /// (<c>ItemPickupPromptUI</c> 가 쓰는 것과 같은 식이다)
    /// </remarks>
    private void UpdateBillboard(Camera cam)
    {
        Vector3 fromCamera = transform.position - cam.transform.position;

        if (lockPitch)
            fromCamera.y = 0f;

        if (fromCamera.sqrMagnitude <= 0.000001f)
            return;

        transform.rotation = Quaternion.LookRotation(fromCamera, Vector3.up);
    }

    // ── 테마 / 안전 설정 ────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="accentColor"/> 를 <see cref="accentGraphics"/> 에 반영한다.
    /// 각 Graphic의 알파는 그대로 두어, 테두리와 구분선의 농도 차이를 유지한다.
    /// </summary>
    public void ApplyAccent()
    {
        if (accentGraphics != null)
        {
            foreach (Graphic graphic in accentGraphics)
            {
                if (graphic == null)
                    continue;

                graphic.color = new Color(
                    accentColor.r, accentColor.g, accentColor.b, graphic.color.a);
            }
        }

        if (tintTitle && titleText != null)
        {
            titleText.color = new Color(
                accentColor.r, accentColor.g, accentColor.b, titleText.color.a);
        }
    }

    /// <summary>
    /// 하위 모든 Graphic의 raycastTarget을 끈다.
    /// 프리팹에서 이미 꺼져 있지만, 나중에 자식을 추가했을 때도 XR 입력을 막지 않게 한다.
    /// </summary>
    private void DisableRaycastTargets()
    {
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.raycastTarget)
                graphic.raycastTarget = false;
        }
    }

#if UNITY_EDITOR
    /// <summary>에디터에서 강조색을 바꾸면 바로 보이게 한다.</summary>
    private void OnValidate()
    {
        if (Application.isPlaying)
            return;

        ApplyAccent();
    }
#endif
}
