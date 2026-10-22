using System.Collections.Generic;
using FlatKit;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Enables the project's FlatKit fog renderer feature everywhere except StartScene.
/// The original feature state is preserved, so renderer features and post-processing
/// that are unrelated to fog are never changed.
/// </summary>
public static class FogRendererFeatureSceneController
{
    // private const string DisableSceneName = "StartScene";
    private static readonly HashSet<string> DisableFogScenes = new()
    {
        "StartScene",

        // StartScene이 3D 타이틀 배경으로 Additive Load하는 씬.
        // sceneLoaded는 Additive 로드에도 발생하므로, 여기에 없으면
        // 배경이 올라오는 순간 StartScene에서 꺼 둔 FlatKitFog가 다시 켜진다.
        "Stage1_TitleBackground",
    };

    // Renderer features are assets shared by scenes. Keep their configured state so a
    // scene transition can restore it instead of assuming that fog is always enabled.
    private static readonly Dictionary<int, bool> ConfiguredFeatureStates = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        RestoreConfiguredFeatureStates();
        ConfiguredFeatureStates.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        Application.quitting -= RestoreConfiguredFeatureStates;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Application.quitting -= RestoreConfiguredFeatureStates;
        Application.quitting += RestoreConfiguredFeatureStates;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyForScene(scene);
    }

    private static void ApplyForScene(Scene scene)
    {
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (pipelineAsset == null) return;

        // bool shouldDisableFog = scene.name == DisableSceneName;
        bool shouldDisableFog = DisableFogScenes.Contains(scene.name);
        var rendererDataList = pipelineAsset.rendererDataList;

        for (int rendererIndex = 0; rendererIndex < rendererDataList.Length; rendererIndex++)
        {
            ScriptableRendererData rendererData = rendererDataList[rendererIndex];
            if (rendererData == null) continue;

            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
            {
                if (feature is not FlatKitFog) continue;

                int featureId = feature.GetInstanceID();
                if (!ConfiguredFeatureStates.TryGetValue(featureId, out bool configuredState))
                {
                    configuredState = feature.isActive;
                    ConfiguredFeatureStates.Add(featureId, configuredState);
                }

                feature.SetActive(!shouldDisableFog && configuredState);
            }
        }
    }

    /// <summary>
    /// 지금 씬에서 FlatKit 안개 렌더러 피처를 끄거나(<paramref name="suppressed"/> = true) 원래 설정으로 되돌린다.
    /// 엔딩처럼 안개를 완전히 걷어야 할 때 쓴다. 렌더러 에셋은 공유되므로 끝날 때(Play 종료·앱 종료)
    /// <see cref="RestoreConfiguredFeatureStates"/>가 원래 상태로 되돌린다.
    /// </summary>
    public static void SetFogSuppressed(bool suppressed)
    {
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (pipelineAsset == null) return;

        var rendererDataList = pipelineAsset.rendererDataList;
        for (int rendererIndex = 0; rendererIndex < rendererDataList.Length; rendererIndex++)
        {
            ScriptableRendererData rendererData = rendererDataList[rendererIndex];
            if (rendererData == null) continue;

            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
            {
                if (feature is not FlatKitFog) continue;

                int featureId = feature.GetInstanceID();
                if (!ConfiguredFeatureStates.TryGetValue(featureId, out bool configuredState))
                {
                    configuredState = feature.isActive;
                    ConfiguredFeatureStates.Add(featureId, configuredState);
                }

                feature.SetActive(!suppressed && configuredState);
            }
        }
    }

    private static void RestoreConfiguredFeatureStates()
    {
        UniversalRenderPipelineAsset pipelineAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

        if (pipelineAsset == null) return;

        var rendererDataList = pipelineAsset.rendererDataList;
        for (int rendererIndex = 0; rendererIndex < rendererDataList.Length; rendererIndex++)
        {
            ScriptableRendererData rendererData = rendererDataList[rendererIndex];
            if (rendererData == null) continue;

            foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
            {
                if (feature is FlatKitFog &&
                    ConfiguredFeatureStates.TryGetValue(feature.GetInstanceID(), out bool configuredState))
                {
                    feature.SetActive(configuredState);
                }
            }
        }
    }
}
