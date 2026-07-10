using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

namespace Haze.Runtime
{
    [VolumeComponentMenu("Haze/Haze Settings Overrides")]
    [VolumeRequiresRendererFeatures(typeof(HazeRendererFeature))]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    [DisplayInfo(name = "Haze Settings Overrides")]
    public class HazeOverridesVolumeComponent : VolumeComponent, IPostProcessComponent
    {
        [Header("Noise Settings")]
        [Tooltip("Determines the tiling of the 3D noise. Increase for higher frequency noise.")]
        [SerializeField] private MinFloatParameter _noiseTiling = new(0.001f, 0.001f);
        [Tooltip("Determines how fast the 3D noise will move in each of the XYZ axes.")]
        [SerializeField] private Vector3Parameter _noisePanningSpeed = new(Vector3.zero);
        [Tooltip("Determines the contribution of each of the 4 color channels of the 3D noise.")]
        [SerializeField] private Vector4Parameter _noiseWeights = new(Vector4.one);
        
        [Header("Multiple scattering settings")]
        [Tooltip("Determines the blend between regular fog and fog with screen-space multiple scattering. Set to 0 to completely disable multiple scattering.")]
        [SerializeField] private ClampedFloatParameter _multipleScatteringIntensity = new(1, 0, 1);
        [Tooltip("Determines the brightness of the blurred image that gets composited with the fog. The lower the value, the brighter the result.")]
        [SerializeField] private MinFloatParameter _multipleScatteringRadius = new(7, 0.01f);
        [Tooltip("Determines the blur amount of the multiple scattering buffer. A lower value will make the blur effect less intense/")]
        [SerializeField] private ClampedFloatParameter _multipleScatteringScatter = new(1, 0, 1);
        [Tooltip("Determines the brightness threshold for the multiple scattering pre-filtering. A value of 0 does no filtering, blurring the whole image, while a larger value will only blur brighter parts of the image.")]
        [SerializeField] private ClampedFloatParameter _multipleScatteringThreshold = new(0, 0, 1);
        [FormerlySerializedAs("_maxmultipleScatteringIterations")]
        [Tooltip("Determines the maximum number of blur iterations for the multiple scattering effect. A larger amount of iterations will result in a increased blurring distance, but it will increase performance overhead.")]
        [SerializeField] private ClampedIntParameter _maxMultipleScatteringIterations = new(5, 3, 10);
        
        public FloatParameter NoiseTiling => _noiseTiling;
        public Vector3Parameter NoisePanningSpeed => _noisePanningSpeed;
        public Vector4Parameter NoiseWeights => _noiseWeights;
        public ClampedFloatParameter MultipleScatteringIntensity => _multipleScatteringIntensity;
        public MinFloatParameter MultipleScatteringRadius => _multipleScatteringRadius;
        public ClampedFloatParameter MultipleScatteringScatter => _multipleScatteringScatter;
        public ClampedFloatParameter MultipleScatteringThreshold => _multipleScatteringThreshold;
        public ClampedIntParameter MaxMultipleScatteringIterations => _maxMultipleScatteringIterations;
    
        public bool IsActive()
        {
            return active;
        }
    }
}
