using UnityEngine;

/// <summary>
/// Owns the co-op resonance constraint state (player distance + cooperative release)
/// and drives the fog / volume / audio effects from it. Fog is distance-only.
///
/// 싱글톤 오브젝트다. 상태와 연출은 이 컨트롤러가 소유하고,
/// 스테이지마다 바뀌는 "씬 전용" 참조는 <see cref="ResonanceSceneContext"/> 가 <see cref="Register"/> 로 주입한다.
/// </summary>
public sealed class ResonanceController : MonoBehaviour
{
    public static ResonanceController Instance { get; private set; }
    private static ResonanceSceneContext _currentContext;

    [Header("Distance-Based Constraint")]
    // 스폰 포인트가 없을 때의 폴백 기본값.
    private float resonanceDistance = 15f;
    [SerializeField, Min(0f)] private float minDistance = 2f;
    [SerializeField, Min(0f)] private float distanceResponseSpeed = 6f;
    [Tooltip("거리 계산과 Fog 적용값을 매 프레임 출력합니다. 원인 확인 후 끄세요.")]
    [SerializeField] private bool logDistanceCalculation;

    [Header("Constraint Effects")]
    [SerializeField] private FogConstraint fog = new FogConstraint();
    [SerializeField] private VolumeConstraint volume = new VolumeConstraint();
    [SerializeField] private AudioConstraint audioConstraint = new AudioConstraint();

    [Header("Debug")]
    [Tooltip("테스트용: 끄면 안개·Volume·청각 제약 연출을 모두 비활성화한다. 플레이 중 실시간 토글 가능.")]
    [SerializeField] private bool constraintEnabled = true;

    private bool initialized;
    private bool isConstraintReleased;
    private bool hasContext;
    private bool resonanceDistanceResolved;

    // 제약 연출이 꺼진 상태: 테스트 토글 OFF 또는 공명 해제.
    private bool IsConstraintInactive => !constraintEnabled || isConstraintReleased;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        // VRSystem 하위에 두면 부모가 이미 DontDestroyOnLoad이므로 별도 호출은 필요 없다.
        // 독립 루트로 둘 경우에만 DontDestroyOnLoad(gameObject) 를 켠다.

        initialized = fog.Initialize(this);
        volume.Initialize(constraintEnabled, this);

        if (_currentContext != null) Bind(_currentContext);
        else ApplyNoContext();
    }

    private void OnEnable()
    {
        if (Instance != this) return;
        CooperativeActivationController.ActivationSucceeded += ReleaseFogConstraint;
    }

    private void OnDisable()
    {
        if (Instance != this) return;
        CooperativeActivationController.ActivationSucceeded -= ReleaseFogConstraint;

        if (Application.isPlaying)
            fog.Apply(0f);
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        Instance = null;
        fog.Restore();
    }

    private void OnApplicationQuit()
    {
        if (Instance != this) return;
        fog.Restore();
    }

#if UNITY_EDITOR
    // 플레이 중 인스펙터에서 constraintEnabled를 토글하면 즉시 반영.
    // Update가 쉬는 disabled 상태에선 여기서 안개/weight를 OFF로 스냅한다.
    private void OnValidate()
    {
        if (!Application.isPlaying || !volume.Initialized) return;

        if (!constraintEnabled)
        {
            fog.Apply(0f);
            volume.Snap(true);
        }
        ApplyConstraintEffects();
    }
#endif

    // ── 씬 바인딩 ──────────────────────────────────────────────

    public static void Register(ResonanceSceneContext context)
    {
        if (context == null) return;
        _currentContext = context;
        if (Instance != null) Instance.Bind(context);
    }

    public static void Unregister(ResonanceSceneContext context)
    {
        if (_currentContext != context) return;   // 로드/언로드 순서에 무관하게 안전
        _currentContext = null;
        if (Instance != null) Instance.ApplyNoContext();
    }

    private void Bind(ResonanceSceneContext context)
    {
        ResolveResonanceDistance(context);           // 최초 1회만 책정, 이후 스테이지는 그대로 사용

        audioConstraint.SetSources(context.ConstrainedAudioSources);
        if (context.OverrideFogIntensity)
            fog.SetActiveIntensity(context.FogActiveIntensity);

        isConstraintReleased = context.StartReleased;
        hasContext = true;

        ApplyConstraintEffects();                    // Bloom/Vignette + 청각 초기 상태
        fog.SnapConstrained(!IsConstraintInactive);  // 안개 초기 강도
        volume.Snap(IsConstraintInactive);           // 전환 직후 weight 튐 방지
    }

    /// <summary>
    /// 공명 거리는 "최초로 바인딩된 스테이지"(정상 플로우에서는 Stage1)에서 한 번만 책정하고,
    /// 이후 스테이지에서는 그 값을 그대로 사용한다.
    /// </summary>
    private void ResolveResonanceDistance(ResonanceSceneContext context)
    {
        if (resonanceDistanceResolved)
            return;

        resonanceDistanceResolved = true;   // 성공/실패와 무관하게 여기서 확정

        if (context.HostSpawnPoint == null || context.ClientSpawnPoint == null)
        {
            Debug.LogWarning($"[Resonance] 스폰 포인트가 없어 기본 거리({resonanceDistance:F2})로 확정합니다.", this);
            return;
        }

        resonanceDistance = Vector3.Distance(context.HostSpawnPoint.position, context.ClientSpawnPoint.position);
        Debug.Log($"[Resonance] Resonance distance fixed at {resonanceDistance:F2} " + $"(source scene: {context.gameObject.scene.name})", this);
    }

    /// <summary>스테이지 컨텍스트가 없는 구간(StartScene, 전환 사이)의 안전 상태.</summary>
    private void ApplyNoContext()
    {
        hasContext = false;
        audioConstraint.SetSources(null);   // 파괴된 AudioSource 참조 정리
        audioConstraint.Apply(true);        // 정상(Master) 그룹
        audioConstraint.UpdateRelief(1f);   // 믹서 스냅샷을 완화 상태로 되돌림
        fog.Apply(0f);
        volume.Snap(true);
    }

    private void Update()
    {
        if (!initialized || !constraintEnabled || !hasContext)
            return;

        bool hasDistance = TryGetOtherPlayerDistance(out float playerDistance);

        // 가까워졌다가 다시 멀어지면 제약을 재적용하고 공명 성공 상태를 리셋
        if (isConstraintReleased && hasDistance && playerDistance >= resonanceDistance)
            ReengageConstraint();

        float proximity = CalculateProximity(hasDistance, playerDistance);
        float interpolation = DistanceInterpolation();

        fog.Tick(proximity, IsConstraintInactive, interpolation);
        volume.UpdateWeight(proximity, IsConstraintInactive, interpolation);
        audioConstraint.UpdateRelief(proximity);

        if (logDistanceCalculation)
        {
            Debug.Log(
                $"proximity={proximity:F3}, fogIntensity={fog.CurrentIntensity:F3}, " +
                $"activeIntensity={fog.ActiveIntensity:F3}", this);
        }
    }

    // 공명 on/off에 따른 효과(Bloom/Vignette + 청각)를 반영한다.
    private void ApplyConstraintEffects()
    {
        volume.ApplyPostFx(IsConstraintInactive);
        audioConstraint.Apply(IsConstraintInactive);
    }

    /// <summary>
    /// 두 플레이어 거리로 근접도[0..1]를 구한다.
    /// 0 = 공명 거리(멀리), 1 = minDistance 이내(가장 가까움).
    /// </summary>
    private float CalculateProximity(bool hasDistance, float playerDistance)
    {
        if (!hasDistance) return 0f;

        float clampedMinDistance = Mathf.Min(minDistance, resonanceDistance - 0.001f);
        return Mathf.InverseLerp(resonanceDistance, clampedMinDistance, playerDistance);
    }

    // distanceResponseSpeed 기반 프레임 독립 보간 계수.
    private float DistanceInterpolation()
    {
        return distanceResponseSpeed <= 0f
            ? 1f : 1f - Mathf.Exp(-distanceResponseSpeed * Time.deltaTime);
    }

    private static bool TryGetOtherPlayerDistance(out float playerDistance)
    {
        playerDistance = 0f;

        NetworkPlayer localPlayer = null;
        NetworkPlayer remotePlayer = null;
        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            if (player.IsLocalNetworkRig)
                localPlayer = player;
            else
                remotePlayer ??= player;
        }

        if (localPlayer == null || remotePlayer == null || remotePlayer.PlayerTransform == null)
            return false;

        // The local NetworkTransform can lag behind the XR rig in simulator and
        // in a network tick.  Use the real local rig position first, while the
        // remote player's replicated NetworkTransform remains the source for
        // the other player.
        Transform localTransform = localPlayer.HardwareRig != null
            ? localPlayer.HardwareRig.playerTransform
            : null;

        if (localTransform == null)
            localTransform = localPlayer.PlayerTransform;

        if (localTransform == null)
            return false;

        playerDistance = Vector3.Distance(localTransform.position, remotePlayer.PlayerTransform.position);
        return true;
    }

    private void ReleaseFogConstraint()
    {
        isConstraintReleased = true;
        ApplyConstraintEffects(); // 공명 해제 → Bloom/Vignette OFF, 청각 정상
    }

    /// <summary>
    /// 거리 이탈로 제약을 다시 적용한다.
    /// </summary>
    private void ReengageConstraint()
    {
        isConstraintReleased = false;
        ApplyConstraintEffects(); // 다시 제약 → Bloom/Vignette ON, 청각 제약

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player != null && player.Object != null && player.Object.IsValid)
                player.ClearCooperativeActivation();
        }
    }
}
