using UnityEngine;

/// <summary>
/// Owns the co-op resonance constraint state (player distance + cooperative release)
/// and drives the fog / volume / audio effects from it. Fog is distance-only.
/// </summary>
public sealed class ResonanceController : MonoBehaviour
{
    [Header("Distance-Based Constraint")]
    [SerializeField] private Transform hostSpawnPoint;
    [SerializeField] private Transform clientSpawnPoint;
    [SerializeField, Min(0f)] private float minDistance = 2f;
    [SerializeField, Min(0f)] private float distanceResponseSpeed = 6f;
    [Tooltip("거리 계산과 Fog 적용값을 매 프레임 출력합니다. 원인 확인 후 끄세요.")]
    [SerializeField] private bool logDistanceCalculation;
    private float resonanceDistance = 15f; // 스폰 포인트간 거리

    [Header("Constraint Effects")]
    [SerializeField] private FogConstraint fog = new FogConstraint();
    [SerializeField] private VolumeConstraint volume = new VolumeConstraint();
    [SerializeField] private AudioConstraint audioConstraint = new AudioConstraint();

    [Header("Debug")]
    [Tooltip("테스트용: 끄면 안개·Volume·청각 제약 연출을 모두 비활성화한다. 플레이 중 실시간 토글 가능.")]
    [SerializeField] private bool constraintEnabled = true;

    private bool initialized;
    private bool isConstraintReleased;

    // 제약 연출이 꺼진 상태: 테스트 토글 OFF 또는 공명 해제.
    private bool IsConstraintInactive => !constraintEnabled || isConstraintReleased;

    private void Awake()
    {
        InitializeResonanceDistance();
        initialized = fog.Initialize(this);
        volume.Initialize(constraintEnabled, this);

        ApplyConstraintEffects();               // Bloom/Vignette + 청각 초기 상태
        fog.SnapConstrained(constraintEnabled); // 안개 초기 강도
    }

    private void OnEnable()
    {
        CooperativeActivationController.ActivationSucceeded += ReleaseFogConstraint;
    }

    private void OnDisable()
    {
        CooperativeActivationController.ActivationSucceeded -= ReleaseFogConstraint;

        if (Application.isPlaying)
            fog.Apply(0f);
    }

    private void OnDestroy() => fog.Restore();

    private void OnApplicationQuit() => fog.Restore();

#if UNITY_EDITOR
    // 플레이 중 인스펙터에서 constraintEnabled를 토글하면 즉시 반영.
    // Update가 쉬는 disabled 상태에선 여기서 안개/weight를 OFF로 스냅한다.
    private void OnValidate()
    {
        if (!Application.isPlaying || !volume.Initialized) return;

        if (!constraintEnabled)
        {
            fog.Apply(0f);
            volume.SnapReleased();
        }
        ApplyConstraintEffects();
    }
#endif

    private void InitializeResonanceDistance()
    {
        if (hostSpawnPoint == null || clientSpawnPoint == null)
        {
            Debug.LogWarning($"[Resonance] Spawn point가 지정되지 않아 기본 거리({resonanceDistance})를 사용합니다.", this);
            return;
        }

        resonanceDistance = Vector3.Distance(hostSpawnPoint.position, clientSpawnPoint.position);
        Debug.Log($"[Resonance Fog] Spawn-point resonance distance initialized: {resonanceDistance:F2}", this);
    }

    private void Update()
    {
        if (!initialized || !constraintEnabled)
            return;

        bool hasDistance = TryGetOtherPlayerDistance(out float playerDistance);

        // 가까워졌다가 다시 멀어지면 제약을 재적용하고 공명 성공 상태를 리셋
        if (isConstraintReleased && hasDistance && playerDistance >= resonanceDistance)
            ReengageConstraint();

        float proximity = CalculateProximity(hasDistance, playerDistance);
        float interpolation = DistanceInterpolation();

        fog.Tick(proximity, IsConstraintInactive, interpolation);
        volume.UpdateWeight(proximity, IsConstraintInactive, interpolation);

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
