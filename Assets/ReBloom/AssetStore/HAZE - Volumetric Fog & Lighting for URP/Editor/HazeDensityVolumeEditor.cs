using System;
using Haze.Runtime;
using UnityEditor;

namespace Haze.Editor
{
    [CustomEditor(typeof(HazeDensityVolume)), CanEditMultipleObjects]
    public class HazeDensityVolumeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var volume = (HazeDensityVolume)target;
            var densityVolumeMode = volume.DensityMode;
            var serializedProperty = serializedObject.GetIterator();
            var isSubtractive = densityVolumeMode == HazeDensityVolume.VolumeDensityMode.Subtractive;
            serializedProperty.NextVisible(true);
            
            for (var i = 0; i < 5; i++)
            {
                serializedProperty.NextVisible(false);
                EditorGUILayout.PropertyField(serializedProperty, true);
            }
            
            while (serializedProperty.NextVisible(false))
            {
                if (isSubtractive && !serializedProperty.name.Contains("Height", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (volume.GradientMappingMethod != HazeDensityVolume.GradientMapping.MainLightDirection &&
                    string.Compare(serializedProperty.name, "_gradientLightScattering", StringComparison.Ordinal) == 0)
                {
                    continue;
                }
                EditorGUILayout.PropertyField(serializedProperty, true);
            }

            if (isSubtractive)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Lighting", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("_secondaryLightDensityBoost"));
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
