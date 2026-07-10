using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using ProfilingScope = UnityEngine.Rendering.ProfilingScope;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Haze.Runtime
{
    public sealed class HazeRendererFeature : ScriptableRendererFeature
    {
        public enum HeightFogMode
        {
            Global,
            Local,
            CameraRelative
        }

        private enum Resolution
        {
            _16 = 0,
            _32 = 1,
            _64 = 2,
            _128 = 3,
            _256 = 4
        }

        private enum AspectRatioAdjustment
        {
            None = 0,
            Upscale = 1,
            Downscale = 2
        }

        private enum BufferSampling
        {
            Tricubic = 0,
            Trilinear = 1,
            Point = 2
        }
        
#region Density Volume Data

        [Serializable, StructLayout(LayoutKind.Sequential)]
        public struct HazeDensityVolumeData
        {
            public float4x4 WorldToLocal;
            public float Shape;
            /*
             * X -> Density
             * Y -> Noise Threshold
             * Z -> Main light density boost
             * W -> Secondary light density boost
             */
            public float4 DensitySettings;
            public float3 AmbientColor;
            public float3 LightContribution;
            public float4 HeightFog;

            /*
             * X -> Additional light contribution
             * Y -> Probe volume contribution
             * Z -> Main light phase
             * W -> Gradient sampling index
             */
            public float4 LightAndGradientSettings;
            public float GradientMappingMethod;
            public float Override;

            public static int SizeInBytes => sizeof(float) * ((4 * 4) + 1 + 4 + 3 + 3 + 4 + 4 + 1 + 1);
            
            public void SetData(HazeDensityVolume densityVolume)
            {
                var ambientColor = densityVolume.AmbientColor;
                var lightContribution = densityVolume.MainLightContribution;

                WorldToLocal = densityVolume.WorldToLocal;
                Shape = (float)densityVolume.VolumeShape;
                DensitySettings = new float4(densityVolume.Density, densityVolume.NoiseThreshold,
                    densityVolume.MainLightDensityBoost, densityVolume.SecondaryLightDensityBoost);
                AmbientColor = new float3(ambientColor.r, ambientColor.g, ambientColor.b);
                LightContribution = new float3(lightContribution.r, lightContribution.g, lightContribution.b);
                HeightFog = new float4(densityVolume.MaxFogHeight, densityVolume.HeightFogSmoothness,
                    densityVolume.HeightFogFactor, (float)densityVolume.VolumeHeightFogMode);
                LightAndGradientSettings = new float4(densityVolume.AdditionalLightContribution,
                    densityVolume.ProbeVolumeContribution, densityVolume.MainLightScattering,
                    densityVolume.VolumeIndex);
                GradientMappingMethod = (float)densityVolume.GradientMappingMethod + densityVolume.GradientLightScattering;
                Override = densityVolume.DensityMode == HazeDensityVolume.VolumeDensityMode.Override ? 1 : 0;
            }
        }

        private HazeDensityVolumeData[] _densityVolumeData;
        private GraphicsBuffer _densityVolumeDataBuffer;

        private static Texture2D _volumeGradientTexture; 
        
        private const int MaximumDensityVolumes = 16;

        private static Camera _currentCamera;
        private static readonly List<HazeDensityVolume> DensityVolumes = new();

        private Dictionary<Camera, List<HazeDensityVolume>> _visibleDensityVolumesDictionary = new();
        // private List<HazeDensityVolume> _visibleDensityVolumes = new();
        private readonly Plane[] _planeArray = new Plane[6];

#endregion

        [Serializable]
        public class NoiseData
        {
            [Tooltip("The tiling of the noise texture. Increase for higher detail frequency.")]
            [SerializeField] internal float noiseTiling = 0.1f;
            [Tooltip("The panning speed of the noise texture in each direction.")]
            [SerializeField] internal float3 noisePanningSpeed = new(0, 0, 0);
            [Tooltip("The weights of the noise texture. Each component is multiplied by the corresponding channel (RGBA) of the noise texture.")]
            [SerializeField] internal float4 noiseWeights = new(1, 1, 1, 1);
        }

        [Serializable]
        public class MultipleScatteringData
        {
            [Tooltip("Determines the blend between regular fog and fog with screen-space multiple scattering. Set to 0 to completely disable multiple scattering.")]
            [SerializeField] [Range(0,1)] internal float intensity = 1.0f;
            [Tooltip("Determines the brightness of the blurred image that gets composited with the fog. The lower the value, the brighter the result.")]
            [SerializeField] [Min(0.01f)] internal float radius = 1.0f;
            [Tooltip("Determines the blur amount of the multiple scattering buffer. A lower value will make the blur effect less intense.")]
            [SerializeField] [Range(0, 1)] internal float scatter = 1.0f;
            [Tooltip("Determines the brightness threshold for the multiple scattering pre-filtering. A value of 0 does no filtering, blurring the whole image, while a larger value will only blur brighter parts of the image.")]
            [SerializeField] [Range(0, 1)] internal float threshold = 0.0f;
            [Tooltip("Determines the maximum number of blur iterations for the multiple scattering effect. A larger amount of iterations will result in a increased blurring distance, but it will increase performance overhead.")]
            [SerializeField] [Range(3, 10)] internal int maxIterations = 5;
        }

        private struct FroxelFogPassSettings
        {
            public int3 Resolution;
            public float2 FroxelFogRange;
            public ComputeShader FroxelFogComputeShader;
            public Texture3D NoiseTexture;
            public NoiseData NoiseData;
            public float TemporalAccumulationBlending;
            public float MainLightShadowBias;
            public bool JitterNoiseMotion;
        }
        
        private static readonly int VolumeNearClipPlane = Shader.PropertyToID("_VolumeNearClipPlane");
        private static readonly int VolumeFarClipPlane = Shader.PropertyToID("_VolumeFarClipPlane");
        private static readonly int SourceTexLowMip = Shader.PropertyToID("_SourceTexLowMip");
        private static readonly int BloomParams = Shader.PropertyToID("_Params");
        private static readonly int BloomIntensity = Shader.PropertyToID("_HazeBloomIntensity");
        private static readonly int FroxelVolumeVp = Shader.PropertyToID("_FroxelVolumeVP");
        private static readonly int SampleScale = Shader.PropertyToID("_SampleScale");
        private static readonly int BlurRadius = Shader.PropertyToID("_HazeBlurRadius");
        private static readonly int IgnStrength = Shader.PropertyToID("_IGNStrength");
        private static readonly int HazeDepthBias = Shader.PropertyToID("_HazeDepthBias");

        [Header("Shaders")]
        [SerializeField, HideInInspector] private ComputeShader _froxelFogComputeShader;
        [SerializeField, HideInInspector] private Shader _froxelFogCompositeShader;
        [SerializeField, HideInInspector] private Shader _bloomShader;

        [SerializeField] private bool _renderBeforeTransparents;
        
        [Header("Resolution")]
        [Tooltip("The resolution for the width and height of the froxel buffers.")]
        [SerializeField] private Resolution _froxelBufferResolution = Resolution._128;
        [Tooltip("Aspect ratio adjustment mode for froxel resolution. Set to none to keep X and Y resolution equal. Upscale will increase the X/Y resolution to match the aspect ratio, while Downscale will reduce the Y/X resolution.")]
        [SerializeField] private AspectRatioAdjustment _aspectRatioAdjustment = AspectRatioAdjustment.None;
        [Tooltip("The amount of depth slices of the froxel buffers.")]
        [SerializeField] private Resolution _froxelBufferDepth = Resolution._64;
        [Tooltip("The near and far clipping planes of the froxel fog effect.")]
        [SerializeField] private float2 _froxelFogRange = new(0.1f, 500.0f);
        [Tooltip("Sampling method for the froxel buffer. Tricubic is the most artifact-free but more performance-intensive. Point is for more lo-fi stylized effects.")]
        [SerializeField] private BufferSampling _bufferSampling;
        [Tooltip("Adjusts the strength of the interleaved gradient noise (IGN) which reduces artifacts when using TAA.")]
        [SerializeField, Range(0,1)] private float _interleavedGradientNoiseStrength = 1.0f;
        [Tooltip("Toggles the motion of the jitter noise. Turn off for a more lo-fi result.")]
        [SerializeField] private bool _jitterNoiseMotion = true;
        [Tooltip("Excludes overlay cameras from rendering HAZE.")]
        [SerializeField] private bool _excludeOverlayCameras = true;

        [Header("Temporal accumulation")]
        [Tooltip("Controls the temporal accumulation blending. Set to 0 to disable temporal accumulation.")]
        [SerializeField, Range(0, 0.99f)] private float _temporalAccumulationBlending = 0.95f;

        [Header("Lighting settings")] 
        [Tooltip("Main light shadow bias to help with light leaking from walls.")]
        [SerializeField, Range(-0.5f, 0.5f)] private float _mainLightShadowBias = 0.0f;
        [Tooltip("Depth bias applied in composition to help prevent light leaking from walls.")]
        [SerializeField, Range(0f, 0.1f)] private float _depthBias = 0.0f;
        
        [Header("Volume settings")]
        [Tooltip("Maximum distance at which density volumes are considered visible.")]
        [SerializeField] private float _maximumVolumeDistance = 100;

        [Header("Noise")]
        [Tooltip("The 3D noise texture used for the fog density")]
        [SerializeField] private Texture3D _noiseTexture;
        [SerializeField] private NoiseData _noiseData = new();

        [Header("Multiple Scattering")]
        [SerializeField] private MultipleScatteringData _multipleScatteringData = new();
        
        private static FroxelFogRenderPass _froxelFogRenderPass;
        private static FroxelFogCompositePass _froxelFogCompositePass;
        private static MultipleScatteringPass _multipleScatteringPass;

#if UNITY_EDITOR
        private static bool _initialized = false;
#endif
        private static readonly int GlobalBloomTexture = Shader.PropertyToID("_GLOBAL_BloomTexture");

        #region Density Volume Methods

        private static readonly Func<HazeDensityVolume, HazeDensityVolume, bool> VolumeOrderComparison = (a, b) =>
        {
            var biasA = a.Density < 0 ? 0 : 1000;
            var biasB = b.Density < 0 ? 0 : 1000;
            var camPos = _currentCamera.transform.position;
            if (a.Priority != b.Priority)
            {
                return a.Priority + biasA > b.Priority + biasB;
            }
            var distanceComparison = math.distancesq(camPos, a.VolumeBounds.ClosestPoint(camPos)) + biasA > math.distancesq(camPos, b.VolumeBounds.ClosestPoint(camPos)) + biasB;
            return distanceComparison;
        };

        private static readonly int ZBufferParameters = Shader.PropertyToID("_ZBufferParameters");
        private static readonly int InverseViewProjectionMatrix = Shader.PropertyToID("_InverseViewProjectionMatrix");
        private static readonly int PrevViewProjectionMatrix = Shader.PropertyToID("_PrevViewProjectionMatrix");
        private static readonly int TemporalReprojection = Shader.PropertyToID("_TemporalReprojection");
        private static readonly int TemporalAccumulationBlending = Shader.PropertyToID("_TemporalAccumulationBlending");
        private static readonly int MainLightShadowBias = Shader.PropertyToID("_MainLightShadowBias");
        private static readonly int JitterNoiseMotion = Shader.PropertyToID("_JitterNoiseMotion");
        private static readonly int GlobalNoisePanningTiling = Shader.PropertyToID("_GlobalNoisePanningTiling");
        private static readonly int GlobalNoiseWeights = Shader.PropertyToID("_GlobalNoiseWeights");
        private static readonly int VisibleDensityVolumes = Shader.PropertyToID("_VisibleDensityVolumes");
        private static readonly int GlobalDensityMultiplier = Shader.PropertyToID("_GlobalDensityMultiplier");
        private static readonly int GlobalDensityThreshold = Shader.PropertyToID("_GlobalDensityThreshold");
        private static readonly int GlobalMainLightDensityBoost = Shader.PropertyToID("_GlobalMainLightDensityBoost");
        private static readonly int GlobalSecondaryLightDensityBoost = Shader.PropertyToID("_GlobalSecondaryLightDensityBoost");
        private static readonly int LightScattering = Shader.PropertyToID("_LightScattering");
        private static readonly int GlobalAdditionalLightContribution = Shader.PropertyToID("_GlobalAdditionalLightContribution");
        private static readonly int GlobalProbeVolumeContribution = Shader.PropertyToID("_GlobalProbeVolumeContribution");
        private static readonly int AmbientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly int GlobalMainLightContribution = Shader.PropertyToID("_GlobalMainLightContribution");
        private static readonly int GlobalHeightFog = Shader.PropertyToID("_GlobalHeightFog");
        private static readonly int ColorDensityBuffer = Shader.PropertyToID("_ColorDensityBuffer");
        private static readonly int ColorDensityReadBuffer = Shader.PropertyToID("_ColorDensityReadBuffer");
        private static readonly int GlobalNoiseTexture = Shader.PropertyToID("_GlobalNoiseTexture");
        private static readonly int VolumeGradientTexture = Shader.PropertyToID("_VolumeGradientTexture");
        private static readonly int HazeDensityVolumeBuffer = Shader.PropertyToID("_HazeDensityVolumeBuffer");
        private static readonly int SecondaryLightDataBuffer = Shader.PropertyToID("_SecondaryLightDataBuffer");
        private static readonly int ScatterBuffer = Shader.PropertyToID("_ScatterBuffer");

        private static void SortVolumes(ref List<HazeDensityVolume> volumes,
            Func<HazeDensityVolume, HazeDensityVolume, bool> compare)
        {
            var len = volumes.Count;
            for (var i = 0; i < len; i++)
            {
                var current = volumes[i];
                for (var j = i - 1; j >= 0 && !compare(current, volumes[j]); j--)
                {
                    volumes[j + 1] = volumes[j];
                    volumes[j] = current;
                }
            }
        }

        private void UpdateVolumeVisibility(Camera camera)
        {
            if (camera == null || camera.cameraType == CameraType.Preview)
            {
                return;
            }

            if (!_visibleDensityVolumesDictionary.TryGetValue(camera, out var visibleDensityVolumes))
            {
                visibleDensityVolumes = new List<HazeDensityVolume>();
                camera.transform.hasChanged = true;
            }

            if (camera.transform.hasChanged)
            {
                visibleDensityVolumes.Clear();
                GeometryUtility.CalculateFrustumPlanes(camera, _planeArray);
                foreach (var densityVolume in DensityVolumes)
                {
                    if (densityVolume.IsWithinRange(camera.transform.position, _maximumVolumeDistance)
                        && densityVolume.IsWithinCameraFrustum(_planeArray))
                    {
                        visibleDensityVolumes.Add(densityVolume);
                    }
                }

                SortVolumes(ref visibleDensityVolumes, VolumeOrderComparison);
                _froxelFogRenderPass?.UpdateVisibleDensityVolumeCount(visibleDensityVolumes.Count);
                camera.transform.hasChanged = false;
            }
            _visibleDensityVolumesDictionary.TryAdd(camera, visibleDensityVolumes);
        }

        private void InitializeDensityVolumeData()
        {
            _densityVolumeData = new HazeDensityVolumeData[MaximumDensityVolumes];
            for (var i = 0; i < _densityVolumeData.Length; i++)
            {
                _densityVolumeData[i] = new HazeDensityVolumeData();
            }

            _densityVolumeDataBuffer?.Release();
            _densityVolumeDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaximumDensityVolumes, HazeDensityVolumeData.SizeInBytes);
            _froxelFogRenderPass?.UpdateDensityVolumeDataBuffer(_densityVolumeDataBuffer);
        }

        private void AssignDensityVolumeData(Camera camera)
        {
            if (!_visibleDensityVolumesDictionary.TryGetValue(camera, out var visibleVolumes))
            {
                return;
            }

            var volumeCount = visibleVolumes.Count; 
            if (volumeCount <= 0)
            {
                return;
            }

            for (var i = 0; i < math.min(MaximumDensityVolumes, volumeCount); i++)
            {
                _densityVolumeData[i] = visibleVolumes[i].DensityVolumeData;
            }
            _densityVolumeDataBuffer.SetData(_densityVolumeData);
        }

        private static void NotifyVisibilityUpdate()
        {
            if (Application.isPlaying && _currentCamera != null)
            {
                _currentCamera.transform.hasChanged = true;
            }
        } 

        public static int AddVolume(HazeDensityVolume densityVolume)
        {
            if (!DensityVolumes.Contains(densityVolume))
            {
                DensityVolumes.Add(densityVolume);
                UpdateVolumeGradientTexture();
                NotifyVisibilityUpdate();
                return DensityVolumes.Count - 1;
            }

            return -1;
        }

        public static void RemoveVolume(HazeDensityVolume densityVolume)
        {
            if (DensityVolumes.Contains(densityVolume))
            {
                DensityVolumes.Remove(densityVolume);
                
                // Re-assign indices
                for (var i = 0; i < DensityVolumes.Count; i++)
                {
                    DensityVolumes[i].ReassignIndex(i);
                }
                UpdateVolumeGradientTexture(false);
                NotifyVisibilityUpdate();
            }
        }

        public static void UpdateVolumeGradientTexture(bool recreate = true)
        {
            if (DensityVolumes.Count <= 0)
            {
                _volumeGradientTexture = Texture2D.whiteTexture;
                return;
            }

            if (recreate)
            {
                _volumeGradientTexture = new Texture2D(16, 2 * DensityVolumes.Count);
            }
            for (var i = 0; i < DensityVolumes.Count * 2; i += 2)
            {
                var gradient = DensityVolumes[i / 2].ColorGradient;
                for (var j = 0; j < 16; j++)
                {
                    var col = gradient.Evaluate(j / 16.0f);
                    _volumeGradientTexture.SetPixel(j, i, col);
                    _volumeGradientTexture.SetPixel(j, i + 1, col);
                }
            }
            
            _volumeGradientTexture.Apply();
            _froxelFogRenderPass?.UpdateVolumeGradientTexture(_volumeGradientTexture);
        }

#endregion

        public override void OnCameraPreCull(ScriptableRenderer renderer, in CameraData cameraData)
        {
            _currentCamera = cameraData.camera;

            if (_currentCamera.cameraType == CameraType.Preview ||
                (_excludeOverlayCameras && cameraData.renderType == CameraRenderType.Overlay))
            {
                return;
            }

            UpdateVolumeVisibility(cameraData.camera);
            AssignDensityVolumeData(cameraData.camera);

#if UNITY_EDITOR
            if (!_initialized && _currentCamera != null)
            {
                Create();
                EditorSceneManager.sceneOpened += OnSceneOpened;
                _initialized = true;
            }
#endif
        }

#if UNITY_EDITOR
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            _initialized = false;
        }
#endif

        public override void Create()
        {
            Dispose();
            //Filthy hack to get around AdditionalLightsCookieAtlasTexture not being assigned when there's no additional lights using cookies.
            //Leads to the CS breaking for an unassigned texture, even if we only use cookies on the directional light.
            Shader.SetGlobalTexture(Shader.PropertyToID("_AdditionalLightsCookieAtlasTexture"), Texture2D.whiteTexture);
            Shader.SetGlobalTexture(Shader.PropertyToID("_MainLightCookieTexture"), Texture2D.whiteTexture);

            // _currentCamera = Camera.main;

            if (_froxelFogComputeShader == null)
            {
                return;
            }

            //Force first visibility update
            if (_currentCamera != null)
            {
                _currentCamera.transform.hasChanged = true;
            }

            var aspectRatio = _aspectRatioAdjustment switch
            {
                AspectRatioAdjustment.None => 1.0f,
                _ => _currentCamera ? _currentCamera.aspect : 1f
            };

            var bufferResolution = 16 << (int)_froxelBufferResolution;
            var bufferDepth = 16 << (int)_froxelBufferDepth;
            var resolution = new int3(bufferResolution, bufferResolution, bufferDepth);
            switch (_aspectRatioAdjustment)
            {
                case AspectRatioAdjustment.Upscale:
                    if (aspectRatio > 1.0f)
                    {
                        resolution.x = (int)(resolution.x * aspectRatio);
                    }
                    else
                    {
                        resolution.y = (int)(resolution.y * math.rcp(aspectRatio));
                    }
                    break;
                case AspectRatioAdjustment.Downscale:
                    if (aspectRatio > 1.0f)
                    {
                        resolution.y = (int)(resolution.y * math.rcp(aspectRatio));
                    }
                    else
                    {
                        resolution.x = (int)(resolution.x * aspectRatio);
                    }
                    break;
                case AspectRatioAdjustment.None:
                default:
                    break;
            }

            var froxelFogPassSettings = new FroxelFogPassSettings
            {
                Resolution = resolution,
                FroxelFogRange = _froxelFogRange,
                FroxelFogComputeShader = _froxelFogComputeShader,
                NoiseTexture = _noiseTexture,
                NoiseData = _noiseData,
                TemporalAccumulationBlending = _temporalAccumulationBlending,
                MainLightShadowBias = _mainLightShadowBias,
                JitterNoiseMotion = _jitterNoiseMotion
            };
            
            _froxelFogRenderPass = new FroxelFogRenderPass(froxelFogPassSettings)
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques
            };

            if (_froxelFogCompositeShader == null)
            {
                return;
            }
            
            _froxelFogCompositePass = new FroxelFogCompositePass(_froxelFogCompositeShader, _bufferSampling, _multipleScatteringData, _interleavedGradientNoiseStrength, _depthBias)
            {
                renderPassEvent = _renderBeforeTransparents ? RenderPassEvent.BeforeRenderingTransparents : RenderPassEvent.BeforeRenderingPostProcessing
            };

            InitializeDensityVolumeData();
            UpdateVolumeGradientTexture();

            if (_bloomShader == null)
            {
                return;
            }

            _multipleScatteringPass = new MultipleScatteringPass(_bloomShader, _multipleScatteringData)
            {
                renderPassEvent = _renderBeforeTransparents ? RenderPassEvent.AfterRenderingSkybox : RenderPassEvent.AfterRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cameraData = renderingData.cameraData;
            if (cameraData.cameraType is CameraType.Preview or CameraType.Reflection || !cameraData.postProcessEnabled)
            {
                return;
            }

            if (_excludeOverlayCameras && cameraData.renderType == CameraRenderType.Overlay)
            {
                return;
            }

            var hazeGlobalFog = VolumeManager.instance.stack?.GetComponent<HazeGlobalFogVolumeComponent>();
            if ((hazeGlobalFog == null || !hazeGlobalFog.active || hazeGlobalFog.GlobalDensityMultiplier.value <= 0) &&
                _visibleDensityVolumesDictionary[cameraData.camera].Count <= 0)
            {
                return;
            }

            if (_froxelFogComputeShader == null)
            {
                return;
            }
            
            _froxelFogRenderPass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Color);

            renderer.EnqueuePass(_froxelFogRenderPass);

            var multipleScatteringIntensity = _multipleScatteringData.intensity;
                
            var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
            if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
            {
                if (hazeSettingsOverrides.MultipleScatteringIntensity.overrideState)
                {
                    multipleScatteringIntensity = hazeSettingsOverrides.MultipleScatteringIntensity.value;
                }
            }
            
            if (_multipleScatteringPass != null && multipleScatteringIntensity > 0)
            {
                renderer.EnqueuePass(_multipleScatteringPass);
            }
            
            if (_froxelFogCompositePass == null)
            {
                return;
            }

            renderer.EnqueuePass(_froxelFogCompositePass);
        }

        protected override void Dispose(bool disposing)
        {
            _froxelFogRenderPass?.Dispose();
            _froxelFogCompositePass?.Dispose();
            _multipleScatteringPass?.Dispose();

            _densityVolumeDataBuffer?.Release();
            _densityVolumeDataBuffer = null;
            
#if UNITY_EDITOR
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            _initialized = false;
#endif
        }

        private class FroxelFogRenderPass : ScriptableRenderPass
        {
            private class ScatterPassData
            {
                internal TextureHandle WriteBuffer;
                internal TextureHandle ReadBuffer;
                internal ComputeShader ComputeShader;
                internal int2 ScatterThreadGroups;
            }

            private class DensityGatherPassData
            {
                internal TextureHandle WriteBuffer;
                internal TextureHandle ReadBuffer;
                internal TextureHandle NoiseTexture;
                internal Matrix4x4 InverseViewProjectionMatrix;
                internal Matrix4x4 PrevViewProjectionMatrix;
                internal float4 ZBufferParameters;
                internal bool TemporalReprojection;
                internal BufferHandle LightDataBuffer;
                internal BufferHandle DensityVolumeDataBuffer;
                internal TextureHandle VolumeGradientTexture;
                internal FroxelFogPassSettings Settings;
                internal int3 DensityGatherThreadGroups;
                internal int VisibleDensityVolumes;
            }
            
            private GraphicsBuffer _densityVolumeDataBuffer;
            private GraphicsBuffer _lightDataBuffer;
            private int _visibleDensityVolumes;
            
            private readonly RenderTexture _colorDensityBuffer;
            private readonly RenderTexture _colorDensityHistoryBuffer;
            private readonly RenderTexture _scatterBuffer;
            
            private readonly RTHandle _noiseTextureHandle;
            private RTHandle _colorDensityBufferHandle;
            private RTHandle _scatterBufferHandle;
            private readonly RTHandle _volumeNoiseTextureHandle; 
            private RTHandle _volumeGradientTextureHandle;
            
            private readonly RenderTargetInfo _renderTargetInfo;
            private readonly int3 _densityGatherThreadGroups;
            private readonly int2 _scatterThreadGroups;

            private readonly FroxelFogPassSettings _passSetings;
            
            private readonly Dictionary<Camera, Matrix4x4> _prevViewProjectionMatrixDictionary;
            private readonly Dictionary<Camera, RTHandle> _historyBufferDictionary;
            private readonly RenderTextureDescriptor _bufferDescriptor;

            private const int DensityGatherKernelIndex = 0;
            private const int ScatterKernelIndex = 1;
            private const int MaximumVisibleLights = 128;

            public void UpdateDensityVolumeDataBuffer(in GraphicsBuffer densityVolumeDataBuffer)
            {
                _densityVolumeDataBuffer = densityVolumeDataBuffer;
            }
            
            public void UpdateVolumeGradientTexture(in Texture2D volumeGradientTexture)
            {
                _volumeGradientTextureHandle?.Release();
                _volumeGradientTextureHandle = RTHandles.Alloc(volumeGradientTexture);
            }

            public void UpdateVisibleDensityVolumeCount(int count)
            {
                _visibleDensityVolumes = count;
            }

            public FroxelFogRenderPass(FroxelFogPassSettings settings)
            {
                profilingSampler = new ProfilingSampler("Haze Froxel Fog");
                _passSetings = settings;
                _prevViewProjectionMatrixDictionary = new Dictionary<Camera, Matrix4x4>();
                _historyBufferDictionary = new Dictionary<Camera, RTHandle>();
                var resolution = _passSetings.Resolution;

                _renderTargetInfo = new RenderTargetInfo
                {
                    format = GraphicsFormat.R16G16B16A16_SFloat,
                    width = resolution.x,
                    height = resolution.y,
                    volumeDepth = resolution.z,
                    msaaSamples = 1
                };
                
                _bufferDescriptor = new RenderTextureDescriptor
                {
                    dimension = TextureDimension.Tex3D,
                    width = _passSetings.Resolution.x,
                    height = _passSetings.Resolution.y,
                    volumeDepth = _passSetings.Resolution.z,
                    graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    depthBufferBits = 0,
                    depthStencilFormat = GraphicsFormat.None,
                    colorFormat = RenderTextureFormat.ARGBHalf,
                    enableRandomWrite = true,
                    msaaSamples = 1,
                    useMipMap = false
                };

                var fallbackNoiseTexture = new Texture3D(1, 1, 1, GraphicsFormat.R16G16B16A16_SFloat,
                    TextureCreationFlags.DontInitializePixels);
                fallbackNoiseTexture.SetPixel(0,0,0,Color.white);
                fallbackNoiseTexture.Apply();

                _noiseTextureHandle = RTHandles.Alloc(settings.NoiseTexture == null ? fallbackNoiseTexture : settings.NoiseTexture);
                _volumeGradientTextureHandle = RTHandles.Alloc(Texture2D.whiteTexture);
                
                _passSetings.FroxelFogComputeShader.GetKernelThreadGroupSizes(DensityGatherKernelIndex, out var threadGroupSizesX, out var threadGroupSizesY, out var threadGroupSizesZ);
                _densityGatherThreadGroups = new int3(  Mathf.CeilToInt((float) resolution.x / threadGroupSizesX), 
                                                        Mathf.CeilToInt((float) resolution.y / threadGroupSizesY), 
                                                        Mathf.CeilToInt((float) resolution.z / threadGroupSizesZ));
                
                _passSetings.FroxelFogComputeShader.GetKernelThreadGroupSizes(ScatterKernelIndex, out threadGroupSizesX, out threadGroupSizesY, out threadGroupSizesZ);
                _scatterThreadGroups = new int2(Mathf.CeilToInt((float) resolution.x / threadGroupSizesX), 
                                                Mathf.CeilToInt((float) resolution.y / threadGroupSizesY));
                
                _lightDataBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaximumVisibleLights, sizeof(float) * 3);
            }

            private class CustomData : ContextItem
            {
                public TextureHandle IntermediateBuffer;
                
                public override void Reset()
                {
                    IntermediateBuffer = TextureHandle.nullHandle;
                }
            }
            
            private void HazeFroxelFogCommon(Camera camera, NativeArray<VisibleLight> visibleLights)
            {
                var punctualLightsAmount = 0;

                _prevViewProjectionMatrixDictionary.TryAdd(camera, Matrix4x4.identity);
                
                foreach (var light in visibleLights)
                {
                    if (light.lightType is LightType.Spot or LightType.Point)
                    {
                        punctualLightsAmount++;
                    }
                }

                punctualLightsAmount = math.min(MaximumVisibleLights, punctualLightsAmount);
                var lightDataArray = new NativeArray<float3>(1, Allocator.Temp);
                if (punctualLightsAmount > 0)
                {
                    lightDataArray.Dispose();
                    lightDataArray = new NativeArray<float3>(punctualLightsAmount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                    var counter = 0;
                    for (var i = 0; i < visibleLights.Length && counter < punctualLightsAmount; i++)
                    {
                        var visibleLight = visibleLights[i];
                        if (visibleLight.lightType is not (LightType.Spot or LightType.Point))
                        {
                            continue;
                        }
                        if (visibleLight.light.TryGetComponent<HazeLight>(out var lightData))
                        {
                            lightDataArray[counter++] = new float3(lightData.LightContribution, lightData.DensityBoost, visibleLight.light.color.a);
                        }
                        else
                        {
                            lightDataArray[counter++] = new float3(0,0, visibleLight.light.color.a);
                        }
                    }
                }
                _lightDataBuffer.SetData(lightDataArray);
                lightDataArray.Dispose();
            }

#if !UNITY_6000_4_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var camera = renderingData.cameraData.camera;
                
                RenderingUtils.ReAllocateHandleIfNeeded(ref _colorDensityBufferHandle, in _bufferDescriptor, FilterMode.Trilinear, TextureWrapMode.Clamp);
                _historyBufferDictionary.TryGetValue(camera, out var historyBuffer);
                RenderingUtils.ReAllocateHandleIfNeeded(ref historyBuffer, in _bufferDescriptor, FilterMode.Trilinear, TextureWrapMode.Clamp);
                _historyBufferDictionary.TryAdd(camera, historyBuffer);
                RenderingUtils.ReAllocateHandleIfNeeded(ref _scatterBufferHandle, in _bufferDescriptor,
                    FilterMode.Trilinear, TextureWrapMode.Clamp);
            }

            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var cameraData = renderingData.cameraData;
                var lightData = renderingData.lightData;
                
                var visibleLights = lightData.visibleLights;
                var camera = cameraData.camera;
                HazeFroxelFogCommon(camera, visibleLights);

                var cmd = CommandBufferPool.Get("HAZE Froxel Fog");
                var froxelFogRange = _passSetings.FroxelFogRange;
                var farDivNear = froxelFogRange.y / froxelFogRange.x;
                var zBufferParameters = new float4(1.0f - farDivNear, farDivNear, froxelFogRange.x, froxelFogRange.y);

                var viewMatrix = cameraData.GetViewMatrix();
                var projMatrix = Matrix4x4.Perspective(camera.GetGateFittedFieldOfView(), camera.aspect, froxelFogRange.x, froxelFogRange.y);
                var viewProjectionMatrix = projMatrix * viewMatrix;
                    
                Shader.SetGlobalFloat(VolumeNearClipPlane, froxelFogRange.x);
                Shader.SetGlobalFloat(VolumeFarClipPlane, froxelFogRange.y);
                Shader.SetGlobalMatrix(FroxelVolumeVp, viewProjectionMatrix);

                _prevViewProjectionMatrixDictionary.TryAdd(camera, Matrix4x4.identity);
                
                var froxelFogComputeShader = _passSetings.FroxelFogComputeShader;
                var temporalReprojection = Time.timeSinceLevelLoad > Time.unscaledDeltaTime;
                /*var temporalReprojection = (Application.isPlaying && cameraData.cameraType == CameraType.Game) ||
                                           (!Application.isPlaying && cameraData.cameraType == CameraType.SceneView);*/
                
                
                cmd.SetComputeVectorParam(froxelFogComputeShader, ZBufferParameters, zBufferParameters);
                cmd.SetComputeMatrixParam(froxelFogComputeShader, InverseViewProjectionMatrix, Matrix4x4.Inverse(viewProjectionMatrix));
                cmd.SetComputeMatrixParam(froxelFogComputeShader, PrevViewProjectionMatrix, _prevViewProjectionMatrixDictionary[camera]);
                
                cmd.SetComputeFloatParam(froxelFogComputeShader, TemporalReprojection, temporalReprojection ? 1 : 0);
                cmd.SetComputeFloatParam(froxelFogComputeShader, TemporalAccumulationBlending, 1.0f - _passSetings.TemporalAccumulationBlending);
                cmd.SetComputeFloatParam(froxelFogComputeShader, MainLightShadowBias, _passSetings.MainLightShadowBias);
                cmd.SetComputeFloatParam(froxelFogComputeShader, JitterNoiseMotion, _passSetings.JitterNoiseMotion ? 1 : 0);

                var noiseTiling = _passSetings.NoiseData.noiseTiling;
                var noisePanningSpeed = _passSetings.NoiseData.noisePanningSpeed;
                var noiseWeights = _passSetings.NoiseData.noiseWeights;
                
                var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
                if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
                {
                    if (hazeSettingsOverrides.NoiseTiling.overrideState)
                    {
                        noiseTiling = hazeSettingsOverrides.NoiseTiling.value;
                    }

                    if (hazeSettingsOverrides.NoisePanningSpeed.overrideState)
                    {
                        noisePanningSpeed = hazeSettingsOverrides.NoisePanningSpeed.value;
                    }

                    if (hazeSettingsOverrides.NoiseWeights.overrideState)
                    {
                        noiseWeights = hazeSettingsOverrides.NoiseWeights.value;
                    }
                }
                
                cmd.SetComputeVectorParam(froxelFogComputeShader, GlobalNoisePanningTiling, new float4(noisePanningSpeed, noiseTiling));
                cmd.SetComputeVectorParam(froxelFogComputeShader, GlobalNoiseWeights, noiseWeights);
                cmd.SetComputeFloatParam(froxelFogComputeShader, VisibleDensityVolumes, _visibleDensityVolumes);

                //Volume parameters
                var globalFogVolumeComponent = VolumeManager.instance.stack?.GetComponent<HazeGlobalFogVolumeComponent>();

                if (globalFogVolumeComponent != null && globalFogVolumeComponent.active)
                {
                    cmd.SetComputeFloatParam(froxelFogComputeShader, GlobalDensityMultiplier, globalFogVolumeComponent.GlobalDensityMultiplier.value);
                    cmd.SetComputeFloatParam(froxelFogComputeShader, GlobalDensityThreshold, globalFogVolumeComponent.GlobalDensityThreshold.value);
                    cmd.SetComputeFloatParam(froxelFogComputeShader, GlobalMainLightDensityBoost, globalFogVolumeComponent.GlobalMainLightDensityBoost.value);
                    cmd.SetComputeFloatParam(froxelFogComputeShader, GlobalSecondaryLightDensityBoost, globalFogVolumeComponent.GlobalSecondaryLightDensityBoost.value);
                    cmd.SetComputeFloatParam(froxelFogComputeShader, LightScattering, globalFogVolumeComponent.MainLightScattering.value);
                    cmd.SetComputeFloatParam(froxelFogComputeShader, GlobalAdditionalLightContribution, globalFogVolumeComponent.AdditionalLightContribution.value);
                    cmd.SetComputeFloatParam(froxelFogComputeShader, GlobalProbeVolumeContribution, globalFogVolumeComponent.ProbeVolumeContribution.value);
                    cmd.SetComputeVectorParam(froxelFogComputeShader, AmbientColor, globalFogVolumeComponent.AmbientColor.value);
                    cmd.SetComputeVectorParam(froxelFogComputeShader, GlobalMainLightContribution, globalFogVolumeComponent.MainLightContribution.value);
                    cmd.SetComputeVectorParam(froxelFogComputeShader, GlobalHeightFog, new float4(globalFogVolumeComponent.MaxFogHeight.value,
                    globalFogVolumeComponent.HeightFogSmoothness.value, globalFogVolumeComponent.HeightFogFactor.value, globalFogVolumeComponent.CameraRelativeHeightFog.value ? 1 : 0));
                }

                cmd.SetComputeTextureParam(froxelFogComputeShader, DensityGatherKernelIndex, ColorDensityBuffer, _colorDensityBufferHandle);
                // cmd.SetComputeTextureParam(froxelFogComputeShader, DensityGatherKernelIndex, ColorDensityReadBuffer, _colorDensityHistoryBufferHandle);
                cmd.SetComputeTextureParam(froxelFogComputeShader, DensityGatherKernelIndex, ColorDensityReadBuffer, _historyBufferDictionary[camera]);
                cmd.SetComputeTextureParam(froxelFogComputeShader, DensityGatherKernelIndex, GlobalNoiseTexture, _noiseTextureHandle);
                cmd.SetComputeTextureParam(froxelFogComputeShader, DensityGatherKernelIndex, VolumeGradientTexture, _volumeGradientTextureHandle);
                
                cmd.SetComputeBufferParam(froxelFogComputeShader, DensityGatherKernelIndex, HazeDensityVolumeBuffer, _densityVolumeDataBuffer);
                cmd.SetComputeBufferParam(froxelFogComputeShader, DensityGatherKernelIndex, SecondaryLightDataBuffer, _lightDataBuffer);
                
                cmd.DispatchCompute(froxelFogComputeShader, DensityGatherKernelIndex, _densityGatherThreadGroups.x, _densityGatherThreadGroups.y, _densityGatherThreadGroups.z);

                context.ExecuteCommandBuffer(cmd);
                
                (_colorDensityBufferHandle, _historyBufferDictionary[camera]) = (_historyBufferDictionary[camera], _colorDensityBufferHandle);
                // Orthographic projection doesn't use VP matrix; assign previous view matrix instead
                _prevViewProjectionMatrixDictionary[camera] = camera.orthographic ? viewMatrix : viewProjectionMatrix;
                
                cmd.Clear();
                
                cmd.SetComputeTextureParam(froxelFogComputeShader, ScatterKernelIndex, ScatterBuffer, _scatterBufferHandle);
                cmd.SetComputeTextureParam(froxelFogComputeShader, ScatterKernelIndex, ColorDensityReadBuffer, _colorDensityBufferHandle);
                cmd.DispatchCompute(froxelFogComputeShader, ScatterKernelIndex, _scatterThreadGroups.x, _scatterThreadGroups.y, 1);
                cmd.SetGlobalTexture(ScatterBuffer, _scatterBufferHandle);
                context.ExecuteCommandBuffer(cmd);
                
                CommandBufferPool.Release(cmd);
            }
#endif

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var cameraData = frameData.Get<UniversalCameraData>();
                var lightData = frameData.Get<UniversalLightData>();

                var visibleLights = lightData.visibleLights;
                var camera = cameraData.camera;
                
                HazeFroxelFogCommon(camera, visibleLights);

                _historyBufferDictionary.TryGetValue(camera, out var historyBuffer);
                RenderingUtils.ReAllocateHandleIfNeeded(ref historyBuffer, in _bufferDescriptor, FilterMode.Trilinear, TextureWrapMode.Clamp);
                _historyBufferDictionary.TryAdd(camera, historyBuffer);
                RenderingUtils.ReAllocateHandleIfNeeded(ref _colorDensityBufferHandle, in _bufferDescriptor,FilterMode.Trilinear, TextureWrapMode.Clamp);
                RenderingUtils.ReAllocateHandleIfNeeded(ref _scatterBufferHandle, in _bufferDescriptor, FilterMode.Trilinear, TextureWrapMode.Clamp);
                
                var colorDensityBuffer = renderGraph.ImportTexture(_colorDensityBufferHandle, _renderTargetInfo);
                var colorDensityHistoryBuffer = renderGraph.ImportTexture(_historyBufferDictionary[camera], _renderTargetInfo);
                
                var scatterBuffer = renderGraph.ImportTexture(_scatterBufferHandle, _renderTargetInfo);
                var noiseTexture = renderGraph.ImportTexture(_noiseTextureHandle);
                var volumeGradientTexture = renderGraph.ImportTexture(_volumeGradientTextureHandle);
                var densityVolumeDataBuffer = renderGraph.ImportBuffer(_densityVolumeDataBuffer);
                var lightDataBuffer = renderGraph.ImportBuffer(_lightDataBuffer);

                using (var builder = renderGraph.AddComputePass("Color density accumulation", out DensityGatherPassData passData, profilingSampler))
                {
                    passData.WriteBuffer = colorDensityBuffer;
                    passData.ReadBuffer = colorDensityHistoryBuffer;
                    passData.NoiseTexture = noiseTexture;
                    passData.PrevViewProjectionMatrix = _prevViewProjectionMatrixDictionary[camera];
                    passData.LightDataBuffer = lightDataBuffer;
                    passData.DensityVolumeDataBuffer = densityVolumeDataBuffer;
                    passData.VolumeGradientTexture = volumeGradientTexture;
                    passData.Settings = _passSetings;
                    passData.VisibleDensityVolumes = _visibleDensityVolumes;
                    passData.DensityGatherThreadGroups = _densityGatherThreadGroups;
                    
                    builder.UseTexture(passData.WriteBuffer, AccessFlags.Write);
                    builder.UseTexture(passData.ReadBuffer);
                    builder.UseTexture(passData.NoiseTexture);
                    builder.UseTexture(passData.VolumeGradientTexture);
                    builder.UseBuffer(passData.DensityVolumeDataBuffer);
                    builder.AllowPassCulling(false);

                    var customData = frameData.Create<CustomData>();
                    customData.IntermediateBuffer = colorDensityBuffer;

                    var froxelFogRange = _passSetings.FroxelFogRange;
                    var farDivNear = froxelFogRange.y / froxelFogRange.x;
                    var zBufferParameters = new float4(1.0f - farDivNear, farDivNear, froxelFogRange.x, froxelFogRange.y);

                    var viewMatrix = cameraData.GetViewMatrix();
                    var projMatrix = Matrix4x4.Perspective(camera.GetGateFittedFieldOfView(), camera.aspect, froxelFogRange.x, froxelFogRange.y);
                    var viewProjectionMatrix = projMatrix * viewMatrix;
                    
                    Shader.SetGlobalFloat(VolumeNearClipPlane, froxelFogRange.x);
                    Shader.SetGlobalFloat(VolumeFarClipPlane, froxelFogRange.y);
                    Shader.SetGlobalMatrix(FroxelVolumeVp, viewProjectionMatrix);

                    passData.InverseViewProjectionMatrix = Matrix4x4.Inverse(viewProjectionMatrix);
                    passData.ZBufferParameters = zBufferParameters;
                    passData.TemporalReprojection = Time.timeSinceLevelLoad > Time.unscaledDeltaTime;
                    
                    builder.SetRenderFunc((DensityGatherPassData data, ComputeGraphContext context) => ExecuteDensityGatherPass(data, context));

                    (_colorDensityBufferHandle, _historyBufferDictionary[camera]) = (_historyBufferDictionary[camera], _colorDensityBufferHandle);
                    // Orthographic projection doesn't use VP matrix; assign previous view matrix instead
                    _prevViewProjectionMatrixDictionary[camera] = camera.orthographic ? viewMatrix : viewProjectionMatrix;
                }

                using (var builder = renderGraph.AddComputePass("Scatter", out ScatterPassData passData, profilingSampler))
                {
                    var customData = frameData.Get<CustomData>();
                    passData.WriteBuffer = scatterBuffer;
                    passData.ReadBuffer = customData.IntermediateBuffer;
                    passData.ComputeShader = _passSetings.FroxelFogComputeShader;
                    passData.ScatterThreadGroups = _scatterThreadGroups.xy;

                    customData.IntermediateBuffer = scatterBuffer;
                    builder.UseTexture(passData.WriteBuffer, AccessFlags.Write);
                    builder.UseTexture(passData.ReadBuffer);
                    builder.AllowPassCulling(false);
                    
                    builder.SetGlobalTextureAfterPass(passData.WriteBuffer, ScatterBuffer);
                    
                    builder.SetRenderFunc((ScatterPassData data, ComputeGraphContext context) =>
                    {
                        ExecuteScatterPass(context, data);
                    });
                }
            }
            
            private static void ExecuteDensityGatherPass(DensityGatherPassData data, ComputeGraphContext context)
            {
                var settings = data.Settings;
                context.cmd.SetComputeVectorParam(settings.FroxelFogComputeShader, ZBufferParameters, data.ZBufferParameters);
                context.cmd.SetComputeMatrixParam(settings.FroxelFogComputeShader, InverseViewProjectionMatrix, data.InverseViewProjectionMatrix);
                context.cmd.SetComputeMatrixParam(settings.FroxelFogComputeShader, PrevViewProjectionMatrix, data.PrevViewProjectionMatrix);
                
                context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, TemporalReprojection, data.TemporalReprojection ? 1 : 0);
                context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, TemporalAccumulationBlending, 1.0f - settings.TemporalAccumulationBlending);
                context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, MainLightShadowBias, settings.MainLightShadowBias);
                context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, JitterNoiseMotion, settings.JitterNoiseMotion ? 1 : 0);

                var noiseTiling = settings.NoiseData.noiseTiling;
                var noisePanningSpeed = settings.NoiseData.noisePanningSpeed;
                var noiseWeights = settings.NoiseData.noiseWeights;
                
                var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
                if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
                {
                    if (hazeSettingsOverrides.NoiseTiling.overrideState)
                    {
                        noiseTiling = hazeSettingsOverrides.NoiseTiling.value;
                    }

                    if (hazeSettingsOverrides.NoisePanningSpeed.overrideState)
                    {
                        noisePanningSpeed = hazeSettingsOverrides.NoisePanningSpeed.value;
                    }

                    if (hazeSettingsOverrides.NoiseWeights.overrideState)
                    {
                        noiseWeights = hazeSettingsOverrides.NoiseWeights.value;
                    }
                }
                
                context.cmd.SetComputeVectorParam(settings.FroxelFogComputeShader, GlobalNoisePanningTiling, new float4(noisePanningSpeed, noiseTiling));
                context.cmd.SetComputeVectorParam(settings.FroxelFogComputeShader, GlobalNoiseWeights, noiseWeights);
                context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, VisibleDensityVolumes, data.VisibleDensityVolumes);

                //Volume parameters
                var globalFogVolumeComponent = VolumeManager.instance.stack?.GetComponent<HazeGlobalFogVolumeComponent>();

                if (globalFogVolumeComponent != null && globalFogVolumeComponent.active)
                {
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, GlobalDensityMultiplier, globalFogVolumeComponent.GlobalDensityMultiplier.value);
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, GlobalDensityThreshold, globalFogVolumeComponent.GlobalDensityThreshold.value);
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, GlobalMainLightDensityBoost, globalFogVolumeComponent.GlobalMainLightDensityBoost.value);
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, GlobalSecondaryLightDensityBoost, globalFogVolumeComponent.GlobalSecondaryLightDensityBoost.value);
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, LightScattering, globalFogVolumeComponent.MainLightScattering.value);
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, GlobalAdditionalLightContribution, globalFogVolumeComponent.AdditionalLightContribution.value);
                    context.cmd.SetComputeFloatParam(settings.FroxelFogComputeShader, GlobalProbeVolumeContribution, globalFogVolumeComponent.ProbeVolumeContribution.value);
                    context.cmd.SetComputeVectorParam(settings.FroxelFogComputeShader, AmbientColor, globalFogVolumeComponent.AmbientColor.value);
                    context.cmd.SetComputeVectorParam(settings.FroxelFogComputeShader, GlobalMainLightContribution, globalFogVolumeComponent.MainLightContribution.value);
                    context.cmd.SetComputeVectorParam(settings.FroxelFogComputeShader, GlobalHeightFog, new float4(globalFogVolumeComponent.MaxFogHeight.value,
                    globalFogVolumeComponent.HeightFogSmoothness.value, globalFogVolumeComponent.HeightFogFactor.value, globalFogVolumeComponent.CameraRelativeHeightFog.value ? 1 : 0));
                }

                context.cmd.SetComputeTextureParam(settings.FroxelFogComputeShader, DensityGatherKernelIndex, ColorDensityBuffer, data.WriteBuffer);
                context.cmd.SetComputeTextureParam(settings.FroxelFogComputeShader, DensityGatherKernelIndex, ColorDensityReadBuffer, data.ReadBuffer);
                context.cmd.SetComputeTextureParam(settings.FroxelFogComputeShader, DensityGatherKernelIndex, GlobalNoiseTexture, data.NoiseTexture);
                context.cmd.SetComputeTextureParam(settings.FroxelFogComputeShader, DensityGatherKernelIndex, VolumeGradientTexture, data.VolumeGradientTexture);
                
                context.cmd.SetComputeBufferParam(settings.FroxelFogComputeShader, DensityGatherKernelIndex, HazeDensityVolumeBuffer, data.DensityVolumeDataBuffer);
                context.cmd.SetComputeBufferParam(settings.FroxelFogComputeShader, DensityGatherKernelIndex, SecondaryLightDataBuffer, data.LightDataBuffer);
                
                context.cmd.DispatchCompute(settings.FroxelFogComputeShader, DensityGatherKernelIndex, data.DensityGatherThreadGroups.x, data.DensityGatherThreadGroups.y, data.DensityGatherThreadGroups.z);
            }

            private static void ExecuteScatterPass(ComputeGraphContext context, ScatterPassData data)
            {
                context.cmd.SetComputeTextureParam(data.ComputeShader, ScatterKernelIndex, ScatterBuffer, data.WriteBuffer);
                context.cmd.SetComputeTextureParam(data.ComputeShader, ScatterKernelIndex, ColorDensityReadBuffer, data.ReadBuffer);
                context.cmd.DispatchCompute(data.ComputeShader, ScatterKernelIndex, data.ScatterThreadGroups.x, data.ScatterThreadGroups.y, 1);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_colorDensityBuffer);
                CoreUtils.Destroy(_scatterBuffer);
                
                _colorDensityBufferHandle?.Release();
                _scatterBufferHandle?.Release();
                _noiseTextureHandle?.Release();

                foreach (var buffer in _historyBufferDictionary.Values)
                {
                    buffer?.Release();
                }
                _lightDataBuffer?.Release();
                _lightDataBuffer = null;
            }
        }

        private class FroxelFogCompositePass : ScriptableRenderPass
        {
            private readonly Material _froxelFogCompositeMaterial;
            private readonly BufferSampling _bufferSampling;
            private readonly float _interleavedGradientNoiseStrength;
            private readonly MultipleScatteringData _multipleScatteringData;
            private readonly float _depthBias;
            private RTHandle _copiedColor;

            private class BlitPassData
            {
                internal TextureHandle Source;
                internal TextureHandle Destination;
                internal Material BlitMaterial;
                internal int ShaderPass;
            }

            public FroxelFogCompositePass(Shader froxelFogCompositeShader, BufferSampling bufferSampling, MultipleScatteringData multipleScatteringData, float interleavedGradientNoiseStrength, float depthBias)
            {
                _froxelFogCompositeMaterial = CoreUtils.CreateEngineMaterial(froxelFogCompositeShader);
                _bufferSampling = bufferSampling;
                _multipleScatteringData = multipleScatteringData;
                _interleavedGradientNoiseStrength = interleavedGradientNoiseStrength;
                _depthBias = depthBias;
            }
            
            private void CompositePassCommon()
            {
                switch (_bufferSampling)
                {
                    case BufferSampling.Tricubic:
                        _froxelFogCompositeMaterial.EnableKeyword("TRICUBIC_SAMPLING");
                        break;
                    case BufferSampling.Point:
                        _froxelFogCompositeMaterial.EnableKeyword("POINT_SAMPLING");
                        break;
                    case BufferSampling.Trilinear:
                    default:
                        _froxelFogCompositeMaterial.EnableKeyword("TRILINEAR_SAMPLING");
                        break;
                }

                Shader.SetGlobalFloat(IgnStrength, _interleavedGradientNoiseStrength);
                Shader.SetGlobalFloat(HazeDepthBias, _depthBias);
                
                var multipleScatteringIntensity = _multipleScatteringData.intensity;
                var multipleScatteringRadius = _multipleScatteringData.radius;
                
                var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
                if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
                {
                    if (hazeSettingsOverrides.MultipleScatteringIntensity.overrideState)
                    {
                        multipleScatteringIntensity = hazeSettingsOverrides.MultipleScatteringIntensity.value;
                    }

                    if (hazeSettingsOverrides.MultipleScatteringRadius.overrideState)
                    {
                        multipleScatteringRadius = hazeSettingsOverrides.MultipleScatteringRadius.value;
                    }
                }
                
                Shader.SetGlobalFloat(BloomIntensity, multipleScatteringIntensity);
                Shader.SetGlobalFloat(BlurRadius, multipleScatteringRadius);
            }
#if !UNITY_6000_4_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                RenderingUtils.ReAllocateIfNeeded(ref _copiedColor, descriptor);
            }

            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var renderer = renderingData.cameraData.renderer;
                var cameraColorTargetHandle = renderer.cameraColorTargetHandle;
                
                CompositePassCommon();

                var cmd = CommandBufferPool.Get("Composite pass");
                
                Blit(cmd, cameraColorTargetHandle, _copiedColor, _froxelFogCompositeMaterial);
                Blit(cmd, _copiedColor, cameraColorTargetHandle);
                
                context.ExecuteCommandBuffer(cmd);
                
                CommandBufferPool.Release(cmd);
            }
#endif
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var srcCamColor = resourceData.activeColorTexture;
                var textureDesc = srcCamColor.GetDescriptor(renderGraph);

                textureDesc.depthBufferBits = 0;
                
                var copiedTexture = renderGraph.CreateTexture(textureDesc);
                CompositePassCommon();

                using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("Froxel fog composite/Blit", out var passData))
                {
                    passData.Source = srcCamColor;
                    passData.Destination = copiedTexture;
                    passData.BlitMaterial = _froxelFogCompositeMaterial;
                    passData.ShaderPass = 0;
                    
                    builder.SetInputAttachment(passData.Source, 0);
                    builder.SetRenderAttachment(passData.Destination, 0);
                    builder.AllowPassCulling(false);
                    
                    builder.SetRenderFunc((BlitPassData data, RasterGraphContext context) => ExecuteBlitPass(data, context));
                }

                resourceData.cameraColor = copiedTexture;
            }
            
            private static void ExecuteBlitPass(BlitPassData data, RasterGraphContext context)
            {
                Blitter.BlitTexture(context.cmd, data.Source, new Vector4(1,1,0,0), data.BlitMaterial, data.ShaderPass);
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_froxelFogCompositeMaterial);
                _copiedColor?.Release();
            }
        }

        private class MultipleScatteringPass : ScriptableRenderPass
        {
            private readonly Material _ssmsMaterial;
            private readonly float _scatter;
            private readonly float _threshold;
            private readonly int _maxIterations;
            private readonly ProfilingSampler _prefilterSampler = new("Prefilter");
            private readonly ProfilingSampler _downscaleSampler = new ("Downscale");
            private readonly ProfilingSampler _upscaleSampler = new ("Upscale");

            private RTHandle[] _upscaleHandles;
            private RTHandle[] _downscaleHandles;

            private class PassData
            {
                internal Material BloomMaterial;
                internal TextureHandle ColorTexture;
                internal NativeArray<TextureHandle> DownsampleBuffers;
                internal NativeArray<TextureHandle> UpsampleBuffers;
                internal ProfilingSampler PrefilterSampler;
                internal ProfilingSampler DownscaleSampler;
                internal ProfilingSampler UpscaleSampler;
                internal int MipCount;
            }
            
            public MultipleScatteringPass(Shader bloomShader, MultipleScatteringData multipleScatteringData)
            {
                _ssmsMaterial = CoreUtils.CreateEngineMaterial(bloomShader);
                _scatter = multipleScatteringData.scatter;
                _threshold = multipleScatteringData.threshold;
                _maxIterations = multipleScatteringData.maxIterations;
            }

#if !UNITY_6000_4_OR_NEWER
            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                var width = descriptor.width;
                var height = descriptor.height;
                var maxIterations = _maxIterations;
                
                var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
                if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
                {
                    if (hazeSettingsOverrides.MaxMultipleScatteringIterations.overrideState)
                    {
                        maxIterations = hazeSettingsOverrides.MaxMultipleScatteringIterations.value;
                    }
                }
                
                var maxSize = Mathf.Max(width, height);
                var iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
                var mipCount = Mathf.Clamp(iterations, 1, maxIterations);

                if (_downscaleHandles == null || _downscaleHandles.Length != mipCount)
                {
                    _downscaleHandles = new RTHandle[mipCount];
                }
                
                if (_upscaleHandles == null || _upscaleHandles.Length != mipCount)
                {
                    _upscaleHandles = new RTHandle[mipCount];
                }
                
                RenderingUtils.ReAllocateIfNeeded(ref _downscaleHandles[0], descriptor);
                RenderingUtils.ReAllocateIfNeeded(ref _upscaleHandles[0], descriptor);
                
                for (var i = 1; i < mipCount; i++)
                {
                    width = math.max(1, width >> 1);
                    height = math.max(1, height >> 1);
                    descriptor.width = width;
                    descriptor.height = height;
                    RenderingUtils.ReAllocateIfNeeded(ref _downscaleHandles[i], descriptor);
                    RenderingUtils.ReAllocateIfNeeded(ref _upscaleHandles[i], descriptor);
                } 
            }

            [Obsolete("This rendering path is for compatibility mode only (when Render Graph is disabled). Use Render Graph API instead.", false)]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var renderer = renderingData.cameraData.renderer;
                var srcCamColor = renderer.cameraColorTargetHandle;

                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                var width = descriptor.width;
                var height = descriptor.height;

                var scatter = _scatter;
                var threshold = _threshold;
                var maxIterations = _maxIterations;
                
                var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
                if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
                {
                    if (hazeSettingsOverrides.MultipleScatteringScatter.overrideState)
                    {
                        scatter = hazeSettingsOverrides.MultipleScatteringScatter.value;
                    }

                    if (hazeSettingsOverrides.MultipleScatteringThreshold.overrideState)
                    {
                        threshold = hazeSettingsOverrides.MultipleScatteringThreshold.value;
                    }

                    if (hazeSettingsOverrides.MaxMultipleScatteringIterations.overrideState)
                    {
                        maxIterations = hazeSettingsOverrides.MaxMultipleScatteringIterations.value;
                    }
                }
                
                var maxSize = Mathf.Max(width, height);
                var iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
                var mipCount = Mathf.Clamp(iterations, 1, maxIterations);

                var thresholdValue = Mathf.GammaToLinearSpace(threshold);
                var thresholdKnee = thresholdValue * 0.5f;
                var bloomParameters = new float4(scatter, 65472, thresholdValue, thresholdKnee);
                _ssmsMaterial.SetVector(BloomParams, bloomParameters);
                _ssmsMaterial.SetFloat(SampleScale, 0.5f + math.log2(height) - (int)math.log2(height));
                
                var loadAction = RenderBufferLoadAction.DontCare;
                var storeAction = RenderBufferStoreAction.Store;

                var cmd = CommandBufferPool.Get("SSMS Bloom");
                
                using (new ProfilingScope(cmd, _prefilterSampler))
                {
                    Blitter.BlitCameraTexture(cmd, srcCamColor, _downscaleHandles[0], loadAction, storeAction, _ssmsMaterial, 0);
                }

                using (new ProfilingScope(cmd, _downscaleSampler))
                {
                    var last = _downscaleHandles[0];
                            
                    for (var i = 1; i < mipCount; i++)
                    {
                        var mipDown = _downscaleHandles[i];
                        var mipUp = _upscaleHandles[i];
                                
                        Blitter.BlitCameraTexture(cmd, last, mipUp, loadAction, storeAction, _ssmsMaterial, 1);
                        Blitter.BlitCameraTexture(cmd, mipUp, mipDown, loadAction, storeAction, _ssmsMaterial, 2);

                        last = mipDown;
                    }
                }

                using (new ProfilingScope(cmd, _upscaleSampler))
                {
                    for (var i = mipCount - 2; i >= 0; i--)
                    {
                        var lowMip = (i == mipCount - 2) ? _downscaleHandles[i + 1] : _upscaleHandles[i + 1];
                        var highMip = _downscaleHandles[i];
                        var mipUp = _upscaleHandles[i];
                                
                        cmd.SetGlobalTexture(SourceTexLowMip, lowMip);
                        Blitter.BlitCameraTexture(cmd, highMip, mipUp, loadAction, storeAction, _ssmsMaterial, 3);
                    }
                }
                
                cmd.SetGlobalTexture(GlobalBloomTexture, _upscaleHandles[0]);
                
                context.ExecuteCommandBuffer(cmd);
                
                CommandBufferPool.Release(cmd);
            }
#endif

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resourceData = frameData.Get<UniversalResourceData>();
                var srcCamColor = resourceData.activeColorTexture;
                var textureDesc = srcCamColor.GetDescriptor(renderGraph);

                textureDesc.depthBufferBits = 0;
                textureDesc.msaaSamples = MSAASamples.None;

                var width = textureDesc.width;
                var height = textureDesc.height;
                var scatter = _scatter;
                var threshold = _threshold;
                var maxIterations = _maxIterations;
                
                var hazeSettingsOverrides = VolumeManager.instance.stack?.GetComponent<HazeOverridesVolumeComponent>();
                if (hazeSettingsOverrides != null && hazeSettingsOverrides.IsActive())
                {
                    if (hazeSettingsOverrides.MultipleScatteringScatter.overrideState)
                    {
                        scatter = hazeSettingsOverrides.MultipleScatteringScatter.value;
                    }

                    if (hazeSettingsOverrides.MultipleScatteringThreshold.overrideState)
                    {
                        threshold = hazeSettingsOverrides.MultipleScatteringThreshold.value;
                    }

                    if (hazeSettingsOverrides.MaxMultipleScatteringIterations.overrideState)
                    {
                        maxIterations = hazeSettingsOverrides.MaxMultipleScatteringIterations.value;
                    }
                }
                
                var maxSize = Mathf.Max(width, height);
                var iterations = Mathf.FloorToInt(Mathf.Log(maxSize, 2f) - 1);
                var mipCount = Mathf.Clamp(iterations, 1, maxIterations);
                
                var downsampleBuffers = new NativeArray<TextureHandle>(mipCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                var upsampleBuffers = new NativeArray<TextureHandle>(mipCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);

                textureDesc.clearBuffer = false;

                var thresholdValue = Mathf.GammaToLinearSpace(threshold);
                var thresholdKnee = thresholdValue * 0.5f;
                var bloomParameters = new float4(scatter, 65472, thresholdValue, thresholdKnee);
                _ssmsMaterial.SetVector(BloomParams, bloomParameters);
                _ssmsMaterial.SetFloat(SampleScale, 0.5f + math.log2(height) - (int)math.log2(height));

                textureDesc.name = "DownscaledBuffer_0";
                downsampleBuffers[0] = renderGraph.CreateTexture(textureDesc);
                textureDesc.name = "UpscaledBuffer_0";
                upsampleBuffers[0] = renderGraph.CreateTexture(textureDesc);
                
                for (var i = 1; i < mipCount; i++)
                {
                    width = math.max(1, width >> 1);
                    height = math.max(1, height >> 1);
                    textureDesc.width = width;
                    textureDesc.height = height;
                    textureDesc.name = "DownscaledBuffer";
                    downsampleBuffers[i] = renderGraph.CreateTexture(textureDesc);
                    textureDesc.name = "UpscaledBuffer";
                    upsampleBuffers[i] = renderGraph.CreateTexture(textureDesc);
                }

                using (var builder = renderGraph.AddUnsafePass<PassData>("SSMS Bloom", out var passData))
                {
                    passData.BloomMaterial = _ssmsMaterial;
                    passData.DownsampleBuffers = downsampleBuffers;
                    passData.UpsampleBuffers = upsampleBuffers;
                    passData.MipCount = mipCount;
                    passData.PrefilterSampler = _prefilterSampler;
                    passData.DownscaleSampler = _downscaleSampler;
                    passData.UpscaleSampler = _upscaleSampler;
                    
                    passData.ColorTexture = srcCamColor;
                    
                    builder.UseTexture(passData.ColorTexture);
                    for (var i = 0; i < mipCount; i++)
                    {
                        builder.UseTexture(passData.DownsampleBuffers[i], AccessFlags.ReadWrite);
                        builder.UseTexture(passData.UpsampleBuffers[i], AccessFlags.ReadWrite);
                    }

                    builder.AllowPassCulling(false);
                    builder.SetGlobalTextureAfterPass(passData.UpsampleBuffers[0], GlobalBloomTexture);
                    
                    builder.SetRenderFunc((PassData data, UnsafeGraphContext context) =>
                    {
                        ExecuteMultipleScatteringPass(context, data);
                    });
                }

                downsampleBuffers.Dispose();
                upsampleBuffers.Dispose();
            }

            private static void ExecuteMultipleScatteringPass(UnsafeGraphContext context, PassData data)
            {
                var unsafeCmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                        
                var loadAction = RenderBufferLoadAction.DontCare;
                var storeAction = RenderBufferStoreAction.Store;

                using (new ProfilingScope(unsafeCmd, data.PrefilterSampler))
                {
                    Blitter.BlitCameraTexture(unsafeCmd, data.ColorTexture, data.DownsampleBuffers[0], loadAction, storeAction, data.BloomMaterial, 0);
                }

                using (new ProfilingScope(unsafeCmd, data.DownscaleSampler))
                {
                    var last = data.DownsampleBuffers[0];
                            
                    for (var i = 1; i < data.MipCount; i++)
                    {
                        var mipDown = data.DownsampleBuffers[i];
                        var mipUp = data.UpsampleBuffers[i];
                                
                        Blitter.BlitCameraTexture(unsafeCmd, last, mipUp, loadAction, storeAction, data.BloomMaterial, 1);
                        Blitter.BlitCameraTexture(unsafeCmd, mipUp, mipDown, loadAction, storeAction, data.BloomMaterial, 2);

                        last = mipDown;
                    }
                }

                using (new ProfilingScope(unsafeCmd, data.UpscaleSampler))
                {
                    for (var i = data.MipCount - 2; i >= 0; i--)
                    {
                        var lowMip = (i == data.MipCount - 2) ? data.DownsampleBuffers[i + 1] : data.UpsampleBuffers[i + 1];
                        var highMip = data.DownsampleBuffers[i];
                        var mipUp = data.UpsampleBuffers[i];
                                
                        unsafeCmd.SetGlobalTexture(SourceTexLowMip, lowMip);
                        Blitter.BlitCameraTexture(unsafeCmd, highMip, mipUp, loadAction, storeAction, data.BloomMaterial, 3);
                    }
                }
            }

            public void Dispose()
            {
                CoreUtils.Destroy(_ssmsMaterial);
                if (_downscaleHandles != null)
                {
                    foreach (var downscaleHandle in _downscaleHandles)
                    {
                        downscaleHandle?.Release();
                    }
                }

                if (_upscaleHandles != null)
                {
                    foreach (var upscaleHandle in _upscaleHandles)
                    {
                        upscaleHandle?.Release();
                    }
                }
            }
        }
    }
}
