using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace ReBloom.Water
{
    /// <summary>
    /// 미션을 클리어하면 수면이 차오르는 연출.
    ///
    /// 대상(HugeWater 등)을 활성화한 뒤 Y 를 startY -> endY 로 올린다.
    /// 이 컴포넌트는 대상이 아니라 **항상 켜져 있는 오브젝트**에 붙인다.
    /// 대상이 비활성 상태여도 코루틴이 돌아야 하기 때문이다.
    /// </summary>
    [AddComponentMenu("ReBloom/Rising Water")]
    public class RisingWater : MonoBehaviour
    {
        [Header("대상")]
        [Tooltip("차오를 오브젝트. 비우면 자기 자신을 쓴다")]
        public Transform target;

        [Tooltip("시작할 때 대상을 꺼둔다. 클리어 시점에 켜진다")]
        public bool hideUntilPlayed = true;

        [Tooltip("함께 켤 오브젝트들 (폭포 파티클, WaterSurface 등)")]
        public GameObject[] alsoActivate;

        [Header("높이")]
        [Tooltip("로컬 Y 기준. 대상의 현재 Y 가 보통 시작값이다")]
        public float startY = -1.26f;
        public float endY = 0f;

        [Tooltip("끄면 월드 Y 로 움직인다")]
        public bool useLocalPosition = true;

        [Header("타이밍")]
        [Tooltip("차오르는 데 걸리는 시간 (초)")]
        public float duration = 6f;

        [Tooltip("켜지고 나서 실제로 오르기 시작할 때까지의 뜸 (초)")]
        public float startDelay = 0.4f;

        [Tooltip("진행 곡선. 처음엔 빠르게 밀려들고 끝에서 잔잔해진다")]
        public AnimationCurve ease = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 1.8f),
            new Keyframe(0.35f, 0.62f, 1.4f, 1.4f),
            new Keyframe(1f, 1f, 0.08f, 0f));

        [Header("이벤트")]
        public UnityEvent onRiseStarted;
        public UnityEvent onRiseCompleted;

        [Header("디버그")]
        public bool logToConsole = true;

        [Tooltip("이미 재생했어도 다시 호출하면 처음부터 재생한다")]
        public bool allowReplay = false;

        Coroutine running;
        bool hasPlayed;

        public bool IsPlaying { get { return running != null; } }
        public bool HasPlayed { get { return hasPlayed; } }

        Transform Target { get { return target != null ? target : transform; } }

        void Reset()
        {
            target = transform;
            startY = transform.localPosition.y;
        }

        void Awake()
        {
            Transform t = Target;
            if (t == null) return;

            SetY(startY);

            if (hideUntilPlayed && t.gameObject != gameObject)
                t.gameObject.SetActive(false);
        }

        /// <summary>미션 클리어에서 호출. onMissionComplete 에 연결한다.</summary>
        public void Play()
        {
            if (hasPlayed && !allowReplay) return;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(Rise());
        }

        /// <summary>다시 처음 상태로 (테스트용).</summary>
        public void ResetWater()
        {
            if (running != null) { StopCoroutine(running); running = null; }
            hasPlayed = false;
            SetY(startY);
            Transform t = Target;
            if (hideUntilPlayed && t != null && t.gameObject != gameObject)
                t.gameObject.SetActive(false);
        }

        IEnumerator Rise()
        {
            hasPlayed = true;
            Transform t = Target;

            SetY(startY);
            if (t != null && !t.gameObject.activeSelf) t.gameObject.SetActive(true);

            if (alsoActivate != null)
                for (int i = 0; i < alsoActivate.Length; i++)
                    if (alsoActivate[i] != null) alsoActivate[i].SetActive(true);

            if (logToConsole)
                Debug.Log("[RisingWater] " + (t != null ? t.name : "?") + " 활성화 — "
                    + startY.ToString("F2") + " -> " + endY.ToString("F2") + " (" + duration + "s)", this);

            if (onRiseStarted != null) onRiseStarted.Invoke();

            if (startDelay > 0f) yield return new WaitForSeconds(startDelay);

            float elapsed = 0f;
            float d = Mathf.Max(0.01f, duration);
            while (elapsed < d)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / d);
                float k = (ease != null && ease.length > 0) ? ease.Evaluate(u) : u;
                SetY(Mathf.LerpUnclamped(startY, endY, k));
                yield return null;
            }

            SetY(endY);
            running = null;

            if (logToConsole) Debug.Log("[RisingWater] 차오름 완료", this);
            if (onRiseCompleted != null) onRiseCompleted.Invoke();
        }

        void SetY(float y)
        {
            Transform t = Target;
            if (t == null) return;

            if (useLocalPosition)
            {
                Vector3 p = t.localPosition;
                p.y = y;
                t.localPosition = p;
            }
            else
            {
                Vector3 p = t.position;
                p.y = y;
                t.position = p;
            }
        }

        void OnDrawGizmosSelected()
        {
            Transform t = Target;
            if (t == null) return;

            Vector3 basePos = useLocalPosition && t.parent != null
                ? t.parent.TransformPoint(new Vector3(t.localPosition.x, startY, t.localPosition.z))
                : new Vector3(t.position.x, startY, t.position.z);
            Vector3 topPos = useLocalPosition && t.parent != null
                ? t.parent.TransformPoint(new Vector3(t.localPosition.x, endY, t.localPosition.z))
                : new Vector3(t.position.x, endY, t.position.z);

            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
            Gizmos.DrawLine(basePos, topPos);
            Gizmos.DrawWireCube(basePos, new Vector3(6f, 0.02f, 6f));
            Gizmos.DrawWireCube(topPos, new Vector3(6f, 0.02f, 6f));
        }
    }
}
