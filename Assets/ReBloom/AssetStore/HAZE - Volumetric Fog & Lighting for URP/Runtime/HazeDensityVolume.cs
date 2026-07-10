using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using static Haze.Runtime.HazeRendererFeature;

namespace Haze.Runtime
{
   [ExecuteAlways]
   public class HazeDensityVolume : MonoBehaviour
   {
      public enum Shape
      {
         Cube,
         Sphere
      }

      public enum VolumeDensityMode
      {
         Additive = 0,
         Subtractive = 1,
         Override = 2
      }
      
      public enum GradientMapping
      {
         LocalZ = 0,
         LocalY = 1,
         LocalX = 2,
         LocalZY = 3,
         ScreenSpaceX = 4,
         ScreenSpaceY = 5,
         MainLightDirection = 6
      }
      
      [Tooltip("The shape of the density volume.")]
      [SerializeField] private Shape _shape = Shape.Cube;

      [Tooltip("The overall weight of all density linked to this volume. Used to easily fade volumes in and out.")]
      [SerializeField, Range(0,1)] private float _weight = 1;
      [Tooltip("Rendering priority to sort volumes in case override volumes are used.")]
      [SerializeField] private int _priority = 0;

      [Header("Density")]
      [Tooltip("Determines the density of the fog inside the volume.")]
      [SerializeField, Min(0)] private float _density = 1.0f;
      [Tooltip("Determines the threshold at which the 3D noise will cut away the density of the fog inside the volume.")]
      [SerializeField] private float _noiseThreshold = 0.5f;
      [FormerlySerializedAs("volumeDensityMode")]
      [Tooltip("Determines how the volume interacts with the fog density. Default behavior is additive, while subtractive mode can be used to remove fog from the area inside the volume.")]
      [SerializeField] private VolumeDensityMode _densityMode = VolumeDensityMode.Additive;
      
      [Header("Color")]
      [Tooltip("The main color of the fog inside the volume.")]
      [SerializeField, ColorUsage(false, true)] private Color _ambientColor = Color.white;
      [Tooltip("The color gradient the volume uses to modify the fog's ambient color")]
      [SerializeField] private Gradient _colorGradient = new();

      [Tooltip("The method of mapping the gradient color.")]
      [SerializeField] private GradientMapping _gradientMapping = GradientMapping.LocalZ;
      [Tooltip("Gradient light scattering value when \"Main Light Direction\" mapping method is selected.")]
      [SerializeField, Range(0, 0.9999f)] private float _gradientLightScattering = 0.5f;
      [Tooltip("The additional color that gets multiplied by the sun light. Increase the HDR intensity for more intense sun rays.")]
      [SerializeField, ColorUsage(false, true)] private Color _mainLightContribution = Color.white;
      
      [Header("Height fog")]
      [Tooltip("The factor by which the fog is reduced based on height.")]
      [SerializeField, Min(0)] private float _heightFogFactor = 0;
      [Tooltip("The maximum height of the fog inside the volume.")]
      [SerializeField] private float _maxFogHeight = 0;
      [Tooltip("The smoothness of the height fog threshold. Values close to 0 will make the height threshold sharper, while negative values will invert the height fog.")]
      [SerializeField] private float _heightFogSmoothness = 0.1f;
      [Tooltip("The mode of the height fog. \"Global\" makes the height fog work based on world-space height. \"Local\" makes the height fog work relative to the volume, in which case the max height would range from 0 to 1. \"Camera Relative\" mode makes the maximum height of the fog be relative to the camera's position on the world-space Y axis.")]
      [SerializeField] private HeightFogMode _heightFogMode = HeightFogMode.Global;
      
      [Header("Lighting")]
      [Tooltip("Only available in Forward+; determines how much additional lights contribute to the color of the fog inside the volume.")]
      [SerializeField, Min(0)] private float _additionalLightContribution = 1;
      [Tooltip("Determines how much adaptive probe volume illumination contributes to the final color of the fog inside the volume.")]
      [SerializeField, Min(0)] private float _probeVolumeContribution = 0;
      [FormerlySerializedAs("_mainLightPhase")]
      [Tooltip("The main light scattering amount; values closer to 1 make the main light scatter more into the fog inside the volume.")]
      [SerializeField, Range(0, 1)] private float _mainLightScattering = 1;
      [FormerlySerializedAs("_lightDensityBoost")]
      [Tooltip("Increases the density in non-shadow areas; used to enhance the effect of sun rays coming in from the shadows.")]
      [SerializeField, Min(0)] private float _mainLightDensityBoost = 0.0f;
      [Tooltip("Increases the density of fog based on secondary light attenuation. Use light color alpha value to adjust the density boost per-light.")]
      [SerializeField, Min(0)] private float _secondaryLightDensityBoost = 0.0f;
      
      private Bounds _bounds;
      private HazeDensityVolumeData _densityVolumeData;
      private int _volumeIndex;

      public Shape VolumeShape
      {
         get  => _shape;
         set => _shape = value;
      }

      public float Weight
      {
         get => _weight;
         set => _weight = value;
      }

      public int Priority
      {
         get => _priority;
         set => _priority = value;
      }

      public float Density
      {
         get  => _densityMode == VolumeDensityMode.Subtractive ? math.min(-0.01f, -_density * _weight) : _density * _weight;
         set => _density = value;
      }

      public float NoiseThreshold
      {
         get => _noiseThreshold;
         set => _noiseThreshold = value;
      }

      public VolumeDensityMode DensityMode
      {
         get => _densityMode;
         set => _densityMode = value;
      }

      public Color AmbientColor
      {
         get => _densityMode == VolumeDensityMode.Subtractive ? Color.black : _ambientColor;
         set => _ambientColor = value;
      }

      public Gradient ColorGradient
      {
         get => _colorGradient;
         set => _colorGradient = value;
      }

      public GradientMapping GradientMappingMethod {
        get => _gradientMapping;
        set => _gradientMapping = value;
      }
      public float GradientLightScattering {
        get => _gradientLightScattering;
        set => _gradientLightScattering = value;
      }
      public Color MainLightContribution {
        get => _densityMode == VolumeDensityMode.Subtractive ? Color.black : _mainLightContribution;
        set => _mainLightContribution = value;
      }
      public float HeightFogFactor {
        get => _heightFogFactor;
        set => _heightFogFactor = value;
      }
      public float MaxFogHeight {
        get => _maxFogHeight;
        set => _maxFogHeight = value;
      }
      public float HeightFogSmoothness {
        get => _heightFogSmoothness;
        set => _heightFogSmoothness = value;
      }
      public HeightFogMode VolumeHeightFogMode {
        get => _heightFogMode;
        set => _heightFogMode = value;
      }
      public float4x4 WorldToLocal => transform.worldToLocalMatrix;
      public HazeDensityVolumeData DensityVolumeData {
        get => _densityVolumeData;
        set => _densityVolumeData = value;
      }
      public float AdditionalLightContribution {
        get => _densityMode == VolumeDensityMode.Subtractive ?  0 : _additionalLightContribution;
        set => _additionalLightContribution = value;
      }
      public float ProbeVolumeContribution {
        get => _densityMode == VolumeDensityMode.Subtractive ?  0 : _probeVolumeContribution;
        set => _probeVolumeContribution = value;
      }
      public float MainLightScattering {
        get => _densityMode == VolumeDensityMode.Subtractive ?  1 : _mainLightScattering;
        set => _mainLightScattering = value;
      }
      public float MainLightDensityBoost {
        get => _densityMode == VolumeDensityMode.Subtractive ?  0 : _mainLightDensityBoost * _weight;
        set => _mainLightDensityBoost = value;
      }
      public float SecondaryLightDensityBoost {
        get => _secondaryLightDensityBoost * _weight;
        set => _secondaryLightDensityBoost = value;
      }

      public int VolumeIndex => _volumeIndex;
      public Bounds VolumeBounds => _bounds;

      private void Update()
      {
         if (Application.isPlaying && gameObject.isStatic)
         {
            return;
         }
         
         UpdateData();
      }

      private void UpdateData()
      {
         CalculateBounds();
         _densityVolumeData.SetData(this);
      }

      private void CalculateBounds()
      {
         _bounds = new Bounds(transform.position, Vector3.zero);
         _bounds.Encapsulate(transform.TransformPoint(new float3(-0.5f, -0.5f, -0.5f)));
         _bounds.Encapsulate(transform.TransformPoint(new float3(-0.5f, -0.5f,  0.5f)));
         _bounds.Encapsulate(transform.TransformPoint(new float3(-0.5f,  0.5f, -0.5f)));
         _bounds.Encapsulate(transform.TransformPoint(new float3(-0.5f,  0.5f,  0.5f)));
            
         _bounds.Encapsulate(transform.TransformPoint(new float3( 0.5f, -0.5f, -0.5f)));
         _bounds.Encapsulate(transform.TransformPoint(new float3( 0.5f, -0.5f,  0.5f)));
         _bounds.Encapsulate(transform.TransformPoint(new float3( 0.5f,  0.5f, -0.5f)));
         _bounds.Encapsulate(transform.TransformPoint(new float3( 0.5f,  0.5f,  0.5f)));
      }

      public bool IsWithinCameraFrustum(Plane[] cameraPlanes)
      {
         return _density == 0 || GeometryUtility.TestPlanesAABB(cameraPlanes, _bounds);
      }
              
      public bool IsWithinRange(Vector3 position, float distance)
      {
         return _bounds.Contains(position) || Vector3.Distance(_bounds.ClosestPoint(position), position) < distance;
      }

      public void ReassignIndex(int index)
      {
         _volumeIndex = index;
         _densityVolumeData.SetData(this);
      }

      private void OnValidate()
      {
         if (!Application.isPlaying)
         {
            UpdateVolumeGradientTexture();
         }
      }

      private void OnEnable()
      {
         _densityVolumeData = new HazeDensityVolumeData();
         _volumeIndex = AddVolume(this);
         UpdateData();
      }

      private void OnDisable()
      {
         RemoveVolume(this);
      }

      private void OnDrawGizmosSelected()
      {
         Gizmos.matrix = transform.localToWorldMatrix;
         if (_shape == Shape.Cube)
         {
            Gizmos.DrawWireCube(float3.zero, Vector3.one);
         }
         else
         {
            Gizmos.DrawWireSphere(float3.zero, 0.5f);
         }
      }
   }

}
