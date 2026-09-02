using System.Collections;
using UnityEngine;

public class Stage1VRFlash : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Flash Settings")]
    [SerializeField] private float fadeInDuration = 0.15f;
    [SerializeField] private float holdDuration = 0.05f;
    [SerializeField] private float fadeOutDuration = 0.25f;

    private Coroutine flashCoroutine;

    private void Awake()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;
    }

    public void PlayFlash()
    {
        if (canvasGroup == null)
        {
            Debug.LogWarning(
                "[Stage1VRFlash] CanvasGroup이 연결되지 않았습니다.",
                this);

            return;
        }

        if (flashCoroutine != null)
            StopCoroutine(flashCoroutine);

        flashCoroutine = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        // 투명 → 흰색
        yield return FadeAlpha(
            canvasGroup.alpha,
            1f,
            fadeInDuration);

        // 잠깐 흰색 유지
        if (holdDuration > 0f)
            yield return new WaitForSeconds(holdDuration);

        // 흰색 → 투명
        yield return FadeAlpha(
            1f,
            0f,
            fadeOutDuration);

        canvasGroup.alpha = 0f;
        flashCoroutine = null;
    }

    private IEnumerator FadeAlpha(
        float from,
        float to,
        float duration)
    {
        if (duration <= 0f)
        {
            canvasGroup.alpha = to;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            float t =
                Mathf.Clamp01(elapsed / duration);

            canvasGroup.alpha =
                Mathf.Lerp(from, to, t);

            yield return null;
        }

        canvasGroup.alpha = to;
    }

    private void OnDisable()
    {
        if (canvasGroup != null)
            canvasGroup.alpha = 0f;

        flashCoroutine = null;
    }
}