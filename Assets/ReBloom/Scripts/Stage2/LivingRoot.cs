using System.Collections;
using UnityEngine;
using UnityEngine.XR;

public class LivingRoot : MonoBehaviour
{
    [Header("Manager")]
    public RootMissionManager missionManager;

    [Header("Haptic")]
    [SerializeField] private float amplitude = 0.25f;   // 진동 세기
    [SerializeField] private float duration = 0.15f;    // 한 번 진동 길이
    [SerializeField] private float interval = 0.7f;     // 진동 주기 (느리게)

    [Header("힌트 표시 (mental 전용)")]
    [Tooltip("바닥의 초록 동심원 이펙트(FoundCircle). 뿌리 찾기 미션(MISSION 3)이 시작되면 mental 화면에만 켜지고, 이 뿌리를 찾으면 꺼진다.")]
    [SerializeField] private GameObject foundEffect;

    [Header("클리어 표시 (뿌리 위)")]
    [Tooltip("뿌리를 찾았을 때 그루터기 위에 띄울 아이콘")]
    [SerializeField] private Sprite foundMarkerSprite;
    [Tooltip("아이콘 크기(m)")]
    [SerializeField] private float foundMarkerSize = 0.35f;
    [Tooltip("그루터기 맨 윗면에서 아이콘 중심까지 높이(m)")]
    [SerializeField] private float foundMarkerHeight = 0.4f;
    [Tooltip("ear 가 뿌리에 손을 대고(발견) 클리어 표시가 뜨기까지 기다리는 시간(초)")]
    [SerializeField] private float foundMarkerDelay = 2f;

    private RootFoundMarker foundMarker;
    private Coroutine markerRoutine;

    public bool IsFound { get; private set; }

    private float timer;
    // 발견을 이미 매니저에 보고했는가 (네트워크 응답이 오기 전 중복 보고 방지)
    private bool reported;

    private void Awake()
    {
        if (foundEffect != null)
            foundEffect.SetActive(false);

        if (missionManager == null)
            missionManager = FindFirstObjectByType<RootMissionManager>();

        // 미션이 끝나 이 컴포넌트가 꺼져도 이벤트는 받아야 하므로 Awake/OnDestroy 에서 구독한다
        TutorialMissionManager_2.TutorialChanged += OnTutorialChanged;
    }

    private void OnDestroy()
    {
        TutorialMissionManager_2.TutorialChanged -= OnTutorialChanged;
    }

    // 뿌리 찾기 미션(MISSION 3) 차례가 되면 아직 못 찾은 뿌리의 FoundCircle 을 mental 화면에만 켠다
    private void OnTutorialChanged(TutorialStep_2 step)
    {
        if (step == TutorialStep_2.PlantComplete)
            SetHintCircle(!IsFound && LocalSeesHint());
        else
            SetHintCircle(false);
    }

    // 역할이 아직 없으면(혼자 테스트) 보여 준다
    private static bool LocalSeesHint()
    {
        return !RoleManager.HasLocalRole || RoleManager.LocalIsMental;
    }

private void OnTriggerStay(Collider other)
    {
        // 미션이 끝나 비활성화되면 진동/발견을 멈춘다.
        if (!enabled)
            return;

        // 오른손 Collider만 반응하도록 태그를 사용하는 것을 추천
        if (!other.CompareTag("Right Controller"))
            return;

        // ear 역할만 진동을 느낀다.
        if (!RoleManager.LocalIsEar)
            return;

        timer -= Time.deltaTime;

        if (timer <= 0f)
        {
            TriggerRightHandHaptic(amplitude, duration);
            timer = interval;
        }

        // 처음 접촉했을 때만 발견 보고. ear 가 손을 대고 진동을 느낀 경우에만 여기까지 온다.
        // 실제 발견 처리(이펙트 포함)는 매니저가 두 플레이어 모두에게 적용한다.
        if (!IsFound && !reported)
        {
            reported = true;
            missionManager?.OnRootFound(this);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Right Controller"))
        {
            timer = 0f;
        }
    }

    /// <summary>발견 처리. RootMissionManager 가 모든 플레이어에서 호출한다.</summary>
    public void MarkFound()
    {
        if (IsFound)
            return;

        IsFound = true;
        reported = true;

        // 찾은 뿌리의 힌트(FoundCircle)는 바로 끄고, 클리어 표시는 잠시 뒤에 띄운다
        SetHintCircle(false);
        if (markerRoutine != null) StopCoroutine(markerRoutine);
        markerRoutine = StartCoroutine(ShowMarkerAfterDelay());
    }

    /// <summary>찾기 미션이 끝나면 발견 표시를 끈다.</summary>
    public void HideFoundEffect()
    {
        if (markerRoutine != null)
        {
            StopCoroutine(markerRoutine);
            markerRoutine = null;
        }
        SetFoundMarker(false);
        SetHintCircle(false);
    }

    private IEnumerator ShowMarkerAfterDelay()
    {
        if (foundMarkerDelay > 0f)
            yield return new WaitForSeconds(foundMarkerDelay);
        markerRoutine = null;
        SetFoundMarker(true);
    }

    private void SetHintCircle(bool visible)
    {
        if (foundEffect == null)
            return;

        if (foundEffect.activeSelf == visible)
            return;

        foundEffect.SetActive(visible);

        if (visible)
        {
            ParticleSystem ps = foundEffect.GetComponent<ParticleSystem>();
            if (ps != null)
                ps.Play(true);
        }
    }

    // 그루터기 위에 클리어 표시를 띄우거나 숨긴다
    private void SetFoundMarker(bool visible)
    {
        if (foundMarkerSprite == null)
            return;

        if (visible)
        {
            if (foundMarker == null)
                foundMarker = CreateFoundMarker();
            foundMarker.Show();
        }
        else if (foundMarker != null)
        {
            foundMarker.Hide();
        }
    }

    private RootFoundMarker CreateFoundMarker()
    {
        GameObject go = new GameObject("FoundMarker");
        go.layer = gameObject.layer;
        go.transform.SetParent(transform, false);

        RootFoundMarker marker = go.AddComponent<RootFoundMarker>();
        Vector3 top = GetStumpTop();
        marker.Setup(foundMarkerSprite, foundMarkerSize, top + Vector3.up * foundMarkerHeight);
        go.SetActive(false);
        return marker;
    }

    // 그루터기 메시들의 맨 윗면 중앙 (UI, 파티클, 발견 이펙트는 제외)
    private Vector3 GetStumpTop()
    {
        bool has = false;
        Bounds bounds = new Bounds(transform.position, Vector3.zero);

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r is SpriteRenderer) continue;
            if (r.GetComponentInParent<Canvas>(true) != null) continue;
            if (foundEffect != null && r.transform.IsChildOf(foundEffect.transform)) continue;

            if (!has) { bounds = r.bounds; has = true; }
            else bounds.Encapsulate(r.bounds);
        }

        return has ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) : transform.position;
    }

    private void TriggerRightHandHaptic(float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (device.TryGetHapticCapabilities(out HapticCapabilities capabilities))
        {
            if (capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0, amplitude, duration);
            }
        }
    }
}
