using Haze.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Haze.Editor
{
    public static class HazeDensityVolumeCreator
    {
        [MenuItem("GameObject/Haze/Density Volume")]
        public static void CreateHazeDensityVolume(MenuCommand menuCommand)
        {
            var volumeObject = new GameObject("HazeDensityVolume", typeof(HazeDensityVolume))
            {
                isStatic = true
            };

            GameObjectUtility.SetParentAndAlign(volumeObject, menuCommand.context as GameObject);
            
            StageUtility.PlaceGameObjectInCurrentStage(volumeObject);
            GameObjectUtility.EnsureUniqueNameForSibling(volumeObject);
            
            Undo.RegisterCreatedObjectUndo(volumeObject, "Created Haze Density Volume");
            Selection.activeObject = volumeObject;

            if (!Application.isPlaying)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }
        }
    }
}
