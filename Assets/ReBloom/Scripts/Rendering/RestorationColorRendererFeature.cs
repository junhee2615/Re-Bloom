using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace ReBloom.Rendering
{
    public enum RestorationColorStage
    {
        Stage1,
        Stage3
    }

    [Serializable]
    public sealed class RestorationColorGrade
    {
        [Range(-100f, 100f)]
        public float saturation = -55f;

        [Range(-5f, 5f)]
        public float postExposure = -0.3f;

        [Range(-100f, 100f)]
        public float contrast = 10f;

        [ColorUsage(false, true)]
        public Color colorFilter = new Color(0.82f, 0.88f, 1f, 1f);
    }

    [Serializable]
    public sealed class RestorationColorRendererSettings
    {
        public Shader shader;

        [Header("Stage 1")]
        public RestorationColorGrade stage1 = new RestorationColorGrade();

        [Header("Stage 3")]
        public RestorationColorGrade stage3 = new RestorationColorGrade
        {
            saturation = -65f,
            postExposure = -0.4f,
            contrast = 15f,
            colorFilter = Color.white
        };

        [Min(0f)]
        public float fadeDuration = 3f;

        [Header("Preview (테스트용)")]
        public bool previewEnabled;
        public RestorationColorStage previewStage;

        [Range(0f, 1f)]
        public float previewStrength = 1f;

        [Header("Diagnostics (Temporary)")]
        public bool debugLogging;

        // TEMP: 원인 확인 후 제거. Preview Enabled일 때만 화면을 빨간색으로 출력해 셰이더 출력 반영 여부를 확인한다.
        [Tooltip("임시 진단용. Preview Enabled가 켜져 있을 때만 색 보정 대신 빨간색을 출력합니다.")]
        public bool debugSolidColor;
    }

    [DisallowMultipleRendererFeature("Restoration Color")]
    public sealed class RestorationColorRendererFeature : ScriptableRendererFeature
    {
        private const string ShaderName = "Hidden/ReBloom/RestorationColor";

        [SerializeField]
        private RestorationColorRendererSettings settings = new RestorationColorRendererSettings();

        private Material material;
        private RestorationColorPass pass;

        private bool runtimeFilterActive;
        private RestorationColorStage runtimeStage;
        private float runtimeStrength;
        private float fadeStartStrength;
        private float fadeStartTime;
        private bool fadingOut;
        private readonly HashSet<string> reportedDiagnostics = new HashSet<string>();
        private bool diagnosticsWereEnabled;

        public override void Create()
        {
            DisposeResources();

            if (settings.shader == null)
                settings.shader = Shader.Find(ShaderName);

            if (settings.shader == null)
            {
                Debug.LogError($"[{nameof(RestorationColorRendererFeature)}] Shader '{ShaderName}' was not found.", this);
                return;
            }

            material = CoreUtils.CreateEngineMaterial(settings.shader);
            pass = new RestorationColorPass(material, this)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraData cameraData = renderingData.cameraData;
            bool isSceneViewCamera = cameraData.cameraType == CameraType.SceneView;
            UpdateDiagnosticLoggingState();

            string diagnosticState = GetDiagnosticState(cameraData.cameraType, cameraData.renderType);
            LogDiagnostic(
                cameraData.camera,
                "AddRenderPasses",
                "called",
                diagnosticState,
                $"AddRenderPasses called. {FormatCameraState(cameraData.camera, cameraData.cameraType, cameraData.renderType)}, " +
                $"isPreviewCamera={cameraData.isPreviewCamera}, previewEnabled={settings.previewEnabled}, " +
                $"previewStage={settings.previewStage}, previewStrength={settings.previewStrength:0.###}, " +
                $"passReady={pass != null && material != null}.");

            if (pass == null || material == null)
            {
                LogDiagnostic(
                    cameraData.camera,
                    "AddRenderPasses",
                    "missing-resources",
                    diagnosticState,
                    "Pass or material is missing; the render pass was not enqueued.");
                return;
            }

            if (cameraData.isPreviewCamera ||
                (isSceneViewCamera && !settings.previewEnabled) ||
                cameraData.renderType != CameraRenderType.Base)
            {
                string reason = cameraData.isPreviewCamera
                    ? "preview-camera"
                    : isSceneViewCamera && !settings.previewEnabled
                        ? "preview-disabled"
                        : "non-base-camera";
                LogDiagnostic(
                    cameraData.camera,
                    "AddRenderPasses",
                    $"skipped-{reason}",
                    diagnosticState,
                    $"Render pass skipped: {reason}. {FormatCameraState(cameraData.camera, cameraData.cameraType, cameraData.renderType)}, " +
                    $"previewEnabled={settings.previewEnabled}.");
                return;
            }

            RestorationColorGrade grade;
            float strength;

            if (settings.previewEnabled)
            {
                grade = GetGrade(settings.previewStage);
                strength = settings.previewStrength;
            }
            else
            {
                strength = GetRuntimeStrength();
                if (!runtimeFilterActive || strength <= 0f)
                {
                    LogDiagnostic(
                        cameraData.camera,
                        "AddRenderPasses",
                        "skipped-runtime-inactive",
                        diagnosticState,
                        $"Render pass skipped: runtimeFilterActive={runtimeFilterActive}, strength={strength:0.###}.");
                    return;
                }

                grade = GetGrade(runtimeStage);
            }

            if (grade == null || strength <= 0f)
            {
                LogDiagnostic(
                    cameraData.camera,
                    "AddRenderPasses",
                    "skipped-zero-strength",
                    diagnosticState,
                    $"Render pass skipped: gradeMissing={grade == null}, strength={strength:0.###}.");
                return;
            }

            bool debugSolidColor = settings.previewEnabled && settings.debugSolidColor; // TEMP diagnostic
            pass.Setup(grade, strength, debugSolidColor);
            renderer.EnqueuePass(pass);

            LogDiagnostic(
                cameraData.camera,
                "AddRenderPasses",
                "enqueued",
                diagnosticState,
                $"Render pass enqueued. stage={(settings.previewEnabled ? settings.previewStage : runtimeStage)}, strength={strength:0.###}, " +
                $"debugSolidColor={debugSolidColor}.");
        }

        public void Activate(RestorationColorStage stage)
        {
            runtimeStage = stage;
            runtimeStrength = 1f;
            runtimeFilterActive = true;
            fadingOut = false;
        }

        public void FadeOut()
        {
            if (!runtimeFilterActive || settings.fadeDuration <= 0f)
            {
                runtimeStrength = 0f;
                runtimeFilterActive = false;
                fadingOut = false;
                return;
            }

            fadeStartStrength = GetRuntimeStrength();
            fadeStartTime = Time.unscaledTime;
            fadingOut = true;
        }

        /// <summary>
        /// 페이드 없이 즉시 필터를 끈다. 씬 이탈이나 이미 복원이 끝난 방에 늦게 들어온 경우에 쓴다.
        /// </summary>
        public void Deactivate()
        {
            runtimeStrength = 0f;
            runtimeFilterActive = false;
            fadingOut = false;
        }

        protected override void Dispose(bool disposing)
        {
            DisposeResources();
        }

        private RestorationColorGrade GetGrade(RestorationColorStage stage)
        {
            return stage == RestorationColorStage.Stage1 ? settings.stage1 : settings.stage3;
        }

        private void UpdateDiagnosticLoggingState()
        {
            if (settings.debugLogging)
            {
                diagnosticsWereEnabled = true;
            }
            else if (diagnosticsWereEnabled)
            {
                reportedDiagnostics.Clear();
                diagnosticsWereEnabled = false;
            }
        }

        private string GetDiagnosticState(CameraType cameraType, CameraRenderType renderType)
        {
            return $"{cameraType}|{renderType}|{settings.previewEnabled}|{settings.previewStage}|{settings.previewStrength:0.###}|{settings.debugSolidColor}";
        }

        private string FormatCameraState(Camera camera, CameraType cameraType, CameraRenderType renderType)
        {
            string cameraName = camera != null ? camera.name : "<null>";
            return $"camera='{cameraName}', cameraType={cameraType}, renderType={renderType}";
        }

        private void LogDiagnostic(
            Camera camera,
            string phase,
            string status,
            string state,
            string message)
        {
            if (!settings.debugLogging)
                return;

            int cameraId = camera != null ? camera.GetInstanceID() : 0;
            string key = $"{cameraId}|{phase}|{status}|{state}";
            if (!reportedDiagnostics.Add(key))
                return;

            Debug.Log($"[{nameof(RestorationColorRendererFeature)}][{phase}] {message}", this);
        }

        private float GetRuntimeStrength()
        {
            if (!fadingOut)
                return runtimeStrength;

            if (settings.fadeDuration <= 0f)
            {
                runtimeStrength = 0f;
                runtimeFilterActive = false;
                fadingOut = false;
                return runtimeStrength;
            }

            float progress = Mathf.Clamp01((Time.unscaledTime - fadeStartTime) / settings.fadeDuration);
            runtimeStrength = fadeStartStrength * (1f - Mathf.SmoothStep(0f, 1f, progress));

            if (progress >= 1f)
            {
                runtimeStrength = 0f;
                runtimeFilterActive = false;
                fadingOut = false;
            }

            return runtimeStrength;
        }

        private void DisposeResources()
        {
            pass?.Dispose();
            pass = null;

            CoreUtils.Destroy(material);
            material = null;
        }

        private sealed class RestorationColorPass : ScriptableRenderPass
        {
            private static readonly int SaturationId = Shader.PropertyToID("_Saturation");
            private static readonly int PostExposureId = Shader.PropertyToID("_PostExposure");
            private static readonly int ContrastId = Shader.PropertyToID("_Contrast");
            private static readonly int ColorFilterId = Shader.PropertyToID("_ColorFilter");
            private static readonly int FilterStrengthId = Shader.PropertyToID("_FilterStrength");
            private static readonly int DebugSolidColorId = Shader.PropertyToID("_DebugSolidColor"); // TEMP diagnostic

            private readonly Material material;
            private readonly RestorationColorRendererFeature feature;
            private readonly MaterialPropertyBlock properties = new MaterialPropertyBlock();
            private RestorationColorGrade grade;
            private float strength;
            private bool debugSolidColor; // TEMP diagnostic
            private RTHandle temporaryColor;

            public RestorationColorPass(Material material, RestorationColorRendererFeature feature)
            {
                this.material = material;
                this.feature = feature;
            }

            public void Setup(RestorationColorGrade activeGrade, float activeStrength, bool activeDebugSolidColor)
            {
                grade = activeGrade;
                strength = activeStrength;
                debugSolidColor = activeDebugSolidColor;

                properties.Clear();
                properties.SetFloat(SaturationId, grade.saturation);
                properties.SetFloat(PostExposureId, grade.postExposure);
                properties.SetFloat(ContrastId, grade.contrast);
                properties.SetColor(ColorFilterId, grade.colorFilter);
                properties.SetFloat(FilterStrengthId, strength);
                properties.SetFloat(DebugSolidColorId, debugSolidColor ? 1f : 0f);
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                bool isSceneViewCamera = cameraData.isSceneViewCamera;
                string diagnosticState = feature.GetDiagnosticState(cameraData.cameraType, cameraData.renderType);

                feature.LogDiagnostic(
                    cameraData.camera,
                    "RecordRenderGraph",
                    "called",
                    diagnosticState,
                    $"RecordRenderGraph called. {feature.FormatCameraState(cameraData.camera, cameraData.cameraType, cameraData.renderType)}, " +
                    $"previewEnabled={feature.settings.previewEnabled}, filterStrength={strength:0.###}.");

                if (resources.isActiveTargetBackBuffer)
                {
                    feature.LogDiagnostic(
                        cameraData.camera,
                        "RecordRenderGraph",
                        "skipped-backbuffer",
                        diagnosticState,
                        $"Render Graph returned early: active target is the backbuffer. cameraType={cameraData.cameraType}, " +
                        $"renderType={cameraData.renderType}, isSceneViewCamera={isSceneViewCamera}.");
                    return;
                }

                TextureHandle source = resources.activeColorTexture;
                if (!source.IsValid())
                {
                    feature.LogDiagnostic(
                        cameraData.camera,
                        "RecordRenderGraph",
                        "skipped-invalid-source",
                        diagnosticState,
                        $"Render Graph returned early: active color texture is invalid. cameraType={cameraData.cameraType}, " +
                        $"renderType={cameraData.renderType}, isSceneViewCamera={isSceneViewCamera}.");
                    return;
                }

                TextureDesc destinationDescription = renderGraph.GetTextureDesc(source);
                destinationDescription.name = "Restoration Color";
                destinationDescription.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(destinationDescription);

                RenderGraphUtils.BlitMaterialParameters blit = new RenderGraphUtils.BlitMaterialParameters(
                    source,
                    destination,
                    material,
                    0,
                    properties,
                    RenderGraphUtils.FullScreenGeometryType.ProceduralTriangle);

                renderGraph.AddBlitPass(blit, passName: "Restoration Color");
                resources.cameraColor = destination;

                feature.LogDiagnostic(
                    cameraData.camera,
                    "RecordRenderGraph",
                    "registered",
                    diagnosticState,
                    $"Restoration Color Blit was registered. sourceValid={source.IsValid()}, destinationValid={destination.IsValid()}, " +
                    $"activeTargetBackBuffer={resources.isActiveTargetBackBuffer}, cameraType={cameraData.cameraType}, " +
                    $"renderType={cameraData.renderType}.");
            }

#pragma warning disable CS0672, CS0618
            public override void OnCameraSetup(CommandBuffer commandBuffer, ref RenderingData renderingData)
            {
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;

                RenderingUtils.ReAllocateIfNeeded(
                    ref temporaryColor,
                    descriptor,
                    FilterMode.Bilinear,
                    TextureWrapMode.Clamp,
                    name: "Restoration Color");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CameraData cameraData = renderingData.cameraData;
                feature.LogDiagnostic(
                    cameraData.camera,
                    "Execute",
                    "compatibility-path",
                    feature.GetDiagnosticState(cameraData.cameraType, cameraData.renderType),
                    $"Compatibility Execute called. {feature.FormatCameraState(cameraData.camera, cameraData.cameraType, cameraData.renderType)}, " +
                    $"filterStrength={strength:0.###}, temporaryColorReady={temporaryColor != null}.");

                if (temporaryColor == null)
                    return;

                CommandBuffer commandBuffer = CommandBufferPool.Get("Restoration Color");
                using (new ProfilingScope(commandBuffer, new ProfilingSampler("Restoration Color")))
                {
                    material.SetFloat(SaturationId, grade.saturation);
                    material.SetFloat(PostExposureId, grade.postExposure);
                    material.SetFloat(ContrastId, grade.contrast);
                    material.SetColor(ColorFilterId, grade.colorFilter);
                    material.SetFloat(FilterStrengthId, strength);
                    material.SetFloat(DebugSolidColorId, debugSolidColor ? 1f : 0f);

                    RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
                    Blitter.BlitCameraTexture(commandBuffer, source, temporaryColor, material, 0);
                    Blitter.BlitCameraTexture(commandBuffer, temporaryColor, source);
                }

                context.ExecuteCommandBuffer(commandBuffer);
                CommandBufferPool.Release(commandBuffer);
            }
#pragma warning restore CS0672, CS0618

            public void Dispose()
            {
                temporaryColor?.Release();
                temporaryColor = null;
            }
        }
    }
}
