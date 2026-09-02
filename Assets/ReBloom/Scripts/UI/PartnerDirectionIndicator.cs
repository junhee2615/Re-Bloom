using UnityEngine;
using TMPro;

/// <summary>
/// 로컬 클라이언트 화면에만 표시되는 상대 플레이어 방향/거리 표시기.
/// 기존 Photon Fusion 네트워크 구조를 건드리지 않고, NetworkPlayer.All 기준으로
/// 현재 로컬 플레이어를 제외한 상대를 찾아서 카메라 기준 방향을 계산한다.
/// </summary>
public class PartnerDirectionIndicator : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera localCamera;
    [SerializeField] private RectTransform indicatorRoot;
    [SerializeField] private RectTransform arrowRectTransform;
    [SerializeField] private TMP_Text distanceText;
    [SerializeField] private CanvasGroup indicatorCanvasGroup;

    [Header("Settings")]
    [SerializeField] private float hideDistance = 2.5f;
    [SerializeField] private float maxArrowRotation = 50f;

    [Header("Display")]
    [SerializeField] private float verticalOffset = 0f;
    [SerializeField] private float arrowScale = 0.7f;
    [SerializeField] private float textOffsetY = 70f;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("Development Test")]
    [SerializeField] private bool testMode;
    [SerializeField] private Transform testTarget;

    private const float VerySmallValue = 0.001f;
    private bool shouldBeVisible;

    public void SetLocalCamera(Camera camera)
    {
        localCamera = camera;
    }

    public void SetIndicatorRoot(RectTransform root)
    {
        indicatorRoot = root;
    }

    public void SetArrowRectTransform(RectTransform arrow)
    {
        arrowRectTransform = arrow;
    }

    public void SetDistanceText(TMP_Text text)
    {
        distanceText = text;
    }

    private void Start()
    {
        ResolveLocalCamera();
        if (indicatorRoot != null)
        {
            if (indicatorCanvasGroup == null)
                indicatorCanvasGroup = indicatorRoot.GetComponent<CanvasGroup>();

            if (indicatorCanvasGroup == null)
                indicatorCanvasGroup = indicatorRoot.gameObject.AddComponent<CanvasGroup>();

            indicatorRoot.gameObject.SetActive(true);
            indicatorCanvasGroup.alpha = 0f;
            indicatorCanvasGroup.interactable = false;
            indicatorCanvasGroup.blocksRaycasts = false;
        }
    }

    private void Update()
    {
        UpdateFade();
        ResolveLocalCamera();

        if (localCamera == null || indicatorRoot == null || arrowRectTransform == null)
            return;

        if (testMode)
        {
            if (testTarget == null)
            {
                SetIndicatorVisible(false);
                return;
            }

            Vector3 toRemote = testTarget.position - localCamera.transform.position;
            float distance = toRemote.magnitude;

            if (distance <= hideDistance)
            {
                SetIndicatorVisible(false);
                return;
            }

            SetIndicatorVisible(true);

            if (distanceText != null)
                distanceText.text = distance.ToString("F1") + "m";

            UpdateIndicatorPosition(toRemote, distance);
            return;
        }

        NetworkPlayer remotePlayer = FindRemotePlayer();
        if (remotePlayer == null || remotePlayer.PlayerTransform == null)
        {
            SetIndicatorVisible(false);
            return;
        }

        Vector3 toRemoteFromNetwork = remotePlayer.PlayerTransform.position - localCamera.transform.position;
        float distanceFromNetwork = toRemoteFromNetwork.magnitude;

        if (distanceFromNetwork <= hideDistance)
        {
            SetIndicatorVisible(false);
            return;
        }

        SetIndicatorVisible(true);

        if (distanceText != null)
            distanceText.text = distanceFromNetwork.ToString("F1") + "m";

        UpdateIndicatorPosition(toRemoteFromNetwork, distanceFromNetwork);
    }

    private void SetIndicatorVisible(bool visible)
    {
        shouldBeVisible = visible;

        if (indicatorRoot != null && !indicatorRoot.gameObject.activeSelf)
            indicatorRoot.gameObject.SetActive(true);

        if (indicatorCanvasGroup != null && visible)
        {
            indicatorCanvasGroup.interactable = true;
            indicatorCanvasGroup.blocksRaycasts = true;
        }
    }

    private void UpdateFade()
    {
        if (indicatorRoot == null)
            return;

        if (indicatorCanvasGroup == null)
            indicatorCanvasGroup = indicatorRoot.GetComponent<CanvasGroup>();

        if (indicatorCanvasGroup == null)
            indicatorCanvasGroup = indicatorRoot.gameObject.AddComponent<CanvasGroup>();

        float duration = Mathf.Max(fadeDuration, VerySmallValue);
        float targetAlpha = shouldBeVisible ? 1f : 0f;
        indicatorCanvasGroup.alpha = Mathf.MoveTowards(
            indicatorCanvasGroup.alpha,
            targetAlpha,
            Time.deltaTime / duration);

        if (!shouldBeVisible && indicatorCanvasGroup.alpha <= 0f)
        {
            indicatorCanvasGroup.interactable = false;
            indicatorCanvasGroup.blocksRaycasts = false;
        }
    }

    private void ResolveLocalCamera()
    {
        if (localCamera != null)
            return;

        localCamera = Camera.main;

        if (localCamera == null)
        {
            HardwareRig rig = FindFirstObjectByType<HardwareRig>();
            if (rig != null && rig.headTransform != null)
            {
                Camera cam = rig.headTransform.GetComponentInChildren<Camera>(true);
                if (cam != null)
                    localCamera = cam;
            }
        }
    }

    private NetworkPlayer FindRemotePlayer()
    {
        foreach (var player in NetworkPlayer.All)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            if (player.IsLocalNetworkRig)
                continue;

            if (player.PlayerTransform == null)
                continue;

            return player;
        }

        return null;
    }

    private void UpdateIndicatorPosition(Vector3 toRemote, float distance)
    {
        Vector3 localDirection = localCamera.transform.InverseTransformDirection(toRemote);
        float yaw = Mathf.Atan2(localDirection.x, Mathf.Max(localDirection.z, VerySmallValue)) * Mathf.Rad2Deg;
        float clampedYaw = Mathf.Clamp(yaw, -90f, 90f);

        float directionT = Mathf.InverseLerp(-90f, 90f, clampedYaw);
        directionT = Mathf.SmoothStep(0f, 1f, directionT);

        if (indicatorRoot != null)
            indicatorRoot.anchoredPosition = new Vector2(0f, verticalOffset);

        if (arrowRectTransform != null)
        {
            Vector2 min = arrowRectTransform.anchorMin;
            Vector2 max = arrowRectTransform.anchorMax;
            float anchorX = Mathf.Lerp(0f, 1f, directionT);
            min.x = anchorX;
            max.x = anchorX;
            arrowRectTransform.anchorMin = min;
            arrowRectTransform.anchorMax = max;

            arrowRectTransform.anchoredPosition = Vector2.zero;
            arrowRectTransform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            float finalRotation = clampedYaw;
            arrowRectTransform.localRotation = Quaternion.Euler(0f, 0f, -finalRotation);
        }

        if (distanceText != null)
        {
            RectTransform textRect = distanceText.rectTransform;
            textRect.anchoredPosition = new Vector2(0f, -textOffsetY);
            textRect.localRotation = Quaternion.identity;

            Vector3 horizontalDirection = Vector3.ProjectOnPlane(toRemote, localCamera.transform.up);
            if (horizontalDirection.sqrMagnitude < VerySmallValue)
            {
                if (arrowRectTransform != null)
                    arrowRectTransform.localRotation = Quaternion.identity;
            }
        }
    }
}
