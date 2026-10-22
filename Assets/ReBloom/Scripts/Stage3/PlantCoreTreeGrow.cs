using System;
using System.Collections;
using ReBloom.Electricity;
using UnityEngine;

/// <summary>
/// 전기 장치 미션을 클리어하면 엔딩 코어의 나무에 잎이 서서히 자란다.
///
/// 새 네트워크 상태를 만들지 않는다 — 진실 공급원은 <see cref="ElectricityCoreMission.IsClear"/>
/// 하나뿐이고, 이 컴포넌트는 그 결과를 각 피어가 로컬에서 재생할 뿐이다.
/// <see cref="PlantCoreFlowerBloom"/>(새싹 → 꽃)과 같은 구조다.
///
/// 배치: EndingCore 루트. Leaves를 비워 두면 자식 중 <see cref="DefaultLeavesName"/>을 찾아 쓴다.
/// </summary>
public class PlantCoreTreeGrow : MonoBehaviour
{
    private const string DefaultLeavesName = "GenericTree_Leaves";

    /// <summary>잎이 다 자란 순간 한 번. 엔딩 컷씬이 구독한다.</summary>
    public static event Action Grown;

    [Header("연결")]
    [Tooltip("클리어를 지켜볼 미션. 비우면 씬에서 찾는다.")]
    [SerializeField] private ElectricityCoreMission mission;

    [Tooltip("자라날 잎. 비우면 자식 중 GenericTree_Leaves를 찾는다. 클리어 전까지는 꺼둔다.")]
    [SerializeField] private GameObject leaves;

    [Tooltip("잎이 자라기 시작할 때 재생할 소리.")]
    [SerializeField] private AudioSource growAudio;

    [Header("잎")]
    [Tooltip("클리어 후 잎이 돋기 시작할 때까지의 뜸(초). 꽃·물 연출 뒤에 따라오게 둔다.")]
    [SerializeField, Min(0f)] private float startDelay = 1f;

    [Tooltip("자라기 시작할 때의 배율. 0이면 아무것도 없는 데서 돋아난다.")]
    [SerializeField, Min(0f)] private float startScale;

    [Tooltip("다 자랐을 때의 배율. 배치된 크기를 1로 본다.")]
    [SerializeField, Min(0f)] private float targetScale = 1f;

    [Tooltip("잎이 다 자라는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float growDuration = 6f;

    [Tooltip("잎이 돋아나는 기준점. 잎 부모(GenericTree)의 로컬 좌표다. " +
             "메시 피벗이 밑동에 있어 땅에서 솟는 것처럼 보이지 않도록, 줄기 끝에서 퍼지게 한다.")]
    [SerializeField] private Vector3 growPivot = new Vector3(0f, 3.53f, 0f);

    [Header("자라는 모양")]
    [Tooltip("끝에서 목표 크기를 넘었다가 돌아오는 탄성. 0이면 튀지 않고, 1.7이면 약 10% 넘친다.")]
    [SerializeField, Min(0f)] private float overshoot = 1.7f;

    [Header("잎 파티클")]
    [Tooltip("잎이 돋을 때 기준점에서 한 번 터뜨릴 파티클 프리팹들(Leaf, Leaf2 등). " +
             "원래 반복 재생 설정이어도 여기서 한 번 터지는 버스트로 바꿔 쓴다.")]
    [SerializeField] private ParticleSystem[] leafBurstPrefabs;

    [Tooltip("프리팹 하나당 한 번에 뿌릴 잎 수.")]
    [SerializeField, Min(0)] private int burstCount = 10;

    [Tooltip("흩날리는 잎이 남아 있는 시간(초).")]
    [SerializeField, Min(0.1f)] private float burstLifetime = 4f;

    [Tooltip("잎이 퍼져 나가는 반경(월드 단위).")]
    [SerializeField, Min(0f)] private float burstRadius = 1.2f;

    // 잎의 배치 크기·위치. 여기에 배율을 곱한다.
    private Vector3 leavesBaseScale = Vector3.one;
    private Vector3 leavesBasePosition;

    private bool played;

    private void Awake()
    {
        if (mission == null)
            mission = FindFirstObjectByType<ElectricityCoreMission>();

        if (mission == null)
            Debug.LogWarning($"[PlantCoreTreeGrow] {name}: ElectricityCoreMission을 찾지 못했습니다. " +
                             $"잎이 자라지 않습니다.", this);

        if (leaves == null)
            leaves = FindChild(transform, DefaultLeavesName);

        if (leaves == null)
        {
            Debug.LogWarning($"[PlantCoreTreeGrow] {name}: Leaves가 비어 있고 자식에서 " +
                             $"{DefaultLeavesName}도 찾지 못했습니다.", this);
            return;
        }

        leavesBaseScale = leaves.transform.localScale;
        leavesBasePosition = leaves.transform.localPosition;

        // 클리어 전에는 잎이 없어야 한다. 프리팹에서 켜 둔 채로 저장돼도 여기서 맞춘다.
        if (!played)
            leaves.SetActive(false);
    }

    private void OnEnable() => ElectricityCoreMission.Cleared += OnCleared;

    private void OnDisable() => ElectricityCoreMission.Cleared -= OnCleared;

    private void Start()
    {
        // 이미 클리어된 뒤에 들어왔으면 연출 없이 결과 상태로 맞춘다.
        if (mission != null && mission.IsSpawned && mission.IsClear)
            ApplyFinalState();
    }

    private void OnCleared()
    {
        if (played) return;

        played = true;
        StartCoroutine(Grow());
    }

    private IEnumerator Grow()
    {
        if (startDelay > 0f)
            yield return new WaitForSeconds(startDelay);

        if (growAudio != null)
            growAudio.Play();

        if (leaves != null)
        {
            SetScale(startScale);
            leaves.SetActive(true);
        }

        SpawnLeafBursts();

        float elapsed = 0f;

        while (elapsed < growDuration)
        {
            elapsed += Time.deltaTime;

            float k = EaseOutBack(Mathf.Clamp01(elapsed / growDuration));
            SetScale(Mathf.LerpUnclamped(startScale, targetScale, k));

            yield return null;
        }

        ApplyFinalState();
        Grown?.Invoke();
    }

    // 도메인 리로드를 꺼도 static 이벤트가 이전 플레이의 구독자를 물고 있지 않도록.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvents() => Grown = null;

    // 끝에서 1을 살짝 넘었다가 돌아온다. overshoot가 0이면 부드럽게 멈추기만 한다.
    private float EaseOutBack(float t)
    {
        float c = overshoot;
        float x = t - 1f;
        return 1f + (c + 1f) * x * x * x + c * x * x;
    }

    // 원래 반복 재생용인 파티클을 한 번 터지고 끝나는 버스트로 바꿔 기준점에 뿌린다.
    private void SpawnLeafBursts()
    {
        if (leafBurstPrefabs == null || burstCount <= 0 || leaves == null) return;

        Transform parent = leaves.transform.parent;
        Vector3 origin = parent != null ? parent.TransformPoint(growPivot) : growPivot;

        foreach (ParticleSystem prefab in leafBurstPrefabs)
        {
            if (prefab == null) continue;

            ParticleSystem ps = Instantiate(prefab, origin, Quaternion.identity);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // 수명은 시뮬레이션 시간이라 재생 속도만큼 늘려야 실제로 burstLifetime초 남는다.
            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.prewarm = false;
            main.duration = 0.1f;
            main.startLifetime = burstLifetime * main.simulationSpeed;
            main.maxParticles = Mathf.Max(main.maxParticles, burstCount);

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)burstCount) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.radius = burstRadius;

            ps.Play();
            Destroy(ps.gameObject, burstLifetime + 1f);
        }
    }

    private void ApplyFinalState()
    {
        played = true;

        if (leaves == null) return;

        leaves.SetActive(true);
        SetScale(targetScale);
    }

    // ── 테스트용. 미션을 깨지 않고 연출만 확인한다. ────────────────────────────
    // 컴포넌트 헤더 우클릭 → 메뉴. Play 모드에서 쓴다(코루틴이 돌아야 한다).

    [ContextMenu("테스트: 잎 없는 상태로 (Play 모드)")]
    private void DebugResetToBare()
    {
        if (!RequirePlayMode()) return;

        StopAllCoroutines();
        played = false;

        if (leaves != null)
        {
            SetScale(startScale);
            leaves.SetActive(false);
        }
    }

    [ContextMenu("테스트: 잎 자라기 (Play 모드)")]
    private void DebugGrow()
    {
        if (!RequirePlayMode()) return;

        StopAllCoroutines();
        played = false;
        OnCleared();
    }

    private bool RequirePlayMode()
    {
        if (Application.isPlaying) return true;

        Debug.LogWarning($"[PlantCoreTreeGrow] {name}: 연출은 Play 모드에서만 재생됩니다.", this);
        return false;
    }

    // 피벗이 아니라 growPivot을 중심으로 키운다 — 그 점은 제자리에 있고 나머지가 그 점에서 퍼져 나간다.
    // 배율 1이면 배치된 위치·크기 그대로다.
    private void SetScale(float multiplier)
    {
        if (leaves == null) return;

        leaves.transform.localScale = leavesBaseScale * multiplier;
        leaves.transform.localPosition = growPivot + (leavesBasePosition - growPivot) * multiplier;
    }

    private void OnDrawGizmosSelected()
    {
        GameObject target = leaves != null ? leaves : FindChild(transform, DefaultLeavesName);
        if (target == null) return;

        Transform parent = target.transform.parent;
        Vector3 p = parent != null ? parent.TransformPoint(growPivot) : growPivot;

        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.9f);
        Gizmos.DrawWireSphere(p, 0.15f);
    }

    // 꺼진 자식까지 뒤진다. 잎은 클리어 전까지 꺼져 있기 때문이다.
    private static GameObject FindChild(Transform root, string childName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == childName)
                return t.gameObject;

        return null;
    }
}
