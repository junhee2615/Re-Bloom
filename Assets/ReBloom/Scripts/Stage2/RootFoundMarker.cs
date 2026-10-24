using UnityEngine;

/// <summary>
/// 뿌리를 찾았을 때 그루터기 위에 떠 있는 클리어 표시.
/// LivingRoot 가 런타임에 만들어 붙인다. 항상 카메라를 바라보고,
/// 톡 튀어나오듯 커진 뒤 위아래로 살짝 떠다닌다.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class RootFoundMarker : MonoBehaviour
{
    private const float PopDuration = 0.35f;
    private const float HideDuration = 0.25f;
    private const float BobAmplitude = 0.04f;
    private const float BobSpeed = 2f;

    private SpriteRenderer spriteRenderer;
    private Vector3 basePosition;
    private float targetScale = 1f;
    private float shownTime;
    private float hideStartTime = -1f;
    private Camera cachedCamera;

    public void Setup(Sprite sprite, float worldSize, Vector3 worldPosition)
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sprite = sprite;

        float spriteWorld = sprite != null ? Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y) : 1f;
        float parentScale = transform.parent != null ? transform.parent.lossyScale.x : 1f;
        targetScale = worldSize / Mathf.Max(0.0001f, spriteWorld * parentScale);

        transform.position = worldPosition;
        basePosition = transform.localPosition;
    }

    public void Show()
    {
        gameObject.SetActive(true);
        hideStartTime = -1f;
        shownTime = Time.time;
        transform.localScale = Vector3.zero;
    }

    public void Hide()
    {
        if (!gameObject.activeSelf || hideStartTime >= 0f)
            return;
        hideStartTime = Time.time;
    }

    private void LateUpdate()
    {
        float scale;
        if (hideStartTime >= 0f)
        {
            float k = (Time.time - hideStartTime) / HideDuration;
            if (k >= 1f)
            {
                gameObject.SetActive(false);
                return;
            }
            scale = targetScale * (1f - k * k);
        }
        else
        {
            float k = Mathf.Clamp01((Time.time - shownTime) / PopDuration);
            scale = targetScale * EaseOutBack(k);
        }
        transform.localScale = Vector3.one * scale;

        // 위아래로 살짝 떠다닌다
        transform.localPosition = basePosition
            + Vector3.up * (Mathf.Sin((Time.time - shownTime) * BobSpeed * Mathf.PI) * BobAmplitude / Mathf.Max(0.0001f, ParentScale()));

        // 카메라를 바라본다
        if (cachedCamera == null)
            cachedCamera = Camera.main;
        if (cachedCamera != null)
        {
            Vector3 toMarker = transform.position - cachedCamera.transform.position;
            if (toMarker.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(toMarker, Vector3.up);
        }
    }

    private float ParentScale()
    {
        return transform.parent != null ? transform.parent.lossyScale.y : 1f;
    }

    // 살짝 넘쳤다가 돌아오는 팝 느낌
    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        float t = x - 1f;
        return 1f + c3 * t * t * t + c1 * t * t;
    }
}
