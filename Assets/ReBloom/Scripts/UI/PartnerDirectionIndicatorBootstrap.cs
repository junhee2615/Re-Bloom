using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 XR/Main Camera가 런타임에 준비된 뒤,
/// PartnerDirectionCanvas를 자동 생성하고 연결하는 로컬 전용 Bootstrap.
/// 기존 네트워크/플레이어/XR 구조는 건드리지 않는다.
/// </summary>
public class PartnerDirectionIndicatorBootstrap : MonoBehaviour
{
    private const string CanvasName = "PartnerDirectionCanvas";
    private const string RootName = "PartnerIndicator";
    private const string ArrowName = "Arrow";
    private const string DistanceTextName = "DistanceText";

    [Header("Development Test")]
    [SerializeField] private bool testMode;
    [SerializeField] private Transform testTarget;

    private bool initialized;

    private void Start()
    {
        TryInitialize();
    }

    private void Update()
    {
        if (!initialized)
            TryInitialize();
    }

    private void TryInitialize()
    {
        if (initialized)
            return;

        Camera localCamera = Camera.main;

        if (localCamera == null)
        {
            HardwareRig rig = FindFirstObjectByType<HardwareRig>();
            if (rig != null && rig.headTransform != null)
                localCamera = rig.headTransform.GetComponentInChildren<Camera>(true);
        }

        if (localCamera == null)
            return;

        CreateOrReconnectUi(localCamera);
        initialized = true;
    }

    private void CreateOrReconnectUi(Camera localCamera)
    {
        GameObject canvasObject = localCamera.transform.Find(CanvasName)?.gameObject;
        if (canvasObject == null)
            canvasObject = CreateCanvas(localCamera);

        GameObject indicatorRoot = canvasObject.transform.Find(RootName)?.gameObject;
        if (indicatorRoot == null)
            indicatorRoot = CreateIndicatorRoot(canvasObject.transform);

        GameObject arrowObject = indicatorRoot.transform.Find(ArrowName)?.gameObject;
        if (arrowObject == null)
            arrowObject = CreateArrow(indicatorRoot.transform);

        GameObject distanceTextObject = indicatorRoot.transform.Find(DistanceTextName)?.gameObject;
        if (distanceTextObject == null)
            distanceTextObject = CreateDistanceText(indicatorRoot.transform);

        PartnerDirectionIndicator indicator = canvasObject.GetComponent<PartnerDirectionIndicator>();
        if (indicator == null)
            indicator = canvasObject.AddComponent<PartnerDirectionIndicator>();

        indicator.SetLocalCamera(localCamera);
        indicator.SetIndicatorRoot(indicatorRoot.GetComponent<RectTransform>());
        indicator.SetArrowRectTransform(arrowObject.GetComponent<RectTransform>());
        indicator.SetDistanceText(distanceTextObject.GetComponent<TMP_Text>());

        ApplyDeveloperTestFlags(indicator);
    }

    private void ApplyDeveloperTestFlags(PartnerDirectionIndicator indicator)
    {
        if (indicator == null)
            return;

        SetPrivateField(indicator, "testMode", testMode);
        SetPrivateField(indicator, "testTarget", testTarget);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        if (target == null)
            return;

        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (field == null)
            return;

        field.SetValue(target, value);
    }

    private static GameObject CreateCanvas(Camera localCamera)
    {
        GameObject canvasObject = new GameObject(CanvasName, typeof(RectTransform));
        canvasObject.transform.SetParent(localCamera.transform, false);

        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = localCamera;
        canvas.planeDistance = 0.5f;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        GraphicRaycaster raycaster = canvasObject.AddComponent<GraphicRaycaster>();
        raycaster.ignoreReversedGraphics = true;
        raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        return canvasObject;
    }

    private static GameObject CreateIndicatorRoot(Transform parent)
    {
        GameObject root = new GameObject(RootName, typeof(RectTransform));
        root.transform.SetParent(parent, false);

        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(220f, 120f);
        rect.anchoredPosition = new Vector2(0f, 0f);

        return root;
    }

    private static GameObject CreateArrow(Transform parent)
    {
        GameObject arrowObject = new GameObject(ArrowName, typeof(RectTransform));
        arrowObject.transform.SetParent(parent, false);

        Image image = arrowObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = new Color(1f, 0.9f, 0.45f, 1f);

        RectTransform rect = arrowObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(90f, 90f);
        rect.anchoredPosition = Vector2.zero;

        return arrowObject;
    }

    private static GameObject CreateDistanceText(Transform parent)
    {
        GameObject textObject = new GameObject(DistanceTextName, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);

        TMP_Text tmp = textObject.AddComponent<TextMeshProUGUI>();
        tmp.text = "10.0 m";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 42;
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
}
