using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ScreenFade : MonoBehaviour
{
    [SerializeField] private CanvasGroup canvasGroup;

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(FadeIn(1f));
    }

    public IEnumerator FadeOut(float duration)
    {
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(0f, 1f, time / duration);
            yield return null;
        }

        canvasGroup.alpha = 1f;
    }

    public IEnumerator FadeIn(float duration)
    {
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            canvasGroup.alpha = Mathf.Lerp(1f, 0f, time / duration);
            yield return null;
        }

        canvasGroup.alpha = 0f;
    }

    public IEnumerator FadeOutAndIn(float fadeOutTime, float waitTime, float fadeInTime)
    {
        yield return FadeOut(fadeOutTime);

        yield return new WaitForSeconds(waitTime);

        yield return FadeIn(fadeInTime);
    }
}
