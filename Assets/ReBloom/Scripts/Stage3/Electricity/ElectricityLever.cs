using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어의 레버 하나. <b>정해진 역할만</b> 당길 수 있고, 끝까지 당긴 순간
    /// <see cref="ElectricityCoreMission.ReportPull"/>로 보고한다. 판정은 하지 않는다.
    ///
    /// 배치: 레버 손잡이 메시(`MentalLever_low` / `HearingLever_low`)에 붙이고
    /// <see cref="requiredRole"/>을 맞춘다. Collider와, <b><see cref="XRSimpleInteractable"/></b>처럼
    /// 오브젝트를 움직이지 않는 인터랙터블이 함께 필요하다.
    ///
    /// <b><see cref="XRGrabInteractable"/>을 쓰면 안 된다.</b> Track Position / Track Rotation을 모두 꺼도
    /// <c>Grab()</c>이 <c>m_Transform.SetParent(null)</c>로 <b>무조건 부모를 떼어 낸다</b>(XRI 3.3,
    /// XRGrabInteractable.cs:2507). 인스펙터로 막을 수 없고, 떼이면 두 가지가 동시에 무너진다 —
    /// 경첩 기준 각도 계산이 부모를 잃어 매 프레임 실패하고(= 레버가 아예 안 움직인다),
    /// <c>localRotation</c>이 월드 기준이 되어 기계의 자세가 버려진 엉뚱한 각도로 스냅한다.
    /// 증상은 "잡으면 특정 각도로 튄 채 안 움직인다"로 나타난다.
    /// </summary>
    /// <remarks>
    /// 각도는 <b>잡은 순간을 기준으로 한 손의 회전량</b>으로 구한다. 절대 위치로 구하면
    /// 모델의 손잡이가 이미 기울어 있는 만큼(이 모델은 약 29°) 잡자마자 그쪽으로 튀고,
    /// 손 이동량에 배율을 곱해 누적하면 인터랙터가 몇 cm만 튀어도 수십 도가 들어간다.
    /// 잡은 순간부터의 <b>차이</b>만 쓰면 어느 자세로 어디를 잡든 손을 그대로 따라온다.
    ///
    /// 보고는 <b>당긴 본인의 피어에서</b> 나간다. 호스트가 각도 변화를 보고 대신 보고하면
    /// 그 시점이 이미 왕복 지연만큼 늦어, 핑이 높은 쪽이 판정에서 손해를 본다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Electricity Lever")]
    public class ElectricityLever : NetworkBehaviour
    {
        [Header("역할")]
        [Tooltip("이 레버를 당길 수 있는 역할. 다른 역할이 잡아도 움직이지 않는다.")]
        public Role requiredRole = Role.mental;

        [Tooltip("역할이 아직 정해지지 않았을 때도 당기게 할지. 단독 테스트용.")]
        public bool allowWhenRoleUnknown = true;

        [Header("연결")]
        [Tooltip("보고를 받을 미션. 비우면 부모에서 찾는다.")]
        public ElectricityCoreMission mission;

        [Header("회전")]
        [Tooltip("당기지 않았을 때의 각도(도, 로컬 X).")]
        public float restAngle;

        [Tooltip("끝까지 당겼을 때의 각도(도, 로컬 X). 당기는 방향이 반대면 부호를 뒤집는다.")]
        public float pulledAngle = -60f;

        [Tooltip("레버가 손을 따라가는 최대 속도(도/초). 손이 튀어도 확 돌지 않게 막는다.")]
        [Min(30f)] public float maxDegreesPerSecond = 360f;

        [Tooltip("손이 경첩에 이보다 가까우면 각도를 계산하지 않는다(방향이 불안정해진다).")]
        [Min(0.01f)] public float minHandDistance = 0.05f;

        [Tooltip("한 프레임에 손이 이 각도(도)보다 많이 뛰면 손 떨림·스냅으로 보고 버린다. " +
                 "VR 기기는 잡는 순간 물리 손이 한 번 튀므로 이게 없으면 레버가 끝까지 꺾인 채 굳는다.")]
        [Range(10f, 180f)] public float maxJumpDegrees = 60f;

        [Tooltip("이 비율(0~1)을 넘으면 '당겼다'고 보고한다.")]
        [Range(0.5f, 1f)] public float reportThreshold = 0.9f;

        [Tooltip("보고한 뒤 이 비율(0~1) 아래로 되밀면 다시 당길 수 있게 된다. " +
                 "놓아도 각도가 유지되므로 이게 없으면 당긴 레버를 다시 잡을 때마다 또 보고한다. " +
                 "reportThreshold보다 낮아야 하며, 높게 두면 자동으로 깎인다.")]
        [Range(0f, 0.9f)] public float rearmThreshold = 0.5f;

        [Header("디버그")]
        [Tooltip("켜면 아래 슬라이더로 레버를 직접 돌려 본다. VR 없이 축·방향을 확인할 때 쓴다.")]
        public bool debugDrive;

        [Range(-180f, 180f)] public float debugAngle;

        [Tooltip("잡기·각도 계산이 실제로 도는지 콘솔에 찍는다.")]
        public bool logToConsole;

        /// <summary>현재 각도. 상대 화면에도 같은 값이 보이도록 복제한다.</summary>
        [Networked] public float Angle { get; set; }

        /// <summary>0(쉼) ~ 1(끝까지). 연출이 읽는다.</summary>
        public float Pull01 => Mathf.InverseLerp(restAngle, pulledAngle, CurrentAngle);

        // 세션이 없거나 씬 NetworkObject가 베이크되지 않아 스폰되지 않을 때도 돌아가도록
        // 로컬 값을 따로 들고 간다. Object.IsValid 로는 '스폰 전'을 거르지 못해 예외가 난다.
        public bool IsSpawned { get; private set; }
        float CurrentAngle => IsSpawned ? Angle : localAngle;

        float localAngle;

        XRBaseInteractable grab;
        Transform hand;
        float prevHandAngle;    // 직전 프레임의 손 각도. 매 프레임 갱신한다.
        bool hasPrevHandAngle;
        bool reported;
        bool lastRejected;      // 마지막 당김을 미션이 버렸는가(박자 시작 전 등). 재무장 문턱이 달라진다.

        // 버려진 당김 뒤에는 이만큼만 되올리면 다시 당길 수 있다(report 문턱 기준).
        // 너무 좁으면 바닥에서 손이 떨리기만 해도 당김이 찍힌다.
        const float RejectedRearmMargin = 0.15f;

        // 경첩이 사는 좌표계와 그 안에서의 경첩 위치. **Awake에서 한 번 캐시한다.**
        // 살아 있는 transform.parent를 그때그때 읽으면 안 된다 — XRI가 잡는 순간
        // 부모를 떼어 버리면(아래 remarks) 각도 계산과 회전 적용이 동시에 무너진다.
        Transform hingeSpace;
        Vector3 hingePos;

        // 잡기 배선은 Awake에서 한다. Spawned에 두면 세션이 없을 때 아예 동작하지 않는다.
        void Awake()
        {
            if (mission == null) mission = GetComponentInParent<ElectricityCoreMission>();

            localAngle = restAngle;

            hingeSpace = transform.parent;
            hingePos = transform.localPosition;

            grab = GetComponent<XRBaseInteractable>();

            if (logToConsole)
                Debug.Log($"[Lever] {name} Awake — interactable={(grab == null ? "없음" : grab.GetType().Name)}, " +
                          $"parent={(transform.parent == null ? "없음" : transform.parent.name)}, " +
                          $"mission={(mission == null ? "없음" : "ok")}", this);

            if (grab == null) return;

            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnRelease);
        }

        public override void Spawned()
        {
            IsSpawned = true;

            if (HasStateAuthority) Angle = localAngle;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            base.Despawned(runner, hasState);

            IsSpawned = false;

            if (grab == null) return;

            grab.selectEntered.RemoveListener(OnGrab);
            grab.selectExited.RemoveListener(OnRelease);
        }

        /// <summary>이 피어의 플레이어가 이 레버를 쓸 수 있는가.</summary>
        public bool RoleMatches()
        {
            if (!RoleManager.HasLocalRole) return allowWhenRoleUnknown;

            return RoleManager.IsLocalRole(requiredRole);
        }

        void OnGrab(SelectEnterEventArgs args)
        {
            bool roleOk = RoleMatches();
            Transform h = args.interactorObject.transform;

            if (logToConsole)
                Debug.Log($"[Lever] {name} 잡힘 — 역할통과={roleOk}, 손={h?.name}", this);

            if (!roleOk) return;

            hand = h;

            // 기준 각도는 여기서 잡지 않는다. VR에서는 이 직후 물리 손이 한 번 튀어서
            // 여기서 찍은 값이 곧바로 낡아 버린다. 첫 Update에서 잡는다.
            hasPrevHandAngle = false;

            // 한 명만 잡아도 그 사람의 박자는 시작된다.
            if (mission != null) mission.ReportHold(requiredRole, true);
        }

        void OnRelease(SelectExitEventArgs args)
        {
            bool wasHeld = hand != null;
            hand = null;
            hasPrevHandAngle = false;

            // 각도는 **그대로 둔다.** 실제 레버는 놓은 자리에 남고, 쉼 위치로 되돌리면
            // 당긴 결과가 눈에서 사라져 둘이 서로의 상태를 말로 확인할 수조차 없다.
            // 다시 당길 준비(reported 해제)는 각도가 내려올 때 Update가 한다.

            if (wasHeld && mission != null) mission.ReportHold(requiredRole, false);
        }

        void Update()
        {
            // 디버그일 때는 rest~pulled 범위에 가두지 않는다. 축과 방향을 넓게 훑어봐야 하기 때문.
            if (debugDrive)
            {
                SetAngle(debugAngle, clamp: false);
                return;
            }

            if (hand == null) return;

            if (!TryGetHandAngle(hand, out float now)) return;

            // 첫 프레임은 기준만 잡고 넘어간다.
            if (!hasPrevHandAngle)
            {
                prevHandAngle = now;
                hasPrevHandAngle = true;
                return;
            }

            // 기준을 고정하지 않고 **한 프레임치 변화**만 더한다. 중간에 손이 한 번 튀어도
            // 그 프레임만 버리면 되고, 이후 추적이 그대로 이어진다.
            float delta = Mathf.DeltaAngle(prevHandAngle, now);
            prevHandAngle = now;

            if (Mathf.Abs(delta) > maxJumpDegrees)
            {
                if (logToConsole) Debug.Log($"[Lever] {name} 손 튐 무시 — {delta:F0}°", this);
                return;
            }

            SetAngle(Mathf.MoveTowards(CurrentAngle, CurrentAngle + delta,
                                       maxDegreesPerSecond * Time.deltaTime));

            // 놓아도 각도가 유지되므로 보고는 **문턱을 넘는 순간**에만 나가야 한다.
            // 당긴 채로 놓고 다시 잡으면 이미 문턱 위이니, 되밀어 내려오기 전까지는
            // 다시 보고하지 않는다. rearm을 report보다 낮게 강제해 양쪽으로 막힌다 —
            // 같으면 문턱에서 떨 때마다 보고가 쏟아지고, 높으면 영원히 재무장되지 않는다.
            //
            // 단, 미션이 버린 당김(박자 시작 전 뜸 등)은 소모로 치지 않는다 — 문턱 조금 아래까지만
            // 살짝 올렸다 내리면 다시 보고된다. 버려진 당김에도 절반까지 되밀기를 요구하면
            // 일찍 한 번 당긴 레버가 바닥에 굳어, 응집 때 당겨도 아무 일도 일어나지 않는다.
            // (문턱 위에 머물러 있다고 자동으로 다시 보내지는 않는다 — 박자가 시작되는 순간
            //  저절로 당김이 찍혀 상대의 당김과 엉뚱하게 짝지어지기 때문이다.)
            float pull = Pull01;
            float rearm = lastRejected
                ? reportThreshold - RejectedRearmMargin
                : Mathf.Min(rearmThreshold, reportThreshold - 0.05f);

            if (reported)
            {
                if (pull <= rearm) reported = false;
                return;
            }

            if (pull < reportThreshold) return;

            reported = true;
            lastRejected = mission == null || !mission.ReportPull(requiredRole);

            if (logToConsole && lastRejected)
                Debug.Log($"[Lever] {name} 당김 버려짐(박자 시작 전·클리어 뒤) — 살짝 올렸다 다시 당기면 된다", this);
        }

        /// <summary>
        /// 경첩에서 본 손의 각도. 경첩 축(로컬 X)에 수직인 평면에 투영하므로
        /// <b>결과는 항상 X 회전뿐</b>이고, 레버를 옆으로 밀어도 돌지 않는다.
        /// </summary>
        bool TryGetHandAngle(Transform h, out float degrees)
        {
            degrees = 0f;

            if (hingeSpace == null || h == null) return false;

            Vector3 v = hingeSpace.InverseTransformPoint(h.position) - hingePos;

            // X 성분은 경첩 축 방향이라 버린다.
            Vector2 inPlane = new Vector2(v.y, v.z);
            if (inPlane.sqrMagnitude < minHandDistance * minHandDistance) return false;

            // 각도 0 = 로컬 +Y. X축 회전 θ는 +Y를 (0, cosθ, sinθ)로 보내므로 atan2(z, y)가 곧 θ다.
            degrees = Mathf.Atan2(inPlane.y, inPlane.x) * Mathf.Rad2Deg;
            return true;
        }

        void SetAngle(float value, bool clamp = true)
        {
            float clamped = clamp
                ? Mathf.Clamp(value, Mathf.Min(restAngle, pulledAngle), Mathf.Max(restAngle, pulledAngle))
                : value;

            localAngle = clamped;

            if (!IsSpawned) return;

            if (HasStateAuthority) Angle = clamped;
            else RPC_SetAngle(clamped);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetAngle(float value) => Angle = value;

        // Render()가 아니라 LateUpdate다 — Render는 스폰된 뒤에만 돌아서,
        // 세션 없이 에디터에서 돌려 보면 레버가 아예 움직이지 않는다.
        void LateUpdate()
        {
            Quaternion hinge = Quaternion.Euler(CurrentAngle, 0f, 0f);

            // 부모가 그대로면 로컬로 넣는다. 누군가 부모를 떼어 간 상태라면
            // localRotation은 월드 기준이 되어 기계의 자세가 통째로 버려지므로,
            // 캐시해 둔 경첩 좌표계를 기준으로 월드 회전을 직접 맞춘다.
            if (hingeSpace == null || transform.parent == hingeSpace)
                transform.localRotation = hinge;
            else
                transform.rotation = hingeSpace.rotation * hinge;
        }
    }
}
