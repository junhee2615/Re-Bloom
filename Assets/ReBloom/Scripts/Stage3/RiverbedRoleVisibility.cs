using UnityEngine;

namespace ReBloom.Water
{
    /// <summary>
    /// 역할에 따라 물결 패턴을 보여줄지 말지 정한다.
    ///
    ///  - mental : 강바닥에 물결 패턴이 보인다. 대신 진동은 없다.
    ///  - ear    : 패턴 레이어가 카메라에서 컬링되어, 원래 Terrain 강바닥만 보인다. 대신 진동을 느낀다.
    ///
    /// 물결 패턴 오브젝트(TEMP_Riverbed 등)를 Pattern Layer Name 레이어에 올려두면 된다.
    /// 카메라는 Fusion 스폰 이후에 생기므로 주기적으로 다시 확인한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Riverbed Role Visibility")]
    [DefaultExecutionOrder(100)]
    public class RiverbedRoleVisibility : MonoBehaviour
    {
        [Tooltip("물결 패턴 오브젝트가 올라가 있는 레이어 이름")]
        public string patternLayerName = "RiverbedPattern";

        [Tooltip("이 역할에게는 물결 패턴이 보이지 않는다")]
        public Role hideForRole = Role.ear;

        [Tooltip("none 이면 NetworkPlayer 에서 로컬 역할을 읽는다. 단독 테스트할 때만 강제 지정")]
        public Role roleOverride = Role.none;

        [Tooltip("비우면 Camera.main 을 쓴다. 리그 카메라를 직접 넣어도 된다")]
        public Camera targetCamera;

        [Tooltip("카메라가 늦게 생기므로 이 주기로 다시 확인한다 (초)")]
        [Range(0.1f, 2f)] public float checkInterval = 0.4f;

        [Tooltip("적용될 때마다 로그를 남긴다")]
        public bool logToConsole = true;

        int patternMaskBit;
        float nextCheck;
        Camera appliedCamera;
        bool appliedHide;
        bool hasApplied;

        void Awake()
        {
            int layer = LayerMask.NameToLayer(patternLayerName);
            if (layer < 0)
            {
                patternMaskBit = 0;
                Debug.LogWarning("[RiverbedRoleVisibility] 레이어를 찾을 수 없습니다: "
                    + patternLayerName + " — Project Settings > Tags and Layers 에서 만들어 주세요.", this);
            }
            else
            {
                patternMaskBit = 1 << layer;
            }
        }

        /// <summary>이 기기의 로컬 역할. 세션 밖이면 Role.none.</summary>
        public Role ResolveRole()
        {
            if (roleOverride != Role.none) return roleOverride;
            Role? r = NetworkPlayer.LocalRole;
            return r.HasValue ? r.Value : Role.none;
        }

        void Update()
        {
            if (patternMaskBit == 0) return;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + Mathf.Max(0.1f, checkInterval);

            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;

            bool hide = ResolveRole() == hideForRole;

            // 카메라가 바뀌었으면 이전 카메라는 원래대로 돌려놓는다
            if (appliedCamera != null && appliedCamera != cam)
            {
                appliedCamera.cullingMask |= patternMaskBit;
                hasApplied = false;
            }

            if (hasApplied && appliedCamera == cam && appliedHide == hide) return;

            if (hide) cam.cullingMask &= ~patternMaskBit;
            else cam.cullingMask |= patternMaskBit;

            appliedCamera = cam;
            appliedHide = hide;
            hasApplied = true;

            if (logToConsole)
                Debug.Log("[RiverbedRoleVisibility] camera=" + cam.name
                    + " role=" + ResolveRole() + " patternVisible=" + (!hide), this);
        }

        void OnDisable()
        {
            // 컬링을 걸어둔 채로 끝나지 않도록 되돌린다
            if (appliedCamera != null && patternMaskBit != 0)
                appliedCamera.cullingMask |= patternMaskBit;
            hasApplied = false;
        }
    }
}
