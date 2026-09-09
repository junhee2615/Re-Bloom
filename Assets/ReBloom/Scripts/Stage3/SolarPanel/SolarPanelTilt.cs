using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 시소 패널. 플레이어가 판 위 어디에 서 있느냐로 기울기가 정해진다.
    ///
    /// 각도는 <b>수평(0°)을 기준</b>으로 잡는다. 씬에 배치된 각도에 더하는 것이 아니라,
    /// <see cref="maxPitch"/>/<see cref="maxRoll"/>이 수평에서 벗어날 수 있는 절대 한계다.
    /// 배치 각도를 기준으로 삼으면 판마다 도달 범위가 달라져(배치에 25°와 27°가 섞여 있다)
    /// 같은 조작에 다른 결과가 나온다.
    ///
    /// 판은 <b>올라서 있는 동안에만</b> 움직인다. 내려오면 수평으로 돌아가지 않고
    /// 마지막 각도 그대로 남는다 — 맞춰 놓은 각도가 유지돼야 다음 판으로 넘어갈 수 있다.
    ///
    /// 물리를 쓰지 않고 "선 위치 → 각도"를 직접 매핑한다. 이유가 둘이다.
    ///  - CharacterController는 Rigidbody를 밀지 않아 물리로 만들면 힘을 직접 줘야 하고,
    ///    네트워크 물리와 섞이면 튀는 동작을 잡기 어렵다.
    ///  - 입력(플레이어 위치)이 이미 네트워크로 복제되므로, 각 피어가 같은 각도를 독립적으로
    ///    계산할 수 있다. 각도를 따로 동기화할 필요가 없다.
    ///
    /// 배치: 판 면 Transform에 붙인다. 이 Transform이 회전한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Panel Tilt")]
    public class SolarPanelTilt : MonoBehaviour
    {
        [Header("기울기 — 수평(0°) 기준 절대 한계")]
        [Tooltip("앞뒤로 기울 수 있는 최대 각도(수평에서).")]
        public float maxPitch = 20f;
        [Tooltip("좌우로 기울 수 있는 최대 각도(수평에서). 회전축은 로컬 Z이고, 기우는 방향이 로컬 X다. " +
                 "0이면 좌우가 잠긴다(양옆 고정 경첩).")]
        public float maxRoll = 20f;
        [Tooltip("중심에서 이만큼(m) 벗어나면 최대 각도가 된다. 앞뒤·좌우에 같은 값을 쓴다. " +
                 "판 크기와 일부러 분리했다 — 판 끝까지 걸어가야 최대가 되면 한 걸음의 변화가 너무 작다.")]
        [Min(0.01f)] public float fullDeflectionDistance = 0.4f;
        [Tooltip("목표 각도까지 따라가는 시간(초). 클수록 묵직하게 움직인다.")]
        public float smoothTime = 0.25f;

        [Header("누가 올라서는가")]
        [Tooltip("이 판을 맡을 역할. none이면 로컬 플레이어를 쓴다.")]
        public Role occupantRole = Role.none;
        [Tooltip("발밑으로 이만큼(m) 내려 쏴서 이 판을 밟고 있는지 확인한다. " +
                 "리그 루트가 발 높이에 있으므로 0.5면 충분하다.")]
        [Min(0.05f)] public float standProbe = 0.5f;

        [Header("디버그")]
        [Tooltip("세션 없이 에디터에서 혼자 각도를 확인할 때 켠다. 아래 두 슬라이더로 직접 기울인다.")]
        public bool debugManualTilt;

        [Tooltip("앞뒤 기울기 −1~1. 1이면 maxPitch만큼 기운다.")]
        [Range(-1f, 1f)] public float debugForward;

        [Tooltip("좌우 기울기 −1~1. 1이면 maxRoll만큼 기운다.")]
        [Range(-1f, 1f)] public float debugRight;

        /// <summary>지금 누군가 올라서 있는가.</summary>
        public bool IsOccupied { get; private set; }

        /// <summary>지금 이 판의 법선(월드). 기울기가 반영된 현재 값이다.</summary>
        public Vector3 Normal => transform.up;

        // 판이 놓인 방향(yaw)만 남긴 수평 자세. 기울기를 재고 얹는 기준이다.
        Quaternion restYaw;

        // 밟는 면. 이 콜라이더에 맞아야 올라선 것으로 본다.
        Collider deck;

        // 정규화된 현재 기울기 (-1..1). x = 앞뒤, y = 좌우.
        Vector2 current;
        Vector2 velocity;

        void Awake()
        {
            restYaw = YawOnly(transform.rotation);

            deck = GetComponent<Collider>();
            if (deck == null)
                Debug.LogWarning($"[SolarPanelTilt] {name}: 콜라이더가 없어 올라섰는지 판정할 수 없습니다. " +
                                 $"판 면에 Collider를 붙여주세요.", this);
        }

        static Quaternion YawOnly(Quaternion r) => Quaternion.Euler(0f, r.eulerAngles.y, 0f);

        void Update()
        {
            Vector2 target;

            if (debugManualTilt)
            {
                target = new Vector2(debugForward, debugRight);
            }
            else if (TryGetFootPoint(out Vector3 foot))
            {
                // 감도는 판 크기가 아니라 fullDeflectionDistance로 정한다.
                Vector3 local = ToLevelLocal(foot);
                float reach = Mathf.Max(0.01f, fullDeflectionDistance);
                target = new Vector2(
                    Mathf.Clamp(local.z / reach, -1f, 1f),
                    Mathf.Clamp(local.x / reach, -1f, 1f));
            }
            else
            {
                // 아무도 없으면 각도를 건드리지 않는다. 마지막 자세 그대로 남는다.
                IsOccupied = false;
                return;
            }

            // 올라선 첫 프레임에는 지금 판이 놓인 각도에서 이어가야 한다.
            // 안 그러면 current(0,0)에서 출발해 판이 수평으로 한 번 튄다.
            if (!IsOccupied)
            {
                current = CurrentTiltNormalized();
                velocity = Vector2.zero;
                IsOccupied = true;
            }

            current.x = Mathf.SmoothDamp(current.x, target.x, ref velocity.x, smoothTime);
            current.y = Mathf.SmoothDamp(current.y, target.y, ref velocity.y, smoothTime);

            // 앞에 서면 앞이 내려가고, 오른쪽에 서면 오른쪽이 내려간다(시소).
            // 기준은 씬 배치 각도가 아니라 수평이다.
            transform.rotation = restYaw *
                Quaternion.Euler(current.x * maxPitch, 0f, -current.y * maxRoll);
        }

        // 지금 판 각도를 -1~1 정규화 값으로 되돌린다(수평 기준).
        // 배치 각도가 한계를 넘으면 잘리므로, maxPitch는 배치 각도보다 크게 두는 편이 좋다.
        Vector2 CurrentTiltNormalized()
        {
            Vector3 e = (Quaternion.Inverse(restYaw) * transform.rotation).eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, e.x);
            float roll = Mathf.DeltaAngle(0f, e.z);

            return new Vector2(
                maxPitch > 0.01f ? Mathf.Clamp(pitch / maxPitch, -1f, 1f) : 0f,
                maxRoll > 0.01f ? Mathf.Clamp(-roll / maxRoll, -1f, 1f) : 0f);
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

        // 이 판을 맡은 플레이어의 발(리그 루트) 위치. 올라서 있지 않으면 false.
        //
        // 주의: 리그 루트는 조이스틱 이동·텔레포트로만 움직인다. 룸스케일로 몸만 옮기면
        // 헤드셋만 움직이고 여기에는 잡히지 않는다.
        bool TryGetFootPoint(out Vector3 foot)
        {
            foot = default;

            NetworkPlayer player;
            if (occupantRole == Role.none)
                player = NetworkPlayer.LocalInstance;
            else if (!NetworkPlayer.TryGetByRole(occupantRole, out player))
                return false;

            if (player == null) return false;

            Transform rig = player.PlayerTransform;
            if (rig == null) return false;

            Vector3 p = rig.position;
            if (!StandingOnDeck(p)) return false;

            foot = p;
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

            // 안쪽 사각형 = 최대 각도에 도달하는 거리
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(
                fullDeflectionDistance * 2f, 0.02f, fullDeflectionDistance * 2f));

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
