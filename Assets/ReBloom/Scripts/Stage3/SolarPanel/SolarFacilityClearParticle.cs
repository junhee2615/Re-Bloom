using UnityEngine;
using UnityEngine.Rendering;

namespace ReBloom.Solar
{
    /// <summary>
    /// 태양광 시설 전체 클리어 시 패널 주변에 띄우는 반딧불 형태의 빛 입자.
    ///
    /// 하나의 패널(또는 패널 그룹 중심)에 붙여서 사용한다.
    /// 기본 비활성 — 전체 클리어 컷씬이 <see cref="Play"/>를 호출하면 재생을 시작한다.
    ///
    /// Inspector에서 크기·수량·속도·색상·발광 강도를 조절할 수 있다.
    /// URP Bloom(threshold=0.9)과 어울리도록 HDR 색상을 쓴다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Facility Clear Particle")]
    public class SolarFacilityClearParticle : MonoBehaviour
    {
        [Header("방출 영역")]
        [Tooltip("입자가 방출되는 XZ 반경(m).")]
        [SerializeField] float emitRadius = 1.5f;

        [Header("입자 수량·수명")]
        [Tooltip("초당 방출 개수.")]
        [SerializeField] float rateOverTime = 12f;

        [Tooltip("입자 하나의 수명(초).")]
        [SerializeField] float particleLifetime = 4f;

        [Header("크기")]
        [Tooltip("입자 시작 크기 범위 — 최솟값.")]
        [SerializeField] float sizeMin = 0.03f;

        [Tooltip("입자 시작 크기 범위 — 최댓값.")]
        [SerializeField] float sizeMax = 0.08f;

        [Header("속도")]
        [Tooltip("위로 상승하는 속도(m/s).")]
        [SerializeField] float riseSpeed = 0.3f;

        [Tooltip("좌우 흔들림 세기.")]
        [SerializeField] float swayStrength = 0.15f;

        [Header("색상")]
        [Tooltip("청록색 — HDR로 설정하면 Bloom이 걸린다.")]
        [ColorUsage(false, true)]
        [SerializeField] Color colorCyan = new Color(0f, 1.5f, 1.2f, 1f);

        [Tooltip("연두색 — HDR로 설정하면 Bloom이 걸린다.")]
        [ColorUsage(false, true)]
        [SerializeField] Color colorGreen = new Color(0.6f, 1.5f, 0.3f, 1f);

        [Header("발광")]
        [Tooltip("발광 강도 배율. 높일수록 Bloom 번짐이 강해진다.")]
        [SerializeField] float emissionIntensity = 2f;

        [Header("텍스처")]
        [Tooltip("파티클에 쓸 텍스처. 비어 있으면 텍스처 없이 작동한다.")]
        [SerializeField] Texture2D particleTexture;

        ParticleSystem ps;
        ParticleSystemRenderer psRenderer;
        Material particleMaterial;

        [Header("Debug")]
        [SerializeField] bool debugLog;

        public bool IsPlaying => ps != null && ps.isPlaying;

        void Log(string msg)
        {
            if (!debugLog) return;
            Debug.Log($"[Stage3Cutscene] F{Time.frameCount} T{Time.realtimeSinceStartup:F3} ClearParticle({name}) - {msg}", this);
        }

        static string GetHierarchyPath(Transform t)
        {
            if (t == null) return "null";
            string path = t.name;
            Transform p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        public void Play()
        {
            EnsureBuilt();
            if (ps != null)
            {
                ps.gameObject.SetActive(true);
                ps.Clear(true);
                ps.Play(true);

                Transform parent = ps.transform.parent;
                string parentInfo = parent != null
                    ? $"{parent.name} (activeSelf={parent.gameObject.activeSelf}, activeInHierarchy={parent.gameObject.activeInHierarchy})"
                    : "NONE (scene root)";
                Log($"Play: GO.activeSelf={ps.gameObject.activeSelf}, GO.activeInHierarchy={ps.gameObject.activeInHierarchy}, " +
                    $"parent={parentInfo}, hierarchy={GetHierarchyPath(ps.transform)}, " +
                    $"isPlaying={ps.isPlaying}, isEmitting={ps.isEmitting}, isStopped={ps.isStopped}, " +
                    $"particleCount={ps.particleCount}, material={psRenderer?.material?.name ?? "null"}");
            }
            else
            {
                Log("Play: ps가 null — EnsureBuilt 실패");
            }
        }

        public void Stop(bool clearParticles = false)
        {
            if (ps == null) return;
            ps.Stop(true, clearParticles
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting);
        }

        void OnDestroy()
        {
            if (particleMaterial != null) Destroy(particleMaterial);
            if (ps != null) Destroy(ps.gameObject);
        }

        void EnsureBuilt()
        {
            if (ps != null) return;

            GameObject go = new GameObject("_ClearParticle");
            // 씬 루트에 배치 — 부모(FacilityClearParticle)가 SolarPanelCleanView에 의해
            // 비활성화되어도 _ClearParticle의 activeInHierarchy가 유지된다.
            go.transform.SetPositionAndRotation(transform.position, transform.rotation);

            ps = go.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.playOnAwake = false;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            psRenderer = go.GetComponent<ParticleSystemRenderer>();

            ConfigureMain();
            ConfigureEmission();
            ConfigureShape();
            ConfigureVelocity();
            ConfigureNoise();
            ConfigureColorOverLifetime();
            ConfigureSizeOverLifetime();
            ConfigureRenderer();

            go.SetActive(false);

            Log($"EnsureBuilt: shader={psRenderer?.material?.shader?.name ?? "null"}, " +
                $"emission.rate={ps.emission.rateOverTime.constant}, " +
                $"worldPos={go.transform.position}, parent={GetHierarchyPath(go.transform)}, " +
                $"ownerHierarchy={GetHierarchyPath(transform)} (activeSelf={gameObject.activeSelf}, activeInHierarchy={gameObject.activeInHierarchy})");
        }

        void ConfigureMain()
        {
            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = particleLifetime;
            main.startSpeed = riseSpeed;
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.gravityModifier = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(
                ApplyIntensity(colorCyan), ApplyIntensity(colorGreen));
        }

        void ConfigureEmission()
        {
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = rateOverTime;
        }

        void ConfigureShape()
        {
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = emitRadius;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            shape.rotation = new Vector3(-90f, 0f, 0f);
        }

        void ConfigureVelocity()
        {
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.5f, riseSpeed);
            vel.x = new ParticleSystem.MinMaxCurve(-swayStrength * 0.3f, swayStrength * 0.3f);
            vel.z = new ParticleSystem.MinMaxCurve(-swayStrength * 0.3f, swayStrength * 0.3f);
        }

        void ConfigureNoise()
        {
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = swayStrength;
            noise.frequency = 0.5f;
            noise.scrollSpeed = 0.3f;
            noise.octaveCount = 2;
            noise.damping = true;
        }

        void ConfigureColorOverLifetime()
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;

            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.white, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.8f, 0.15f),
                    new GradientAlphaKey(1f, 0.4f),
                    new GradientAlphaKey(0.6f, 0.75f),
                    new GradientAlphaKey(0f, 1f)
                });
            col.color = gradient;
        }

        void ConfigureSizeOverLifetime()
        {
            var size = ps.sizeOverLifetime;
            size.enabled = true;

            var curve = new AnimationCurve();
            curve.AddKey(0f, 0.5f);
            curve.AddKey(0.3f, 1f);
            curve.AddKey(0.7f, 1f);
            curve.AddKey(1f, 0.3f);
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        void ConfigureRenderer()
        {
            particleMaterial = CreateParticleMaterial();
            if (particleMaterial != null)
                psRenderer.material = particleMaterial;

            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.sortMode = ParticleSystemSortMode.Distance;
            psRenderer.shadowCastingMode = ShadowCastingMode.Off;
            psRenderer.receiveShadows = false;
            psRenderer.minParticleSize = 0f;
            psRenderer.maxParticleSize = 0.5f;
        }

        Color ApplyIntensity(Color hdr)
        {
            float maxChannel = Mathf.Max(hdr.r, hdr.g, hdr.b);
            if (maxChannel <= 0f) return hdr;
            float scale = emissionIntensity / maxChannel;
            return new Color(hdr.r * scale, hdr.g * scale, hdr.b * scale, hdr.a);
        }

        Material CreateParticleMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[SolarFacilityClearParticle] URP Particles/Unlit 셰이더를 찾지 못했습니다.");
                return null;
            }

            Material mat = new Material(shader) { name = "ClearParticle" };
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetColor("_BaseColor", Color.white);

            if (particleTexture != null)
                mat.SetTexture("_BaseMap", particleTexture);

            return mat;
        }
    }
}
