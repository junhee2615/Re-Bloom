#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ConfigureStage3TeleporterCutscene
{
    private const string ScenePath =
        "Assets/ReBloom/Scenes/Stage3.unity";
    private const string ManagerName =
        "Stage3TeleporterCutsceneManager";
    private const string TeleporterName =
        "BuildingToWaterfrontTeleporter";
    private const string ClipPath =
        "Assets/ReBloom/Sounds/Stage2/Stage2_TeleporterActivate.mp3";

    [MenuItem("ReBloom/Setup/Configure Stage3 Teleporter Cutscene")]
    public static void Configure()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() ||
            scene.path != ScenePath)
        {
            scene = EditorSceneManager.OpenScene(
                ScenePath,
                OpenSceneMode.Single);
        }

        Stage3TeleporterCutscene manager =
            FindObjectByName<Stage3TeleporterCutscene>(ManagerName);
        if (manager == null)
        {
            Debug.LogError(
                "[Stage3TeleporterSetup] " +
                "Stage3TeleporterCutsceneManager를 찾지 못했습니다.");
            return;
        }

        Transform teleporter =
            FindTransformByName(TeleporterName);
        if (teleporter == null)
        {
            Debug.LogError(
                "[Stage3TeleporterSetup] " +
                "BuildingToWaterfrontTeleporter를 찾지 못했습니다.");
            return;
        }

        GameObject fireCircle =
            FindChildGameObject(teleporter, "Fire Circle");
        GameObject splash =
            FindChildGameObject(teleporter, "Splash");

        if (fireCircle == null || splash == null)
        {
            Debug.LogError(
                "[Stage3TeleporterSetup] 대상 텔레포터에서 " +
                "Fire Circle 또는 Splash를 찾지 못했습니다.");
            return;
        }

        fireCircle.SetActive(false);
        splash.SetActive(false);

        AudioClip clip =
            AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);

        SerializedObject serialized =
            new SerializedObject(manager);

        SetObjectField(
            serialized,
            "teleporterCutsceneShot",
            FindTransformByName("Stage3TeleporterCutsceneShot"));
        SetObjectField(
            serialized,
            "teleporterActivateClip",
            clip);
        SetFloatField(
            serialized,
            "teleporterActivateVolume",
            0.4f);
        SetFloatField(
            serialized,
            "fadeDuration",
            0.6f);
        SetFloatField(
            serialized,
            "cameraMoveDistance",
            0f);
        SetFloatField(
            serialized,
            "cameraMoveDuration",
            4f);
        SetFloatField(
            serialized,
            "viewHoldDuration",
            2f);
        SetBoolField(
            serialized,
            "smoothCameraMovement",
            true);
        SetBoolField(
            serialized,
            "followYaw",
            true);
        SetBoolField(
            serialized,
            "followPitch",
            false);
        SetBoolField(
            serialized,
            "disableResonance",
            true);
        SetBoolField(
            serialized,
            "disableBuiltinFog",
            true);
        SetBoolField(
            serialized,
            "hideRemoteAvatars",
            true);
        SetBoolField(
            serialized,
            "lockLocomotion",
            true);
        SetBoolField(
            serialized,
            "hideTutorialCanvas",
            true);

        SerializedProperty vfx =
            serialized.FindProperty("teleporterVfxObjects");
        vfx.arraySize = 2;
        vfx.GetArrayElementAtIndex(0)
            .objectReferenceValue = fireCircle;
        vfx.GetArrayElementAtIndex(1)
            .objectReferenceValue = splash;

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log(
            "[Stage3TeleporterSetup] " +
            "BuildingToWaterfrontTeleporter의 Fire Circle/Splash를 연결했습니다.",
            manager);
    }

    private static T FindObjectByName<T>(string name)
        where T : Component
    {
        T[] objects =
            Object.FindObjectsByType<T>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

        foreach (T obj in objects)
        {
            if (obj != null && obj.name == name)
                return obj;
        }

        return null;
    }

    private static Transform FindTransformByName(string name)
    {
        GameObject[] roots =
            SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject root in roots)
        {
            Transform found = FindTransform(root.transform, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static Transform FindTransform(
        Transform parent,
        string name)
    {
        if (parent.name == name)
            return parent;

        foreach (Transform child in parent)
        {
            Transform found = FindTransform(child, name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static GameObject FindChildGameObject(
        Transform parent,
        string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(
                     true))
        {
            if (child.name == name)
                return child.gameObject;
        }

        return null;
    }

    private static void SetObjectField(
        SerializedObject serialized,
        string propertyName,
        Object value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    private static void SetFloatField(
        SerializedObject serialized,
        string propertyName,
        float value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    private static void SetBoolField(
        SerializedObject serialized,
        string propertyName,
        bool value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }
}
#endif
