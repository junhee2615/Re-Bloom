using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 월드에 놓인 아이템 한 개. 손으로 잡히는 것과, 획득된 뒤 사라지는 것만 담당한다.
///
/// <see cref="XRGrabInteractable"/> 은 <b>"잡음 감지 전용"</b>으로만 쓴다 —
/// WaterMissionObstacle이 쓰는 방식과 같다. 획득 판정과 상태는 전부
/// <see cref="PlayerItemInventory"/>(네트워크)가 가진다.
///
/// 물리는 이 스크립트가 다루지 않는다. Rigidbody / Collider / XRGrabInteractable 설정은
/// Stage1의 기존 Grab 아이템(Fuel / GeneratorPart)과 동일하게 맞추고, 잡는 동안의
/// 손↔물체 충돌은 인터랙터에 붙어 있는 <c>HandGrabCollisionBridge</c> 가 전역으로 처리한다.
/// (여기서 Physics.IgnoreCollision을 따로 걸면 Bridge가 놓을 때 되돌려 서로 어긋난다)
///
/// 역할이 다른 아이템도 <b>잡을 수는 있다</b>. 획득만 막힌다. (요구사항)
/// 그래서 여기서는 역할을 보지 않고, 획득 판정은 <see cref="ItemPickupInput"/> 과
/// 호스트가 각각 수행한다.
///
/// 숨김 판정: 복제된 <see cref="PlayerItemInventory.HeldItem"/> 을 읽으므로
/// 누가 가져가면 <b>두 피어에서 함께</b> 사라진다. 자신이 상태를 쓰지 않으므로
/// 이 오브젝트에는 NetworkObject가 필요하지 않다.
///
/// 배치: 아이템 <b>모델</b>(HearingAid_low / Medicine_low). 담아 둔 상자(ItemBox_low)와
/// 그 Particle System 은 <b>형제</b>이므로 이 컴포넌트의 영향을 받지 않는다.
/// 즉 획득하면 모델만 사라지고 상자는 제자리에 남는다.
/// <code>
/// Item_HearingAid
/// ├── HearingAid_low      ← 이 컴포넌트
/// │   ├── Ear_Glass_low   ← 함께 사라짐
/// │   └── Ear_Metal_low   ← 함께 사라짐
/// └── ItemBox_low         ← 그대로 유지
///     └── Particle System ← 그대로 유지
/// </code>
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[AddComponentMenu("ReBloom/Items/Grabbable Item")]
public class GrabbableItem : MonoBehaviour
{
    [Header("아이템")]
    [Tooltip("이 오브젝트가 어떤 아이템인지. 에코=HearingAid, 루멘=Medicine.")]
    [SerializeField] private ItemKind kind = ItemKind.None;

    [Header("Debug")]
    [SerializeField] private bool logGrab = true;

    /// <summary>
    /// 지금 이 기기의 손에 잡혀 있는 아이템. 없으면 null.
    ///
    /// 양손 어느 쪽으로 잡아도 같은 값이 들어간다. <see cref="ItemPickupInput"/> 이
    /// "무엇을 잡고 있나"를 알기 위해 씬을 탐색하지 않도록 여기서 알려 준다.
    /// XRGrabInteractable은 로컬 상호작용이므로 이 값은 자연히 로컬 전용이다.
    /// </summary>
    public static GrabbableItem CurrentLocalGrabbed { get; private set; }

    public ItemKind Kind => kind;

    // 지금 월드에 살아 있는 아이템들. 플레이어가 나중에 스폰될 때 역방향으로 찾기 위해 둔다.
    // (PlayerItemInventory.Instances 와 같은 패턴)
    private static readonly List<GrabbableItem> Instances = new List<GrabbableItem>();

    private XRGrabInteractable grab;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Collider> colliders = new List<Collider>();

    private bool removed;

    /// <summary>
    /// 이 아이템이 이미 월드에서 치워졌는지. 안내 UI가 "획득되어 사라졌다"를 알 때 쓴다.
    /// 모델은 즉시 사라지지만 UI는 잠시 더 페이드되어야 하므로 이 값으로 구분한다.
    /// </summary>
    public bool IsRemoved => removed;

    private void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();

        // 메시는 자식(Ear_Glass_low / Ear_Metal_low 등)에 들어 있다.
        // 계층 깊이를 가정하지 않고 하위 전체에서 모아 둔다.
        GetComponentsInChildren(true, colliders);

        var allRenderers = new List<Renderer>();
        GetComponentsInChildren(true, allRenderers);

        // ParticleSystemRenderer는 제외한다. 모델 하위에 연출용 파티클이 생기더라도
        // 획득 시 그것까지 꺼 버리면 의도하지 않게 효과가 사라진다.
        foreach (Renderer itemRenderer in allRenderers)
        {
            if (itemRenderer is ParticleSystemRenderer)
                continue;

            renderers.Add(itemRenderer);
        }

        if (kind == ItemKind.None)
        {
            Debug.LogWarning(
                $"[Item] {name}: Kind가 None입니다. 인스펙터에서 HearingAid 또는 Medicine을 지정하세요.",
                this);
        }
    }

    private void OnEnable()
    {
        Instances.Add(this);

        // 다시 활성화될 때 충돌 무시가 초기화됐을 수 있으므로 여기서 다시 건다.
        IgnoreAllKnownPlayers();

        if (grab == null)
            return;

        grab.selectEntered.AddListener(OnSelectEntered);
        grab.selectExited.AddListener(OnSelectExited);
    }


    private void OnDisable()
    {
        Instances.Remove(this);

        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }

        // 꺼지는 순간 잡힌 상태로 기록이 남지 않게 한다.
        if (CurrentLocalGrabbed == this)
            CurrentLocalGrabbed = null;
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        CurrentLocalGrabbed = this;

        if (logGrab)
        {
            string hand = args.interactorObject != null
                ? args.interactorObject.handedness.ToString()
                : "Unknown";

            Debug.Log($"[Item] 잡음 - {ItemKindRules.DisplayName(kind)} ({hand})", this);
        }
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        if (CurrentLocalGrabbed == this)
            CurrentLocalGrabbed = null;

        // XRI는 잡는 동안 Rigidbody.isKinematic을 토글한다. 그때 PhysX 액터가 다시 만들어지며
        // Physics.IgnoreCollision 쌍이 초기화될 수 있으므로, 놓을 때마다 다시 건다.
        // (매 프레임이 아니라 이벤트 시점에만 돈다)
        IgnoreAllKnownPlayers();

        if (logGrab)
            Debug.Log($"[Item] 놓음 - {ItemKindRules.DisplayName(kind)}", this);
    }

    // ── 플레이어 몸 Collider 통과 ───────────────────────────────────────────

    /// <summary>
    /// 월드에 있는 모든 아이템이 이 플레이어의 몸 Collider를 무시하게 한다.
    /// <c>NetworkPlayer.Spawned</c> 에서 호출한다 — 로컬·원격 모두.
    ///
    /// 왜 플레이어 쪽에서 불러 줘야 하는가:
    /// 아이템은 "앞으로 스폰될 플레이어"를 스스로 알 수 없다. 매 프레임 탐색을 피하려면
    /// 플레이어가 생길 때 알려 주는 쪽이 유일한 이벤트 지점이다. 반대로 아이템이 나중에
    /// 활성화되는 경우는 <see cref="IgnoreAllKnownPlayers"/> 가 처리한다.
    /// 두 방향을 모두 걸어 두므로 스폰 순서가 어떻든 적용된다.
    /// </summary>
    public static void IgnoreCollisionWithPlayerBody(Collider playerBody)
    {
        if (playerBody == null)
            return;

        for (int i = 0; i < Instances.Count; i++)
        {
            GrabbableItem item = Instances[i];

            if (item != null)
                item.ApplyPlayerBodyIgnore(playerBody);
        }
    }

    /// <summary>
    /// 지금 스폰되어 있는 모든 플레이어의 몸 Collider를 무시한다.
    /// <see cref="NetworkPlayer.All"/> 은 로컬·원격 모두를 담고 있어 양쪽이 함께 처리된다.
    /// </summary>
    private void IgnoreAllKnownPlayers()
    {
        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            ApplyPlayerBodyIgnore(player.GetComponent<Collider>());
        }
    }

    /// <summary>
    /// 이 아이템의 Collider들과 플레이어 몸 Collider의 충돌만 무시한다.
    ///
    /// <see cref="Physics.IgnoreCollision"/> 은 <b>콜라이더 쌍</b> 단위이므로
    /// 아이템과 바닥·벽·환경 오브젝트·손의 충돌은 그대로 남는다.
    /// Layer Collision Matrix를 바꾸면 그 레이어 전체에 영향이 가므로 쓰지 않는다.
    /// (NetworkPlayer가 아바타↔CharacterController에 쓰는 방식과 같다)
    ///
    /// 양쪽 콜라이더가 모두 활성·활성계층이어야 호출할 수 있다. 아니면 Unity가 에러를 뱉는다.
    /// </summary>
    private void ApplyPlayerBodyIgnore(Collider playerBody)
    {
        if (removed || playerBody == null)
            return;

        if (!playerBody.enabled || !playerBody.gameObject.activeInHierarchy)
            return;

        int applied = 0;

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider == null || itemCollider == playerBody)
                continue;

            if (!itemCollider.enabled || !itemCollider.gameObject.activeInHierarchy)
                continue;

            Physics.IgnoreCollision(itemCollider, playerBody, true);
            applied++;
        }

        if (applied > 0 && logGrab)
        {
            Debug.Log(
                $"[Item] 플레이어 몸 통과 적용 - {ItemKindRules.DisplayName(kind)} ↔ " +
                $"{playerBody.gameObject.name} ({applied}쌍)", this);
        }
    }

    private void Update()
    {
        if (removed || kind == ItemKind.None)
            return;

        // 누군가 획득하면(복제된 상태) 양쪽 기기에서 함께 사라진다.
        if (!PlayerItemInventory.IsKindTaken(kind))
            return;

        RemoveFromWorld();
    }

    /// <summary>
    /// 월드에서 치운다. GameObject를 끄지 않고 구성 요소만 끄는 이유는
    /// 잡혀 있는 중에도 XRI가 상호작용을 깔끔하게 취소할 수 있게 하기 위해서다.
    /// (XRGrabInteractable.enabled = false 는 선택을 정상적으로 해제한다)
    /// </summary>
    private void RemoveFromWorld()
    {
        removed = true;

        if (CurrentLocalGrabbed == this)
            CurrentLocalGrabbed = null;

        if (grab != null)
            grab.enabled = false;

        foreach (Collider itemCollider in colliders)
        {
            if (itemCollider != null)
                itemCollider.enabled = false;
        }

        // 모델은 같은 프레임에 즉시 지운다.
        //
        // 이 순서가 중요하다 — grab.enabled = false 는 선택 중인 Interactable을
        // Interaction Manager에서 등록 해제시키고, XRI가 그것을 "강제 Detach"로 처리해
        // SetupRigidbodyDrop()이 isKinematic=false / useGravity=true 를 복원한다.
        // 즉 아이템은 그 순간부터 자유 낙하하는 강체가 된다.
        // 렌더러를 같은 프레임에 끄면 그 낙하가 화면에 보이지 않는다.
        // (렌더러를 늦게 끄면 "손에서 떨어진 뒤 사라지는" 것으로 보인다)
        //
        // 상자(ItemBox_low)와 Particle System은 형제이므로 영향받지 않는다.
        foreach (Renderer itemRenderer in renderers)
        {
            if (itemRenderer != null)
                itemRenderer.enabled = false;
        }

        if (logGrab)
            Debug.Log($"[Item] 월드에서 제거 - {ItemKindRules.DisplayName(kind)}", this);
    }
}
