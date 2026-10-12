using System.Collections;
using ReBloom.Electricity;
using UnityEngine;

/// <summary>
/// 전기 장치 미션을 클리어하면 새싹이 꽃으로 자란다.
///
/// 새 네트워크 상태를 만들지 않는다 — 진실 공급원은 <see cref="ElectricityCoreMission.IsClear"/>
/// 하나뿐이고, 이 컴포넌트는 그 결과를 각 피어가 로컬에서 재생할 뿐이다.
/// <see cref="PlantCoreSeedButton"/>(씨앗 → 새싹)과 같은 구조다.
///
/// 배치: SeedToFlower(또는 그 상위) 어디든. Sprouts / Flowers만 연결하면 된다.
/// </summary>
public class PlantCoreFlowerBloom : MonoBehaviour
{
    [Header("연결")]
    [Tooltip("클리어를 지켜볼 미션. 비우면 씬에서 찾는다.")]
    [SerializeField] private ElectricityCoreMission mission;

    [Tooltip("꽃이 피면 물러날 새싹 묶음.")]
    [SerializeField] private GameObject sprouts;

    [Tooltip("자라날 꽃 묶음. 꺼진 상태로 두면 된다.")]
    [SerializeField] private GameObject flowers;

    [Tooltip("꽃이 피기 시작할 때 재생할 소리.")]
    [SerializeField] private AudioSource bloomAudio;

    [Header("꽃")]
    [Tooltip("자라기 시작할 때의 배율. 0이면 아무것도 없는 데서 돋아난다.")]
    [SerializeField, Min(0f)] private float startScale;

    [Tooltip("다 자랐을 때의 배율. 배치된 크기를 1로 본다.")]
    [SerializeField, Min(0f)] private float targetScale = 1f;

    [Tooltip("꽃이 다 자라는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float growDuration = 4f;

    [Header("새싹")]
    [Tooltip("꽃이 자라는 동안 새싹을 같이 줄여 없앤다. 끄면 새싹이 그대로 남는다.")]
    [SerializeField] private bool shrinkSprouts = true;

    [Tooltip("새싹이 사라지는 데 걸리는 시간(초). 꽃보다 짧게 두면 자리를 먼저 비켜준다.")]
    [SerializeField, Min(0f)] private float shrinkDuration = 1.5f;

    // 꽃 자식들의 배치 크기. 여기에 배율을 곱한다.
    private Vector3[] flowerBaseScales;
    private Vector3[] sproutBaseScales;

    private bool played;

    private void Awake()
    {
        if (mission == null)
            mission = FindFirstObjectByType<ElectricityCoreMission>();

        if (mission == null)
            Debug.LogWarning($"[PlantCoreFlowerBloom] {name}: ElectricityCoreMission을 찾지 못했습니다. " +
                             $"꽃이 피지 않습니다.", this);

        if (flowers == null)
            Debug.LogWarning($"[PlantCoreFlowerBloom] {name}: Flowers가 비어 있습니다.", this);

        flowerBaseScales = CaptureBaseScales(flowers);
        sproutBaseScales = CaptureBaseScales(sprouts);
    }

    private void OnEnable() => ElectricityCoreMission.Cleared += OnCleared;

    private void OnDisable() => ElectricityCoreMission.Cleared -= OnCleared;

    private void Start()
    {
        // 이미 클리어된 뒤에 들어왔으면 연출 없이 결과 상태로 맞춘다.
        // (늦게 들어온 피어는 미션의 Update가 Cleared를 쏴주지만, 그보다 먼저 와 있을 수도 있다)
        if (mission != null && mission.IsSpawned && mission.IsClear)
            ApplyFinalState();
    }

    private void OnCleared()
    {
        if (played) return;

        played = true;
        StartCoroutine(Bloom());
    }

    private IEnumerator Bloom()
    {
        if (bloomAudio != null)
            bloomAudio.Play();

        if (flowers != null)
        {
            flowers.SetActive(true);
            SetScales(flowers, flowerBaseScales, startScale);
        }

        float elapsed = 0f;
        float span = Mathf.Max(growDuration, shrinkSprouts ? shrinkDuration : 0f);

        while (elapsed < span)
        {
            elapsed += Time.deltaTime;

            if (flowers != null && growDuration > 0f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / growDuration));
                SetScales(flowers, flowerBaseScales, Mathf.Lerp(startScale, targetScale, k));
            }

            if (shrinkSprouts && sprouts != null && shrinkDuration > 0f)
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / shrinkDuration));
                SetScales(sprouts, sproutBaseScales, Mathf.Lerp(1f, 0f, k));
            }

            yield return null;
        }

        ApplyFinalState();
    }

    private void ApplyFinalState()
    {
        played = true;

        if (flowers != null)
        {
            flowers.SetActive(true);
            SetScales(flowers, flowerBaseScales, targetScale);
        }

        if (shrinkSprouts && sprouts != null)
        {
            SetScales(sprouts, sproutBaseScales, 1f);   // 다음에 켜질 때를 위해 크기는 되돌려 둔다
            sprouts.SetActive(false);
        }
    }

    // ── 테스트용. 미션을 깨지 않고 연출만 확인한다. ────────────────────────────
    // 컴포넌트 헤더 우클릭 → 메뉴. Play 모드에서 쓴다(코루틴이 돌아야 한다).
    // 순서: "새싹 상태로" → "꽃 피우기". 새싹이 꺼져 있으면 줄어드는 게 안 보인다.

    [ContextMenu("테스트: 새싹 상태로 (Play 모드)")]
    private void DebugResetToSprouts()
    {
        if (!RequirePlayMode()) return;

        StopAllCoroutines();
        played = false;

        if (flowers != null)
        {
            SetScales(flowers, flowerBaseScales, startScale);
            flowers.SetActive(false);
        }

        if (sprouts != null)
        {
            SetScales(sprouts, sproutBaseScales, 1f);
            sprouts.SetActive(true);
        }
    }

    [ContextMenu("테스트: 꽃 피우기 (Play 모드)")]
    private void DebugBloom()
    {
        if (!RequirePlayMode()) return;

        StopAllCoroutines();
        played = false;
        OnCleared();
    }

    private bool RequirePlayMode()
    {
        if (Application.isPlaying) return true;

        Debug.LogWarning($"[PlantCoreFlowerBloom] {name}: 연출은 Play 모드에서만 재생됩니다.", this);
        return false;
    }

    // 배치된 크기를 기억해 둔다. 자식마다 크기가 달라도(연꽃 0.5 등) 비율이 유지된다.
    private static Vector3[] CaptureBaseScales(GameObject root)
    {
        if (root == null) return null;

        Transform t = root.transform;
        Vector3[] scales = new Vector3[t.childCount];
        for (int i = 0; i < scales.Length; i++)
            scales[i] = t.GetChild(i).localScale;

        return scales;
    }

    private static void SetScales(GameObject root, Vector3[] baseScales, float multiplier)
    {
        if (root == null || baseScales == null) return;

        Transform t = root.transform;
        int count = Mathf.Min(t.childCount, baseScales.Length);
        for (int i = 0; i < count; i++)
            t.GetChild(i).localScale = baseScales[i] * multiplier;
    }
}
