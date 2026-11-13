using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace ReBloom.Rendering
{
    /// <summary>
    /// Stage1/Stage3에 들어오면 복원 전 색감 필터를 켜고, 그 밖의 씬에서는 즉시 끈다.
    /// 미션 완료 감지와 페이드 시작은 스테이지 씬 안에 만드는 <see cref="RestorationColorStageTracker"/>가 맡는다.
    ///
    /// Renderer Feature는 씬이 공유하는 에셋이므로 런타임 상태가 씬 전환 뒤에도 남는다.
    /// 그래서 Single 로드마다 상태를 다시 정한다. Additive 로드(Opening의 Stage 연출,
    /// 타이틀 배경 등)는 현재 씬의 상태를 바꾸지 않도록 무시한다.
    /// </summary>
    public static class RestorationColorSceneController
    {
        private const string Stage1SceneName = "Stage1";
        private const string Stage3SceneName = "Stage3";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= DeactivateAll;
            DeactivateAll();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;

            // 에디터에서 Play를 끝낼 때도 호출된다. 공유 에셋에 필터 상태가 남지 않게 한다.
            Application.quitting -= DeactivateAll;
            Application.quitting += DeactivateAll;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single)
                return;

            if (scene.name == Stage1SceneName)
                BeginStage(scene, RestorationColorStage.Stage1);
            else if (scene.name == Stage3SceneName)
                BeginStage(scene, RestorationColorStage.Stage3);
            else
                DeactivateAll();
        }

        private static void BeginStage(Scene scene, RestorationColorStage stage)
        {
            // 이 렌더러에 Feature가 없으면(예: 아직 등록 전인 Mobile_Renderer) 아무것도 하지 않는다.
            if (!ForEachFeature(feature => feature.Activate(stage)))
                return;

            // 추적 오브젝트를 스테이지 씬에 두어 씬이 언로드되면 함께 사라지게 한다.
            GameObject trackerObject = new GameObject("[RestorationColorStageTracker]");
            SceneManager.MoveGameObjectToScene(trackerObject, scene);
            trackerObject.AddComponent<RestorationColorStageTracker>().Initialize(stage, scene);
        }

        internal static void FadeOutAll()
        {
            ForEachFeature(feature => feature.FadeOut());
        }

        internal static void DeactivateAll()
        {
            ForEachFeature(feature => feature.Deactivate());
        }

        private static bool ForEachFeature(Action<RestorationColorRendererFeature> action)
        {
            UniversalRenderPipelineAsset pipelineAsset =
                GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

            if (pipelineAsset == null)
                return false;

            bool found = false;
            var rendererDataList = pipelineAsset.rendererDataList;

            for (int rendererIndex = 0; rendererIndex < rendererDataList.Length; rendererIndex++)
            {
                ScriptableRendererData rendererData = rendererDataList[rendererIndex];
                if (rendererData == null)
                    continue;

                foreach (ScriptableRendererFeature feature in rendererData.rendererFeatures)
                {
                    if (feature is not RestorationColorRendererFeature restorationFeature)
                        continue;

                    action(restorationFeature);
                    found = true;
                }
            }

            return found;
        }
    }
}
