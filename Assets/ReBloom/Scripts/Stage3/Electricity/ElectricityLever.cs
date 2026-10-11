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
    /// <see cref="requiredRole"/>을 맞춘다. Collider와 <see cref="XRGrabInteractable"/>이 함께 필요하다.
    /// 코어의 <see cref="NetworkObject"/> 아래 자식이면 따로 NetworkObject를 둘 필요가 없다.
    /// </summary>
    /// <remarks>
    /// 보고는 <b>당긴 본인의 피어에서</b> 나간다. 호스트가 각도 변화를 보고 대신 보고하면
    /// 그 시점이 이미 왕복 지연만큼 늦어, 핑이 높은 쪽이 판정에서 손해를 본다.
    /// 각도 자체는 상대도 보여야 하므로 Stage1 <c>LeverSwitch</c>와 같은 방식으로 복제한다.
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

        [Tooltip("끝까지 당겼을 때의 각도(도, 로컬 X).")]
        public float pulledAngle = -60f;

        [Tooltip("손이 1 m 움직일 때 레버가 도는 각도. 조작 감도.")]
        public float rotateSpeed = 200f;

        [Tooltip("이 비율(0~1)을 넘으면 '당겼다'고 보고한다.")]
        [Range(0.5f, 1f)] public float reportThreshold = 0.9f;

        /// <summary>현재 각도. 상대 화면에도 같은 값이 보이도록 복제한다.</summary>
        [Networked] public float Angle { get; set; }

        /// <summary>0(쉼) ~ 1(끝까지). 연출이 읽는다.</summary>
        public float Pull01 => Mathf.InverseLerp(restAngle, pulledAngle, Angle);

        XRGrabInteractable grab;
        Transform hand;
        float lastHandY;
        bool reported;      // 이번에 당긴 것을 이미 보고했는가

        public override void Spawned()
        {
            if (mission == null) mission = GetComponentInParent<ElectricityCoreMission>();

            grab = GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.selectEntered.AddListener(OnGrab);
                grab.selectExited.AddListener(OnRelease);
            }

            if (HasStateAuthority) Angle = restAngle;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            base.Despawned(runner, hasState);

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
            if (!RoleMatches()) return;

            hand = args.interactorObject.transform;
            lastHandY = hand.position.y;

            // 둘 다 잡아야 박자가 시작된다 — 미션의 시작 조건이다.
            if (mission != null) mission.ReportHold(requiredRole, true);
        }

        void OnRelease(SelectExitEventArgs args)
        {
            bool wasHeld = hand != null;
            hand = null;

            // 손을 놓으면 다시 쉼 위치로. 다음 박에 또 당길 수 있어야 한다.
            reported = false;
            SetAngle(restAngle);

            if (wasHeld && mission != null) mission.ReportHold(requiredRole, false);
        }

        void Update()
        {
            if (hand == null) return;

            float dy = hand.position.y - lastHandY;
            lastHandY = hand.position.y;

            // 순간이동급 변화는 리그 이동·손 튐이므로 무시한다(Stage1 LeverSwitch와 같은 방어).
            if (Mathf.Abs(dy) > 0.2f) return;

            SetAngle(Angle + dy * rotateSpeed);

            if (reported || Pull01 < reportThreshold) return;

            reported = true;
            if (mission != null) mission.ReportPull(requiredRole);
        }

        void SetAngle(float value)
        {
            float clamped = Mathf.Clamp(value, Mathf.Min(restAngle, pulledAngle), Mathf.Max(restAngle, pulledAngle));

            if (HasStateAuthority) Angle = clamped;
            else RPC_SetAngle(clamped);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_SetAngle(float value) => Angle = value;

        // XRI가 손을 따라 오브젝트를 옮기려 하므로, 매 프레임 각도를 덮어써 경첩처럼 고정한다.
        public override void Render() => transform.localRotation = Quaternion.Euler(Angle, 0f, 0f);
    }
}
