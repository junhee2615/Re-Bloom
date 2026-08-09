using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 수생식물 미션 완료 연출
public class PlantClearSequence : MonoBehaviour
{
    [Header("트리거: 이 미션들이 모두 클리어되면 실행")]
    [SerializeField] private List<PetalRhythmMission> plants = new List<PetalRhythmMission>();

    [Header("물 차오르기")]
    [SerializeField] private Transform cleanWater;
    [SerializeField] private float waterStartY = -0.81f;
    [SerializeField] private float waterEndY = 0.30696f;
    [SerializeField] private float waterRiseDuration = 5f;
    [SerializeField] private bool activateCleanWater = true;

    [Header("활성화할 오브젝트 (FloatingPlant, Fish 등)")]
    [SerializeField] private List<GameObject> objectsToActivate = new List<GameObject>();

    [Header("물 정화: WaterPurify 들의 부모 (DirtyToCleanWater)")]
    [SerializeField] private Transform dirtyToCleanWaterRoot;

    [Header("식생 복원: PlantRevive 들의 부모 (AquaticPlant/Static)")]
    [SerializeField] private Transform aquaticStaticRoot;

    [Header("스카이박스 머티리얼 (Sunset)")]
    [SerializeField] private Material sunsetSkybox;
    [Tooltip("스카이박스 전환 페이드 시간(초)")]
    [SerializeField] private float skyboxFadeDuration = 3f;


    [Header("안개 색")]
    [SerializeField] private Color fogColor = new Color32(0x4B, 0x44, 0x35, 0xFF);

    private bool played;

    private void OnEnable()
    {
        PetalRhythmMission.Revived += CheckCompletion;
    }

    private void OnDisable()
    {
        PetalRhythmMission.Revived -= CheckCompletion;
    }

    private void Start()
    {
        // 시작 시 이미 모두 클리어돼 있는 경우 대비
        CheckCompletion();
    }

    private void CheckCompletion()
    {
        if (played)
            return;

        if (plants == null || plants.Count == 0)
            return;

        foreach (var p in plants)
            if (p == null || !p.IsCleared)
                return;

        PlaySequence();
    }

    [ContextMenu("Play Sequence")]
    public void PlaySequence()
    {
        if (played)
            return;
        played = true;

        // 활성화 — StartRevive 전에 활성화해야 하위 코루틴이 돈다
        foreach (var go in objectsToActivate)
            if (go != null)
                go.SetActive(true);

        // 물 정화
        if (dirtyToCleanWaterRoot != null)
            foreach (var wp in dirtyToCleanWaterRoot.GetComponentsInChildren<WaterPurify>(true))
                wp.StartPurify();

        // 식생 복원
        if (aquaticStaticRoot != null)
            foreach (var pr in aquaticStaticRoot.GetComponentsInChildren<PlantRevive>(true))
                pr.StartRevive();

        // 스카이박스 (3초 페이드 전환)
        if (sunsetSkybox != null)
            StartCoroutine(FadeSkybox());

        // 안개 색
        RenderSettings.fogColor = fogColor;

        // 물 차오르기 (5초 연출)
        if (cleanWater != null)
            StartCoroutine(RiseWater());
    }

    private IEnumerator RiseWater()
    {
        if (activateCleanWater)
            cleanWater.gameObject.SetActive(true);

        Vector3 p = cleanWater.localPosition;
        p.y = waterStartY;
        cleanWater.localPosition = p;

        float t = 0f;
        while (t < waterRiseDuration)
        {
            t += Time.deltaTime;
            float y = Mathf.Lerp(waterStartY, waterEndY, Mathf.Clamp01(t / waterRiseDuration));
            Vector3 cur = cleanWater.localPosition;
            cur.y = y;
            cleanWater.localPosition = cur;
            yield return null;
        }

        Vector3 end = cleanWater.localPosition;
        end.y = waterEndY;
        cleanWater.localPosition = end;
    }


    // 스카이박스 페이드: 기존 스카이박스를 어둠게 낮춘 뒤 새 스카이박스로 교체하고 다시 밝힌다.
    // 원본 메티리얼 에셋 보호를 위해 런타임 인스턴스로 동작한다.
    private IEnumerator FadeSkybox()
    {
        if (sunsetSkybox == null)
            yield break;

        float half = Mathf.Max(0.01f, skyboxFadeDuration * 0.5f);

        Material fromInst = RenderSettings.skybox != null ? new Material(RenderSettings.skybox) : null;
        Material toInst = new Material(sunsetSkybox);

        bool fromHasExp = fromInst != null && fromInst.HasProperty("_Exposure");
        bool toHasExp = toInst.HasProperty("_Exposure");
        float fromExp = fromHasExp ? fromInst.GetFloat("_Exposure") : 1f;
        float toExp = toHasExp ? toInst.GetFloat("_Exposure") : 1f;

        // 1단계: 기존 스카이박스 노출 → 0 (어두워짐)
        if (fromInst != null)
        {
            RenderSettings.skybox = fromInst;
            float t = 0f;
            while (t < half)
            {
                t += Time.deltaTime;
                if (fromHasExp) fromInst.SetFloat("_Exposure", Mathf.Lerp(fromExp, 0f, t / half));
                yield return null;
            }
        }

        // 교체: 새 스카이박스(노출 0에서 시작)
        if (toHasExp) toInst.SetFloat("_Exposure", 0f);
        RenderSettings.skybox = toInst;
        DynamicGI.UpdateEnvironment();

        // 2단계: 새 스카이박스 노출 0 → 원래 값 (밝아짐)
        float t2 = 0f;
        while (t2 < half)
        {
            t2 += Time.deltaTime;
            if (toHasExp) toInst.SetFloat("_Exposure", Mathf.Lerp(0f, toExp, t2 / half));
            yield return null;
        }
        if (toHasExp) toInst.SetFloat("_Exposure", toExp);
        DynamicGI.UpdateEnvironment();
    }
}
