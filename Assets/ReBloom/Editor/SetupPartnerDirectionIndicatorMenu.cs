using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public static class SetupPartnerDirectionIndicatorMenu
{
    private const string CanvasName = "PartnerDirectionCanvas";
    private const string RootName = "PartnerIndicator";
    private const string ArrowName = "Arrow";
    private const string DistanceTextName = "DistanceText";

    [MenuItem("ReBloom/Setup Partner Indicator", priority = 1000)]
    public static void SetupPartnerIndicator()
    {
        Camera localCamera = GetLocalCamera();
        if (localCamera == null)
        {
            Debug.LogError("[ReBloom] Main Camera를 찾을 수 없습니다. Scene에 Main Camera가 있는지 확인하세요.");
            return;
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        GameObject canvasObject = FindOrCreateCanvas(localCamera);
        GameObject indicatorRoot = FindOrCreateIndicatorRoot(canvasObject.transform);
        GameObject arrowObject = FindOrCreateArrow(indicatorRoot.transform);
        GameObject distanceTextObject = FindOrCreateDistanceText(indicatorRoot.transform);

        PartnerDirectionIndicator runtimeComponent = canvasObject.GetComponent<PartnerDirectionIndicator>();
        if (runtimeComponent == null)
            runtimeComponent = Undo.AddComponent<PartnerDirectionIndicator>(canvasObject);

        Undo.RecordObject(runtimeComponent, "Assign Partner Direction Indicator references");
        runtimeComponent.SetLocalCamera(localCamera);
        runtimeComponent.SetIndicatorRoot(indicatorRoot.GetComponent<RectTransform>());
        runtimeComponent.SetArrowRectTransform(arrowObject.GetComponent<RectTransform>());
        runtimeComponent.SetDistanceText(distanceTextObject.GetComponent<TMP_Text>());

        Undo.CollapseUndoOperations(undoGroup);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = canvasObject;

        Debug.Log("[ReBloom] Partner Direction Indicator UI setup complete.");
    }

    private static Camera GetLocalCamera()
    {
        if (Camera.main != null)
            return Camera.main;

        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera camera in cameras)
        {
            if (camera.CompareTag("MainCamera"))
                return camera;
        }

        return null;
    }

    private static GameObject FindOrCreateCanvas(Camera parentCamera)
    {
        Transform existing = parentCamera.transform.Find(CanvasName);
        if (existing != null)
        {
            GameObject existingCanvas = existing.gameObject;
            EnsureCanvasComponents(existingCanvas);
            return existingCanvas;
        }

        GameObject newCanvas = new GameObject(CanvasName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(newCanvas, "Create Partner Direction Canvas");
        newCanvas.transform.SetParent(parentCamera.transform, false);

        Canvas canvas = Undo.AddComponent<Canvas>(newCanvas);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = parentCamera;
        canvas.planeDistance = 0.01f;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = Undo.AddComponent<CanvasScaler>(newCanvas);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = Undo.AddComponent<GraphicRaycaster>(newCanvas);
        raycaster.ignoreReversedGraphics = true;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        RectTransform rect = (RectTransform)newCanvas.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return newCanvas;
    }

    private static void EnsureCanvasComponents(GameObject canvasGo)
    {
        Undo.RecordObject(canvasGo, "Ensure Partner Direction Canvas");

        Canvas canvas = canvasGo.GetComponent<Canvas>();
        if (canvas == null)
            canvas = Undo.AddComponent<Canvas>(canvasGo);

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = canvasGo.transform.parent != null ? canvasGo.transform.parent.GetComponent<Camera>() : null;
        canvas.planeDistance = 0.01f;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
        if (scaler == null)
            scaler = Undo.AddComponent<CanvasScaler>(canvasGo);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = canvasGo.GetComponent<GraphicRaycaster>();
        if (raycaster == null)
            raycaster = Undo.AddComponent<GraphicRaycaster>(canvasGo);
        raycaster.ignoreReversedGraphics = true;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        RectTransform rect = canvasGo.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static GameObject FindOrCreateIndicatorRoot(Transform parent)
    {
        Transform existing = parent.Find(RootName);
        if (existing != null)
            return existing.gameObject;

        GameObject root = new GameObject(RootName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Create Partner Indicator Root");
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(220f, 120f);
        rect.anchoredPosition = new Vector2(0f, -80f);

        return root;
    }

    private static GameObject FindOrCreateArrow(Transform parent)
    {
        Transform existing = parent.Find(ArrowName);
        if (existing != null)
            return existing.gameObject;

        GameObject arrowObject = new GameObject(ArrowName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(arrowObject, "Create Partner Indicator Arrow");
        arrowObject.transform.SetParent(parent, false);

        Image image = Undo.AddComponent<Image>(arrowObject);
        image.raycastTarget = false;
        image.color = Color.white;
        image.sprite = CreateTriangleArrowSprite();
        image.preserveAspect = true;

        RectTransform rect = arrowObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(90f, 90f);
        rect.anchoredPosition = Vector2.zero;

        return arrowObject;
    }

    private static GameObject FindOrCreateDistanceText(Transform parent)
    {
        Transform existing = parent.Find(DistanceTextName);
        if (existing != null)
            return existing.gameObject;

        GameObject textObject = new GameObject(DistanceTextName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(textObject, "Create Partner Indicator Distance Text");
        textObject.transform.SetParent(parent, false);

        TMP_Text tmp = Undo.AddComponent<TextMeshProUGUI>(textObject);
        tmp.text = "10.0 m";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 26;
        tmp.enableAutoSizing = false;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        tmp.fontStyle = FontStyles.Bold;

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(180f, 50f);
        rect.anchoredPosition = new Vector2(0f, -55f);

        return textObject;
    }

    private static Sprite CreateTriangleArrowSprite()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        Color transparent = new Color(0f, 0f, 0f, 0f);
        Color solid = new Color(1f, 1f, 1f, 1f);

        Color[] pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = transparent;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = y * size + x;
                float nx = (x - size * 0.5f) / (size * 0.5f);
                float ny = (y - size * 0.5f) / (size * 0.5f);

                bool inside = PointInTriangle(nx, ny, -0.8f, -0.8f, 0.8f, -0.8f, 0.0f, 0.9f);
                if (inside)
                    pixels[index] = solid;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
    }

    private static bool PointInTriangle(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
    {
        float area = Mathf.Abs((ax * (by - cy) + bx * (cy - ay) + cx * (ay - by)) / 2f);
        if (area < 0.0001f)
            return false;

        float s = (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by)) / 2f;
        float s1 = (px * (ay - cy) + ax * (cy - py) + cx * (py - ay)) / 2f;
        float s2 = (px * (cy - by) + cx * (by - py) + bx * (py - cy)) / 2f;

        float signedArea = (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));

        if (signedArea < 0f)
        {
            s = -s;
            s1 = -s1;
            s2 = -s2;
        }

        return s1 >= 0f && s2 >= 0f && (s1 + s2) <= Mathf.Abs(s) + 0.0001f;
    }
}
