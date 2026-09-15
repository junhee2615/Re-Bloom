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
    [Tooltip("공명 거리를 벗어난 상태가 이만큼 연속으로 유지돼야 제약이 다시 걸린다. 그 전에 돌아오면 타이머는 0으로 리셋된다. 0이면 넘는 즉시 걸린다.")]
    [SerializeField, Min(0f)] private float reengageGraceSeconds = 2f;
    [Tooltip("거리 계산과 Fog 적용값을 매 프레임 출력합니다. 원인 확인 후 끄세요.")]
    [SerializeField] private bool logDistanceCalculation;

    [Header("Constraint Effects")]
    [SerializeField] private FogConstraint fog = new FogConstraint();
    [SerializeField] private VolumeConstraint volume = new VolumeConstraint();
    [SerializeField] private AudioConstraint audioConstraint = new AudioConstraint();

    [Header("Debug")]
    [Tooltip("테스트용: 끄면 안개·Volume·청각 제약 연출을 모두 비활성화한다. 플레이 중 실시간 토글 가능.")]
    [SerializeField] private bool constraintEnabled = true;

    private bool isConstraintReleased;
    private bool hasContext;
    private bool resonanceDistanceResolved;

    // StartReleased(스테이지 설정)를 네트워크 상태에 한 번만 시드하기 위한 플래그.
    private bool startReleasedRequested;
    private bool startReleasedSeeded;

    // 공명 거리를 벗어난 채로 지난 시간. 돌아오면 0으로 리셋된다.
    private float overDistanceElapsed;

    // 마지막으로 적용한 역할 게이트. 역할이 늦게 확정되는 것을 따라잡기 위해 캐시한다.
    private bool appliedVisualGate;
    private bool appliedAudioGate;

    // 제약 연출이 꺼진 상태: 테스트 토글 OFF 또는 공명 해제.
    private bool IsConstraintInactive => !constraintEnabled || isConstraintReleased;

    // 역할별로 잃는 감각이 다르다. mental은 시야를, ear은 청각을 잃는다.
    // 역할이 아직 정해지지 않았으면 둘 다 걸지 않는다.
    // RoleManager.LocalRole은 미지정일 때 enum 기본값 mental이 나오므로, 그대로 읽으면 로비·씬 전환 구간에서 엉뚱한 쪽에 시각 제약이 걸린다.
    // LocalIsMental / LocalIsEar은 HasLocalRole을 함께 보므로 미지정이면 둘 다 false다.
    private bool VisualInactive => IsConstraintInactive || !RoleManager.LocalIsMental;
    private bool AudioInactive => IsConstraintInactive || !RoleManager.LocalIsEar;

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

        // 각 서브 오브젝트가 자체 initialized 가드를 갖는다.
        fog.Initialize(this);
        volume.Initialize(constraintEnabled, this);

        if (_currentContext != null) Bind(_currentContext);
        else ApplyNoContext();
    }

    private void OnDisable()
    {
        if (Instance != this) return;

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

        // 해제 여부의 단일 진실 공급원은 NetworkPlayer.HasCooperativeActivationSucceeded 다.
        // StartReleased는 플레이어 스폰 전까지의 초기값이자, 권한 피어가 네트워크 상태에
        // 한 번 시드할 요청으로만 쓰인다. (SyncReleaseState 참고)
        startReleasedRequested = context.StartReleased;
        startReleasedSeeded = false;
        isConstraintReleased = context.StartReleased;
        overDistanceElapsed = 0f;   // 이전 스테이지에서 세던 이탈 시간을 물려받지 않는다
        hasContext = true;

        ApplyConstraintEffects();                    // Bloom/Vignette + 청각 초기 상태
        fog.SnapConstrained(!VisualInactive);        // 안개 초기 강도
        volume.Snap(VisualInactive);                 // 전환 직후 weight 튐 방지
        audioConstraint.UpdateRelief(AudioInactive ? 1f : 0f);
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
        audioConstraint.Apply(true);        // 정상(Master) 그룹
        audioConstraint.SetSources(null);   // 파괴된 AudioSource 참조 정리
        audioConstraint.UpdateRelief(1f);   // 믹서 스냅샷을 완화 상태로 되돌림
        fog.Apply(0f);
        volume.Snap(true);
        volume.ApplyPostFx(true);
    }

    private void Update()
    {
        if (!constraintEnabled || !hasContext)
            return;

        ResolvePlayers(out NetworkPlayer localPlayer, out NetworkPlayer remotePlayer);
        SyncReleaseState(localPlayer);
        SyncRoleGate();

        bool hasDistance = TryGetPlayerDistance(localPlayer, remotePlayer, out float playerDistance);

        // 가까워졌다가 다시 멀어지면 제약을 재적용한다.
        UpdateReengageTimer(localPlayer, hasDistance, playerDistance);

        float proximity = CalculateProximity(hasDistance, playerDistance);
        float interpolation = DistanceInterpolation();

        fog.Tick(proximity, VisualInactive, interpolation);
        volume.UpdateWeight(proximity, VisualInactive, interpolation);
        audioConstraint.UpdateRelief(AudioInactive ? 1f : proximity);

        if (logDistanceCalculation)
        {
            Debug.Log(
                $"proximity={proximity:F3}, fogIntensity={fog.CurrentIntensity:F3}, " +
                $"activeIntensity={fog.ActiveIntensity:F3}, " +
                $"reengage={overDistanceElapsed:F2}/{reengageGraceSeconds:F2}s", this);
        }
    }

    /// <summary>
    /// 거리 이탈이 <see cref="reengageGraceSeconds"/> 동안 <b>연속으로</b> 유지될 때만 제약을 되돌린다.
    /// 타이머는 모든 피어에서 돌지만(경고 UI가 남은 시간을 읽을 수 있도록) 실제 해제 취소는 StateAuthority만 호출한다. .
    /// </summary>
    private void UpdateReengageTimer(NetworkPlayer localPlayer, bool hasDistance, float playerDistance)
    {
        if (!isConstraintReleased)
        {
            overDistanceElapsed = 0f;
            return;
        }

        // 상대를 찾지 못한 프레임(스폰 전·씬 전환 직후)은 이탈로도, 복귀로도 세지 않는다.
        // 타이머를 그대로 두므로, 멀리 떨어진 채 전환을 마치면 이어서 센다.
        if (!hasDistance) return;

        if (playerDistance < resonanceDistance)
        {
            overDistanceElapsed = 0f;
            return;
        }

        overDistanceElapsed = Mathf.Min(overDistanceElapsed + Time.deltaTime, reengageGraceSeconds);
        if (overDistanceElapsed < reengageGraceSeconds) return;

        if (localPlayer == null || !localPlayer.HasNetworkStateAuthority) return;   // 권한 피어가 끊으면 그 결과가 복제돼 이쪽 타이머도 리셋된다

        overDistanceElapsed = 0f;
        ReengageConstraint();
    }

    /// <summary>
    /// 해제 여부는 네트워크 상태(<see cref="NetworkPlayer.HasCooperativeActivationSucceeded"/>)를
    /// 단일 진실 공급원으로 삼는다. 로컬 캐시와 어긋나면 연출을 다시 맞춘다.
    /// </summary>
    private void SyncReleaseState(NetworkPlayer localPlayer)
    {
        if (localPlayer == null)
            return;   // 스폰 전에는 Bind가 넣어둔 StartReleased 값을 유지한다

        // 스테이지가 "해제 상태로 시작"을 요구하면 권한 피어가 네트워크 상태에 한 번만 반영한다.
        if (startReleasedRequested && !startReleasedSeeded)
        {
            startReleasedSeeded = true;

            if (localPlayer.HasNetworkStateAuthority)
            {
                foreach (NetworkPlayer player in NetworkPlayer.All)
                {
                    if (player != null && player.Object != null && player.Object.IsValid)
                        player.SetCooperativeActivationSucceeded();
                }
                return;   // 복제된 상태는 다음 프레임에 읽는다
            }
        }

        bool released = localPlayer.HasCooperativeActivationSucceeded;
        if (released == isConstraintReleased)
            return;

        isConstraintReleased = released;
        ApplyConstraintEffects();
    }

    // 공명 on/off에 따른 효과(Bloom/Vignette + 청각)를 반영한다.
    private void ApplyConstraintEffects()
    {
        volume.ApplyPostFx(VisualInactive);
        audioConstraint.Apply(AudioInactive);
    }

    /// <summary>
    /// 역할이 확정되거나 바뀌면 <b>이산</b> 상태(믹서 그룹 라우팅, Bloom/Vignette on/off)를 다시 맞춘다.
    ///
    /// 안개와 Volume weight는 매 프레임 갱신돼 저절로 따라오지만 이 둘은 상태가 바뀔 때만 적용된다.
    /// 역할은 <see cref="NetworkPlayer.Spawned"/>에서 정해지므로 <see cref="Bind"/>보다 늦고,
    /// 그 순간을 놓치면 다음 해제 상태 변화까지 계속 어긋난 채로 남는다.
    ///
    /// 이벤트(<see cref="RoleManager.LocalRoleChanged"/>) 대신 값을 비교하는 이유가 둘이다.
    ///  - <see cref="RoleManager.ClearLocalRole"/>은 이벤트를 쏘지 않는다.
    ///  - 구독 수명이 씬 전환·중복 인스턴스 정리와 얽히면 해제를 빠뜨리기 쉽다.
    /// </summary>
    private void SyncRoleGate()
    {
        bool visual = RoleManager.LocalIsMental;
        bool audio = RoleManager.LocalIsEar;
        
        if (visual == appliedVisualGate && audio == appliedAudioGate) return;
        
        appliedVisualGate = visual;
        appliedAudioGate = audio;

        // 안개·weight는 스냅하지 않는다.
        // 역할이 확정되는 순간 화면이 튀는 것보다 distanceResponseSpeed로 밀려 들어오는 편이 낫다.
        ApplyConstraintEffects();
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

    /// <summary>유효한 로컬/원격 NetworkPlayer를 한 번에 찾는다.</summary>
    private static void ResolvePlayers(out NetworkPlayer localPlayer, out NetworkPlayer remotePlayer)
    {
        localPlayer = null;
        remotePlayer = null;

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            if (player.IsLocalNetworkRig)
                localPlayer = player;
            else
                remotePlayer ??= player;
        }
    }

    private static bool TryGetPlayerDistance(
        NetworkPlayer localPlayer, NetworkPlayer remotePlayer, out float playerDistance)
    {
        playerDistance = 0f;

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

    /// <summary>
    /// 거리 이탈이 유예 시간을 넘겨 제약을 다시 적용한다. StateAuthority에서만 호출된다.
    /// 네트워크 플래그만 내리고, 로컬 연출은 <see cref="SyncReleaseState"/> 가 복제된 상태를 보고 반영한다.
    /// </summary>
    private static void ReengageConstraint()
    {
        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player != null && player.Object != null && player.Object.IsValid)
                player.ClearCooperativeActivation();
        }
    }
}
