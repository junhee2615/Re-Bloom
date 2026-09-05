using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ReBloom.Water
{
    /// <summary>
    /// 플레이어가 올라서면 점유 상태가 되는 발판.
    /// Stool 위에 트리거 콜라이더로 얹어서 쓴다.
    /// </summary>
    [AddComponentMenu("ReBloom/Stand Zone")]
    [RequireComponent(typeof(Collider))]
    public class StandZone : MonoBehaviour
    {
        [System.Serializable] public class BoolEvent : UnityEvent<bool> { }

        [Tooltip("이 레이어에 있는 콜라이더만 점유로 센다")]
        public LayerMask occupantLayers = ~0;

        [Tooltip("비워두면 태그를 따지지 않는다. 예: Player")]
        public string requiredTag = "";

        [Tooltip("에디터 단독 테스트용. 켜면 사람이 없어도 점유로 친다")]
        public bool debugForceOccupied;

        [Tooltip("점유 상태가 바뀔 때")]
        public BoolEvent onOccupiedChanged;

        readonly HashSet<Collider> occupants = new HashSet<Collider>();
        bool lastState;

        public bool IsOccupied
        {
            get { return debugForceOccupied || occupants.Count > 0; }
        }

        public int OccupantCount { get { return occupants.Count; } }

        void Reset()
        {
            Collider c = GetComponent<Collider>();
            if (c != null) c.isTrigger = true;
        }

        void OnEnable()
        {
            occupants.Clear();
            lastState = IsOccupied;
        }

        bool Accepts(Collider other)
        {
            if (other == null) return false;
            if ((occupantLayers.value & (1 << other.gameObject.layer)) == 0) return false;
            if (!string.IsNullOrEmpty(requiredTag) && !other.CompareTag(requiredTag)) return false;
            return true;
        }

        void OnTriggerEnter(Collider other)
        {
            if (!Accepts(other)) return;
            occupants.Add(other);
            Notify();
        }

        void OnTriggerExit(Collider other)
        {
            if (occupants.Remove(other)) Notify();
        }

        void Update()
        {
            // 파괴되거나 비활성화된 콜라이더 정리
            if (occupants.Count > 0)
            {
                occupants.RemoveWhere(IsGone);
                Notify();
            }
            else if (IsOccupied != lastState) Notify();
        }

        static bool IsGone(Collider c)
        {
            return c == null || !c.enabled || !c.gameObject.activeInHierarchy;
        }

        void Notify()
        {
            bool now = IsOccupied;
            if (now == lastState) return;
            lastState = now;
            if (onOccupiedChanged != null) onOccupiedChanged.Invoke(now);
        }

        void OnDrawGizmos()
        {
            Collider c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = Application.isPlaying && IsOccupied
                ? new Color(0.3f, 1f, 0.5f, 0.35f)
                : new Color(1f, 0.85f, 0.2f, 0.20f);
            Bounds b = c.bounds;
            Gizmos.DrawCube(b.center, b.size);
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawWireCube(b.center, b.size);
        }
    }
}
