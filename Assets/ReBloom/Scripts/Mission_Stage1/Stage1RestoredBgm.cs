using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class Stage1RestoredBgm : MonoBehaviour
{
    [Header("Restoration State")]
    [SerializeField] private LeverMissionManager restorationManager;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float targetVolume = 0.2f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 3f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 2f;

    private AudioSource source;
    private Scene stageScene;
    private Coroutine fadeRoutine;
    private bool started;
    private bool leavingStage;

    private void Awake()
    {
        stageScene = gameObject.scene;
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;

        if (source.clip == null)
            Debug.LogError("[Stage1 Restored BGM] AudioSource에 복원 BGM AudioClip이 없습니다.", this);
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    private void Update()
    {
        if (started || leavingStage || source.clip == null)
            return;

        if (restorationManager == null)
            restorationManager = FindFirstObjectByType<LeverMissionManager>(FindObjectsInactive.Include);

        if (restorationManager == null ||
            restorationManager.Object == null ||
            !restorationManager.Object.IsValid ||
            !restorationManager.IsRestoreApplied)
        {
            return;
        }

        started = true;
        source.Play();
        StartFade(targetVolume, fadeInSeconds, null);
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        if (previous.handle != stageScene.handle || next.handle == stageScene.handle)
            return;

        BeginFadeOutForSceneTransition();
    }

    public void BeginFadeOutForSceneTransition()
    {
        if (!started || leavingStage)
            return;

        leavingStage = true;
        DontDestroyOnLoad(gameObject);
        StartFade(0f, fadeOutSeconds, StopAndDestroy);
    }

    private void StartFade(float volume, float duration, Action onComplete)
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeTo(volume, duration, onComplete));
    }

    private IEnumerator FadeTo(float volume, float duration, Action onComplete)
    {
        float startVolume = source.volume;

        if (duration <= 0f)
        {
            source.volume = volume;
        }
        else
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                source.volume = Mathf.Lerp(startVolume, volume, elapsed / duration);
                yield return null;
            }

            source.volume = volume;
        }

        fadeRoutine = null;
        onComplete?.Invoke();
    }

    private void StopAndDestroy()
    {
        source.Stop();
        Destroy(gameObject);
    }
}
