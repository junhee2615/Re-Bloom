using UnityEngine;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어의 박자를 <b>mental에게만</b> 빛 깜빡임으로 보여준다.
    /// 진동은 느끼지 못하므로, 이 깜빡임이 mental이 가진 유일한 타이밍 정보다.
    ///
    /// 배치: <see cref="ElectricityPulseClock"/>과 같은 GameObject. 깜빡일 대상은
    /// <see cref="lamps"/>(프리팹의 `ElecLight1~3_low`, `ElectricityEmiCoil_low`)에 넣는다.
    /// </summary>
    /// <remarks>
    /// 역할 분리를 <b>레이어 컬링이 아니라 발광을 끄는 것</b>으로 한다. 머티리얼 값은 피어마다
    /// 따로 들고 있어서, ear 쪽에서 꺼 두면 그 피어에서만 어둡다 — 카메라를 건드릴 필요가 없다.
    /// 공유 머티리얼을 더럽히지 않도록 <see cref="MaterialPropertyBlock"/>으로만 쓴다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Electricity Light Cue")]
    [DefaultExecutionOrder(300)]
    public class ElectricityLightCue : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("박자를 읽어 올 시계. 비우면 같은 오브젝트에서 찾는다.")]
        public ElectricityPulseClock clock;

        [Tooltip("깜빡일 발광체들. 비우면 자식 Renderer를 전부 쓴다.")]
        public Renderer[] lamps;

        [Tooltip("같이 깜빡일 실제 조명(선택).")]
        public Light[] lights;

        [Header("역할")]
        [Tooltip("이 빛을 볼 역할.")]
        public Role onlyForRole = Role.mental;

        [Tooltip("역할이 아직 정해지지 않았을 때도 보여줄지. 단독 테스트용.")]
        public bool driveWhenRoleUnknown = true;

        [Header("밝기 — 겹침이 가까울수록 세진다")]
        public Color emissionColor = new Color(1f, 0.85f, 0.4f);

        [Tooltip("두 박이 멀 때 한 번 깜빡일 때의 밝기.")]
        [Min(0f)] public float scatteredIntensity = 1.5f;

        [Tooltip("겹친 순간의 밝기.")]
        [Min(0f)] public float convergedIntensity = 8f;

        [Tooltip("꺼져 있을 때의 밝기. 0이면 완전히 어둡다.")]
        [Min(0f)] public float offIntensity;

        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        MaterialPropertyBlock block;
        float applied = -1f;   // 마지막으로 적용한 밝기. 같으면 건드리지 않는다.

        void Reset() => clock = GetComponent<ElectricityPulseClock>();

        void Awake()
        {
            if (clock == null) clock = GetComponent<ElectricityPulseClock>();
            if (clock == null) clock = FindFirstObjectByType<ElectricityPulseClock>();

            if (lamps == null || lamps.Length == 0) lamps = GetComponentsInChildren<Renderer>(true);

            block = new MaterialPropertyBlock();
        }

        /// <summary>이 피어가 빛을 봐야 하는가.</summary>
        public bool RoleMatches()
        {
            if (onlyForRole == Role.none) return true;
            if (!RoleManager.HasLocalRole) return driveWhenRoleUnknown;

            return RoleManager.IsLocalRole(onlyForRole);
        }

        void LateUpdate()
        {
            // 역할이 아니면 늘 꺼 둔다. 역할은 Bind보다 늦게 오므로 매 프레임 확인한다.
            if (clock == null || !RoleMatches())
            {
                Apply(offIntensity);
                return;
            }

            float target = clock.LightOn
                ? Mathf.Lerp(scatteredIntensity, convergedIntensity, clock.Convergence01)
                : offIntensity;

            Apply(target);
        }

        void Apply(float intensity)
        {
            if (Mathf.Approximately(intensity, applied)) return;
            applied = intensity;

            Color c = emissionColor * intensity;

            for (int i = 0; i < lamps.Length; i++)
            {
                Renderer r = lamps[i];
                if (r == null) continue;

                r.GetPropertyBlock(block);
                block.SetColor(EmissionColor, c);
                r.SetPropertyBlock(block);
            }

            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l == null) continue;

                l.intensity = intensity;
            }
        }
    }
}
