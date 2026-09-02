using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// 수생식물 미션 완료 연출
public class PlantClearSequence : MonoBehaviour
{
    [Header("트리거: 이 미션들이 모두 클리어되면 실행")]
    [SerializeField] private List<PetalRhythmMission> plants = new List<PetalRhythmMission>();

    [Header("활성화할 오브젝트 (FloatingPlant, Fish 등)")]
    [SerializeField] private List<GameObject> objectsToActivate = new List<GameObject>();

    [Header("식생 복원: PlantRevive 들의 부모 (AquaticPlant/Static)")]
    [SerializeField] private Transform aquaticStaticRoot;

    [Header("스카이박스 머티리얼 (Sunset)")]
    [SerializeField] private Material sunsetSkybox;
    [Tooltip("스카이박스 전환 페이드 시간(초)")]
    [SerializeField] private float skyboxFadeDuration = 3f;


    [Header("안개 색")]
    
    [Header("연출 컷씬 (비우면 기존처럼 즉시 전부 적용)")]
    [Tooltip("연결하면 하늘/식생/물고기 효과의 발동 시점을 컷씬이 가져간다.")]
    [SerializeField] private Stage2SkyCutscene cutscene;
[SerializeField] private Color fogColor = new Color32(0x4B, 0x44, 0x35, 0xFF);
    /// <summary>수생식물 미션(MISSION 2) 완료. 연꽃 3개가 모두 클리어되어 완료 연출이 시작될 때 1회.</summary>
    public static event System.Action MissionCleared;


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

        // 컷씬이 연결되어 있고 재생 가능하면,
        // 각 효과의 발동 시점을 컷씬이 가져간다.
        // (컷씬이 없거나 XR 준비 전이면 기존처럼 즉시 전부 적용)
        bool handledByCutscene =
            cutscene != null && cutscene.TryPlay(this);

        if (!handledByCutscene)
        {
            // 활성화 — StartRevive 전에 활성화해야 하위 코루틴이 돌아간다
            ActivateObjects();
            ReviveVegetation();
            StartSkyboxFade();
            ApplyFogColor();
        }

        // 튜토리얼 진행 알림 (TutorialMissionManager_2가 구독)
        MissionCleared?.Invoke();
    }

    // =================================================
    // 개별 단계
    //
    // 컷씬이 각 컷에서 하나씩 불러 쓴다.
    // 컷씬이 없으면 PlaySequence가 한 번에 다 부른다.
    // =================================================

    /// <summary>활성화 대상(Fish 등)을 켠다.</summary>
    public void ActivateObjects()
    {
        if (objectsToActivate == null)
            return;

        foreach (var go in objectsToActivate)
            if (go != null)
                go.SetActive(true);
    }

    /// <summary>식생 채도 복원을 시작한다. PlantRevive마다 reviveDuration 동안 lerp.</summary>
    public void ReviveVegetation()
    {
        if (aquaticStaticRoot == null)
            return;

        foreach (var pr in aquaticStaticRoot.GetComponentsInChildren<PlantRevive>(true))
            pr.StartRevive();
    }

    /// <summary>스카이박스 크로스페이드를 시작한다. skyboxFadeDuration 동안 진행.</summary>
    public void StartSkyboxFade()
    {
        if (sunsetSkybox == null)
            return;

        StartCoroutine(FadeSkybox());
    }

    /// <summary>안개 색을 적용한다.</summary>
    public void ApplyFogColor()
    {
        RenderSettings.fogColor = fogColor;
    }

    /// <summary>스카이박스 전환에 걸리는 시간(초). 컷씬이 컷 길이를 맞추는 데 참고한다.</summary>
    public float SkyboxFadeDuration => skyboxFadeDuration;
    // 도메인 리로드 비활성화 환경 대비: 재생 세션 시작 전 정적 이벤트 리셋
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        MissionCleared = null;
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
