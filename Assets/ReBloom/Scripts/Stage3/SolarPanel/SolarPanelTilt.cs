using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 시소 패널. 플레이어가 판 위 앞뒤 어디에 서 있느냐로 기울기가 정해진다.
    /// 양옆이 고정된 경첩이라 <b>pitch(앞뒤)로만</b> 움직인다
    ///
    /// 각도는 <b>수평(0°)을 기준</b>으로 잡는다. 씬에 배치된 각도에 더하는 것이 아니라,
    /// <see cref="maxPitch"/>가 수평에서 벗어날 수 있는 절대 한계다.
    /// 배치 각도를 기준으로 삼으면 판마다 도달 범위가 달라져(배치에 25°와 27°가 섞여 있다)
    /// 같은 조작에 다른 결과가 나온다.
    ///
    /// 판은 <b>올라서 있는 동안에만</b> 움직인다. 내려오면 수평으로 돌아가지 않고
    /// 마지막 각도 그대로 남는다 — 맞춰 놓은 각도가 유지돼야 다음 판으로 넘어갈 수 있다.
    ///
    /// 같은 오브젝트에 <see cref="SolarReflector"/>가 있고 <see cref="SolarReflector.broken"/>이면 밟아도 움직이지 않는다.
    /// 수리되는 순간부터 조절할 수 있다 — 고장 패널이 살아나면 정상 패널처럼 된다.
    ///
    /// 물리를 쓰지 않고 "선 위치 → 각도"를 직접 매핑한다. 이유가 둘이다.
    ///  - CharacterController는 Rigidbody를 밀지 않아 물리로 만들면 힘을 직접 줘야 하고,
    ///    네트워크 물리와 섞이면 튀는 동작을 잡기 어렵다.
    ///  - 입력(플레이어 위치)이 이미 네트워크로 복제되므로, 각 피어가 같은 각도를 독립적으로
    ///    계산할 수 있다. 각도를 따로 동기화할 필요가 없다.
    ///
    /// 그러려면 <b>모든 피어가 모든 플레이어를 봐야 한다.</b> 로컬 플레이어만 보면 상대가 올라선 판이
    /// 내 화면에서는 움직이지 않는다(루멘이 기울인 판이 에코에게 안 보이던 원인).
    /// 둘이 한 판에 서면 두 발 위치의 평균을 쓴다 — 어느 피어에서 계산해도 같은 규칙이다.
    ///
    /// 원격 플레이어 위치는 보간돼 늦게 오므로 내려오는 순간의 진행도가 피어마다 다르다.
    /// 그래서 내려와도 즉시 멈추지 않고 <b>마지막 목표각까지 마저 간다</b> — 최종 각도가
    /// "마지막 발 위치"만으로 정해져 피어 간 오차가 거의 사라진다.
    ///
    /// 배치: 판 면 Transform에 붙인다. 이 Transform이 회전한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Panel Tilt")]
    public class SolarPanelTilt : MonoBehaviour
    {
        [Header("기울기 — 수평(0°) 기준 절대 한계")]
        [Tooltip("앞뒤로 기울 수 있는 최대 각도(수평에서).")]
        public float maxPitch = 20f;
        [Tooltip("중심에서 앞뒤로 이만큼(m) 벗어나면 최대 각도가 된다. " +
                 "판 크기와 일부러 분리했다 — 판 끝까지 걸어가야 최대가 되면 한 걸음의 변화가 너무 작다.")]
        [Min(0.01f)] public float fullDeflectionDistance = 0.8f;
        [Tooltip("목표 각도까지 따라가는 시간(초). 클수록 묵직하게 움직인다.")]
        public float smoothTime = 0.5f;

        [Header("누가 올라서는가")]
        [Tooltip("이 판을 맡을 역할. none이면 누구든(모든 플레이어) 올라서면 움직인다.")]
        public Role occupantRole = Role.none;
        [Tooltip("발밑으로 이만큼(m) 내려 쏴서 이 판을 밟고 있는지 확인한다. " +
                 "리그 루트가 발 높이에 있으므로 0.5면 충분하다.")]
        [Min(0.05f)] public float standProbe = 0.5f;

        [Header("디버그")]
        [Tooltip("세션 없이 에디터에서 혼자 각도를 확인할 때 켠다. 아래 슬라이더로 직접 기울인다.")]
        public bool debugManualTilt;

        [Tooltip("앞뒤 기울기 −1~1. 1이면 maxPitch만큼 기운다.")]
        [Range(-1f, 1f)] public float debugForward;

        /// <summary>지금 누군가(어느 플레이어든) 올라서 있는가. 모든 피어에서 같은 값이다.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>
        /// 이 기기의 로컬 플레이어가 올라서 있는가. 피어마다 다르다.
        /// "내가 이 판 위에 있다"가 필요한 로컬 연출(진동 등)은 <see cref="IsOccupied"/>가 아니라 이것을 본다.
        /// </summary>
        public bool IsLocallyOccupied { get; private set; }

        /// <summary>지금 이 판의 법선(월드). 기울기가 반영된 현재 값이다.</summary>
        public Vector3 Normal => transform.up;

        // 판이 놓인 방향(yaw)만 남긴 수평 자세. 기울기를 재고 얹는 기준이다.
        Quaternion restYaw;

        // 밟는 면. 이 콜라이더에 맞아야 올라선 것으로 본다.
        Collider deck;

        // 같은 오브젝트의 거울. 고장이면 잠근다. 없으면(거울 아닌 판) 항상 움직인다.
        SolarReflector reflector;

        // 정규화된 현재 앞뒤 기울기 (-1..1).
        float current;
        float velocity;

        // 마지막으로 올라서 있던 동안의 목표. 내려온 뒤 여기까지 마저 간다.
        float target;
        bool settling;

        // 이 이하로 남으면 도착으로 본다(정규화 값, maxPitch 35°면 약 0.02°).
        const float SettleEpsilon = 0.0005f;

        void Awake()
        {
            restYaw = YawOnly(transform.rotation);
            reflector = GetComponent<SolarReflector>();

            deck = GetComponent<Collider>();
            if (deck == null)
                Debug.LogWarning($"[SolarPanelTilt] {name}: 콜라이더가 없어 올라섰는지 판정할 수 없습니다. " +
                                 $"판 면에 Collider를 붙여주세요.", this);
        }

        static Quaternion YawOnly(Quaternion r) => Quaternion.Euler(0f, r.eulerAngles.y, 0f);

        void Update()
        {
            // 고장난 판은 밟아도 움직이지 않는다. 각도는 배치된 그대로 남는다.
            if (reflector != null && reflector.broken)
            {
                IsOccupied = false;
                IsLocallyOccupied = false;
                settling = false;
                return;
            }

            bool localOnDeck = false;

            if (debugManualTilt)
            {
                target = debugForward;
                localOnDeck = true;
            }
            else if (TryGetFootPoint(out Vector3 foot, out localOnDeck))
            {
                // 감도는 판 크기가 아니라 fullDeflectionDistance로 정한다. 좌우 위치는 보지 않는다.
                Vector3 local = ToLevelLocal(foot);
                float reach = Mathf.Max(0.01f, fullDeflectionDistance);
                target = Mathf.Clamp(local.z / reach, -1f, 1f);
            }
            else
            {
                IsOccupied = false;
                IsLocallyOccupied = false;

                // 아무도 없으면 마지막 목표각까지만 마저 가고, 그 뒤로는 건드리지 않는다.
                // 내려오는 순간 멈추면 피어마다 멈춘 자리가 달라 각도가 영구히 갈린다.
                if (!settling) return;

                current = Mathf.SmoothDamp(current, target, ref velocity, smoothTime);
                if (Mathf.Abs(current - target) < SettleEpsilon)
                {
                    current = target;
                    settling = false;
                }

                ApplyTilt();
                return;
            }

            // 올라선 첫 프레임에는 지금 판이 놓인 각도에서 이어가야 한다.
            // 안 그러면 current 0에서 출발해 판이 수평으로 한 번 튄다.
            // 마저 가는 중(settling)에 다시 올라서면 이미 그 자리에서 이어지고 있으니 그대로 둔다.
            if (!IsOccupied && !settling)
            {
                current = CurrentTiltNormalized();
                velocity = 0f;
            }

            IsOccupied = true;
            IsLocallyOccupied = localOnDeck;
            settling = true;

            current = Mathf.SmoothDamp(current, target, ref velocity, smoothTime);
            ApplyTilt();
        }

        void ApplyTilt()
        {
            // 앞에 서면 앞이 내려간다(시소). 기준은 씬 배치 각도가 아니라 수평이다.
            transform.rotation = restYaw * Quaternion.Euler(current * maxPitch, 0f, 0f);
        }

        // 지금 판 각도를 -1~1 정규화 값으로 되돌린다(수평 기준).
        // 배치 각도가 한계를 넘으면 잘리므로, maxPitch는 배치 각도보다 크게 두는 편이 좋다.
        float CurrentTiltNormalized()
        {
            Vector3 e = (Quaternion.Inverse(restYaw) * transform.rotation).eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, e.x);

            return maxPitch > 0.01f ? Mathf.Clamp(pitch / maxPitch, -1f, 1f) : 0f;
        }

        // 발 위치를 "수평으로 눕힌" 판 좌표계로 옮긴다. 피하려는 것이 둘이다.
        //  - 회전 중인 transform으로 InverseTransformPoint를 하면 판이 기울수록 측정값이 변해
        //    각도가 스스로를 먹이는 되먹임이 생긴다.
        //  - 기운 좌표계로 재면 같은 한 걸음이 판 각도에 따라 다른 값으로 읽힌다. 플레이어는
        //    수평 바닥을 걷고 있으므로 재는 축도 수평이어야 걸음과 각도가 일정하게 대응한다.
        Vector3 ToLevelLocal(Vector3 world)
        {
            return Quaternion.Inverse(restYaw) * (world - transform.position);
        }

        // 이 판을 밟고 있는 플레이어들의 발(리그 루트) 위치 평균. 아무도 없으면 false.
        // localOnDeck = 그중에 이 기기의 로컬 플레이어가 있는가.
        //
        // 로컬 플레이어도 원격과 똑같이 네트워크 리그(PlayerTransform)로 잰다 — 하드웨어 리그를 쓰면
        // 내 쪽만 보간 지연이 없어 피어 간 입력이 달라진다.
        //
        // 주의: 리그 루트는 조이스틱 이동·텔레포트로만 움직인다. 룸스케일로 몸만 옮기면
        // 헤드셋만 움직이고 여기에는 잡히지 않는다.
        bool TryGetFootPoint(out Vector3 foot, out bool localOnDeck)
        {
            foot = default;
            localOnDeck = false;

            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (NetworkPlayer player in NetworkPlayer.All)
            {
                if (player == null) continue;
                if (occupantRole != Role.none && player.AssignedRole != occupantRole) continue;

                Transform rig = player.PlayerTransform;
                if (rig == null) continue;

                Vector3 p = rig.position;
                if (!StandingOnDeck(p)) continue;

                sum += p;
                count++;
                if (player == NetworkPlayer.LocalInstance) localOnDeck = true;
            }

            if (count == 0) return false;

            foot = sum / count;
            return true;
        }

        // 이 판을 실제로 밟고 있는가. 발밑으로 짧게 내려 쏴서 이 판의 콜라이더에 맞는지 본다.
        //
        // 위치 상자로 판정하면 판 옆이나 아래에 서 있어도 걸린다 — 판 간격이 4 m인데
        // 판 자체가 3.18 m라, 줄 안 어디에 서든 어느 한 판의 범위에 들어온다.
        // Collider.Raycast는 이 콜라이더 하나만 검사하므로 레이어 마스크가 필요 없고
        // 플레이어 자신의 캡슐에 가로막히지도 않는다.
        bool StandingOnDeck(Vector3 foot)
        {
            if (deck == null) return false;

            Ray ray = new Ray(foot + Vector3.up * standProbe, Vector3.down);
            return deck.Raycast(ray, out _, standProbe * 2f);
        }

        void OnDrawGizmosSelected()
        {
            Quaternion level = Application.isPlaying ? restYaw : YawOnly(transform.rotation);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, level, Vector3.one);

            // 앞뒤 선 두 개 = 최대 각도에 도달하는 거리(경첩 축 방향으로는 어디 서든 같다)
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.8f);
            const float halfWidth = 1.5f;
            for (int s = -1; s <= 1; s += 2)
            {
                float z = s * fullDeflectionDistance;
                Gizmos.DrawLine(new Vector3(-halfWidth, 0f, z), new Vector3(halfWidth, 0f, z));
            }

            // 밟는 면 = 이 오브젝트의 콜라이더 그대로
            Collider c = deck != null ? deck : GetComponent<Collider>();
            if (c == null) return;

            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = IsOccupied
                ? new Color(0.3f, 1f, 0.5f, 0.9f)
                : new Color(1f, 0.85f, 0.2f, 0.5f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
