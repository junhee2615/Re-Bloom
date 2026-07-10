using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Haze.Runtime
{
    [VolumeComponentMenu("Haze/Haze Global Fog")]
    [VolumeRequiresRendererFeatures(typeof(HazeRendererFeature))]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [DisplayInfo(name = "Haze Global Fog")]
    public sealed class HazeGlobalFogVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        [Header("Density")]
        [Tooltip("Determines the density of the global fog.")]
        [SerializeField] private MinFloatParameter _globalDensityMultiplier = new(0, 0);
        [Tooltip("Determines the threshold at which the 3D noise will cut away the density of the global fog.")]
        [SerializeField] private FloatParameter _globalDensityThreshold = new(0.2f);
        [Header("Color")]
        [Tooltip("The main color of the global fog.")]
        [SerializeField] private ColorParameter _ambientColor = new(Color.white, true, false, true);
        [Tooltip("The additional color that gets multiplied by the sun light. Increase the HDR intensity for more intense sun rays.")]
        [SerializeField] private ColorParameter _mainLightContribution = new(Color.white, true, false, true);

        [Header("Height fog")]
        [Tooltip("The factor by which the fog is reduced based on height.")]
        [SerializeField] private MinFloatParameter _heightFogFactor = new(0, 0);
        [Tooltip("The maximum height of the global fog.")]
        [SerializeField] private FloatParameter _maxFogHeight = new(0);
        [Tooltip("The smoothness of the global height fog threshold. Values close to 0 will make the height threshold sharper, while negative values will invert the height fog.")]
        [SerializeField] private FloatParameter _heightFogSmoothness = new(0);
        [Tooltip("Makes the height threshold relative to the camera's position instead of world-space height.")]
        [SerializeField] private BoolParameter _cameraRelativeHeightFog = new(false);

        [Header("Lighting")]
        [Tooltip("Only available in Forward+; determines how much additional lights contribute to the color of the global fog.")]
        [SerializeField] private MinFloatParameter _additionalLightContribution = new(0, 0);
        [Tooltip("Determines how much adaptive probe volume illumination contributes to the final color of the global fog.")]
        [SerializeField] private MinFloatParameter _probeVolumeContribution = new(0, 0);
        [Tooltip("The main light scattering amount; values closer to 1 make the main light scatter more into the global fog.")]
        [SerializeField] private ClampedFloatParameter _mainLightScattering = new(1, 0, 1);
        [FormerlySerializedAs("_globalLightDensityBoost")]
        [Tooltip("Increases the density in non-shadow areas; used to enhance the effect of sun rays coming in from the shadows.")]
        [SerializeField] private MinFloatParameter _globalMainLightDensityBoost = new(0, 0);
        [Tooltip("Increases the density of fog based on secondary light attenuation. Use light color alpha value to adjust the density boost per-light.")]
        [SerializeField] private MinFloatParameter _globalSecondaryLightDensityBoost = new(0, 0);
        
        public MinFloatParameter GlobalDensityMultiplier 
        {
            get => _globalDensityMultiplier;
            set =>  _globalDensityMultiplier = value;
        }
        public FloatParameter GlobalDensityThreshold 
        {
            get => _globalDensityThreshold;
            set =>  _globalDensityThreshold = value;
        }
        public ColorParameter AmbientColor 
        {
            get => _ambientColor;
            set =>  _ambientColor = value;
        }
        public ColorParameter MainLightContribution 
        {
            get => _mainLightContribution;
            set =>  _mainLightContribution = value;
        }
        public MinFloatParameter HeightFogFactor 
        {
            get => _heightFogFactor;
            set =>  _heightFogFactor = value;
        }
        public FloatParameter MaxFogHeight 
        {
            get => _maxFogHeight;
            set =>  _maxFogHeight = value;
        }
        public FloatParameter HeightFogSmoothness 
        {
            get => _heightFogSmoothness;
            set =>  _heightFogSmoothness = value;
        }
        public BoolParameter CameraRelativeHeightFog 
        {
            get => _cameraRelativeHeightFog;
            set =>  _cameraRelativeHeightFog = value;
        }
        public MinFloatParameter AdditionalLightContribution 
        {
            get => _additionalLightContribution;
            set =>  _additionalLightContribution = value;
        }
        public MinFloatParameter ProbeVolumeContribution 
        {
            get => _probeVolumeContribution;
            set =>  _probeVolumeContribution = value;
        }
        public ClampedFloatParameter MainLightScattering 
        {
            get => _mainLightScattering;
            set =>  _mainLightScattering = value;
        }
        public MinFloatParameter GlobalMainLightDensityBoost 
        {
            get => _globalMainLightDensityBoost;
            set =>  _globalMainLightDensityBoost = value;
        }
        public MinFloatParameter GlobalSecondaryLightDensityBoost 
        {
            get => _globalSecondaryLightDensityBoost;
            set =>  _globalSecondaryLightDensityBoost = value;
        }

        public bool IsActive()
        {
            return active;
        }
    }
}
