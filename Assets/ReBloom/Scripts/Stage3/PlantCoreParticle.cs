using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mission 3-2 식물 코어 씨앗 흡수 및 새싹 성장 파티클 연출.
///
/// PlantCoreClearCutscene이 타이밍에 맞춰 PlaySeedSink / PlaySproutGrow를 호출한다.
/// SolarFacilityClearParticle과 동일한 프로그래매틱 ParticleSystem 패턴 사용.
/// </summary>
[AddComponentMenu("ReBloom/Stage3/Plant Core Particle")]
public class PlantCoreParticle : MonoBehaviour
{
    // ── 씨앗 흡수 파티클 ─────────────────────────────────────

    [Header("씨앗 흡수 파티클")]
    [Tooltip("씨앗이 땅으로 스며들 때 나타나는 연두색 빛 입자 색상 (HDR).")]
    [ColorUsage(false, true)]
    [SerializeField] private Color seedColor = new Color(0.6f, 1.8f, 0.3f, 1f);

    [Tooltip("씨앗 파티클 방출 반경(m). 씨앗 군집 크기에 맞게 조절.")]
    [SerializeField] private float seedEmitRadius = 0.07f;

    [Tooltip("초당 씨앗 파티클 방출 수.")]
    [SerializeField] private float seedRateOverTime = 18f;

    [Tooltip("씨앗 파티클 하나의 수명(초).")]
    [SerializeField] private float seedLifetime = 0.75f;

    [Tooltip("씨앗 파티클 크기 범위 — 최솟값(m).")]
    [SerializeField] private float seedSizeMin = 0.006f;

    [Tooltip("씨앗 파티클 크기 범위 — 최댓값(m).")]
    [SerializeField] private float seedSizeMax = 0.02f;

    [Tooltip("씨앗 파티클 위쪽 상승 속도(m/s). 흡수 연출이므로 낮게 유지.")]
    [SerializeField] private float seedRiseSpeed = 0.06f;

    [Tooltip("씨앗 파티클 재생 시간(초). sinkDuration에 맞춤.")]
    [SerializeField] private float seedPlayDuration = 1f;

    // ── 새싹 성장 파티클 ─────────────────────────────────────

    [Header("새싹 성장 파티클")]
    [Tooltip("새싹이 자랄 때 주변에서 올라가는 연두색 빛 입자 색상 (HDR).")]
    [ColorUsage(false, true)]
    [SerializeField] private Color sproutColor = new Color(0.35f, 2f, 0.25f, 1f);

    [Tooltip("새싹 파티클 방출 반경(m). 새싹 한 개의 크기에 맞게 조절.")]
    [SerializeField] private float sproutEmitRadius = 0.1f;

    [Tooltip("초당 새싹 파티클 방출 수. 과도하면 새싹이 가려짐.")]
    [SerializeField] private float sproutRateOverTime = 7f;

    [Tooltip("새싹 파티클 하나의 수명(초).")]
    [SerializeField] private float sproutLifetime = 1.4f;

    [Tooltip("새싹 파티클 크기 범위 — 최솟값(m).")]
    [SerializeField] private float sproutSizeMin = 0.006f;

    [Tooltip("새싹 파티클 크기 범위 — 최댓값(m).")]
    [SerializeField] private float sproutSizeMax = 0.018f;

    [Tooltip("새싹 파티클 위쪽 상승 속도(m/s).")]
    [SerializeField] private float sproutRiseSpeed = 0.14f;

    // ── 내부 상태 ─────────────────────────────────────────────

    private readonly List<ParticleSystem> _seedSystems  = new List<ParticleSystem>();
    private readonly List<ParticleSystem> _sproutSystems = new List<ParticleSystem>();
    private readonly List<Material>       _materials     = new List<Material>();

    // ── 공개 API ──────────────────────────────────────────────

    /// <summary>씨앗이 땅으로 가라앉기 시작할 때 각 씨앗 위치에서 파티클 재생.</summary>
    public void PlaySeedSink(IEnumerable<Vector3> worldPositions)
    {
        foreach (var pos in worldPositions)
        {
            var ps = BuildParticleSystem(
                pos,
                seedColor,
                seedEmitRadius,
                seedRateOverTime,
                seedLifetime,
                seedSizeMin,
                seedSizeMax,
                seedRiseSpeed,
                loop: false,
                duration: seedPlayDuration);
            if (ps != null)
            {
                _seedSystems.Add(ps);
                ps.Play(true);
            }
        }
    }

    /// <summary>새싹이 자라기 시작할 때 각 새싹 위치에서 루프 파티클 재생.</summary>
    public void PlaySproutGrow(IEnumerable<Vector3> worldPositions)
    {
        foreach (var pos in worldPositions)
        {
            var ps = BuildParticleSystem(
                pos,
                sproutColor,
                sproutEmitRadius,
                sproutRateOverTime,
                sproutLifetime,
                sproutSizeMin,
                sproutSizeMax,
                sproutRiseSpeed,
                loop: true,
                duration: 5f);
            if (ps != null)
            {
                _sproutSystems.Add(ps);
                ps.Play(true);
            }
        }
    }

    /// <summary>모든 파티클 중단. clearParticles=true면 화면에서 즉시 제거.</summary>
    public void StopAll(bool clearParticles = true)
    {
        var stop = clearParticles
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        foreach (var ps in _seedSystems)
            if (ps != null) ps.Stop(true, stop);
        foreach (var ps in _sproutSystems)
            if (ps != null) ps.Stop(true, stop);
    }

    // ── 생성된 오브젝트 정리 ─────────────────────────────────

    private void OnDestroy()
    {
        StopAll(true);
        foreach (var ps in _seedSystems)
            if (ps != null) Destroy(ps.gameObject);
        foreach (var ps in _sproutSystems)
            if (ps != null) Destroy(ps.gameObject);
        foreach (var mat in _materials)
            if (mat != null) Destroy(mat);

        _seedSystems.Clear();
        _sproutSystems.Clear();
        _materials.Clear();
    }

    // ── 파티클 시스템 빌더 ───────────────────────────────────

    private ParticleSystem BuildParticleSystem(
        Vector3 worldPos,
        Color color,
        float emitRadius,
        float rate,
        float lifetime,
        float sizeMin,
        float sizeMax,
        float riseSpeed,
        bool  loop,
        float duration)
    {
        var go = new GameObject("_PlantParticle");
        // 씬 루트에 배치 — 부모 계층 비활성화 영향을 받지 않도록
        go.transform.SetPositionAndRotation(worldPos, Quaternion.identity);

        var ps = go.AddComponent<ParticleSystem>();

        // Main
        var main = ps.main;
        main.playOnAwake   = false;
        main.loop          = loop;
        main.duration      = duration;
        main.startLifetime = lifetime;
        main.startSpeed    = 0f; // velocityOverLifetime으로 제어
        main.startSize     = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        main.maxParticles  = 150;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.startColor    = color;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        // Emission
        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = rate;

        // Shape — XZ 평면 원형 방출
        var shape = ps.shape;
        shape.enabled  = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius   = emitRadius;
        shape.arcMode  = ParticleSystemShapeMultiModeValue.Random;
        shape.rotation = new Vector3(-90f, 0f, 0f);

        // Velocity — 위로 천천히 상승 + 미세 흔들림
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space   = ParticleSystemSimulationSpace.World;
        vel.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.5f, riseSpeed * 1.5f);
        vel.x = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.02f, 0.02f);

        // Noise — 유기적인 흔들림
        var noise = ps.noise;
        noise.enabled     = true;
        noise.strength    = 0.04f;
        noise.frequency   = 1f;
        noise.scrollSpeed = 0.4f;
        noise.octaveCount = 2;
        noise.damping     = true;

        // Color over lifetime — Fade in then out
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f,   0f),
                new GradientAlphaKey(0.9f, 0.2f),
                new GradientAlphaKey(0.8f, 0.6f),
                new GradientAlphaKey(0f,   1f)
            });
        col.color = gradient;

        // Size over lifetime — 커졌다 작아짐
        var sizeMod = ps.sizeOverLifetime;
        sizeMod.enabled = true;
        var sizeCurve = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.3f, 1f),
            new Keyframe(0.75f, 0.9f),
            new Keyframe(1f, 0.1f));
        sizeMod.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Renderer
        var rend = go.GetComponent<ParticleSystemRenderer>();
        rend.renderMode         = ParticleSystemRenderMode.Billboard;
        rend.sortMode           = ParticleSystemSortMode.Distance;
        rend.shadowCastingMode  = ShadowCastingMode.Off;
        rend.receiveShadows     = false;
        rend.minParticleSize    = 0f;
        rend.maxParticleSize    = 0.3f;

        var mat = CreateMaterial(color);
        if (mat != null)
        {
            rend.material = mat;
            _materials.Add(mat);
        }

        return ps;
    }

    private Material CreateMaterial(Color baseColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[PlantCoreParticle] URP Particles/Unlit 셰이더를 찾지 못했습니다.");
            return null;
        }

        var mat = new Material(shader) { name = "PlantCoreParticle_Mat" };
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 2f);                                       // Additive
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetColor("_BaseColor", Color.white);
        return mat;
    }
}
