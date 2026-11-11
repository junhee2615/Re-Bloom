using System;
using System.Collections;
using ReBloom.Electricity;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class Stage3RestoredBgm : MonoBehaviour
{
    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float targetVolume = 0.7f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 3f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 2f;

    private AudioSource source;
    private Scene stageScene;
    private Coroutine fadeRoutine;
    private ElectricityCoreMission restorationMission;
    private bool started;
    private bool leavingStage;
    private bool endingFadeOutStarted;

    private void Awake()
    {
        stageScene = gameObject.scene;
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        source.volume = 0f;

        if (source.clip == null)
            Debug.LogError("[Stage3 Restored BGM] AudioSource에 복원 BGM AudioClip이 없습니다.", this);
    }

    private void OnEnable()
    {
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void OnDisable()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    }

    public void TryStartRestoredBgm()
    {
        if (!CanStart() || !IsActiveNetworkedStage())
            return;

        StartRestoredBgm();
    }

#if UNITY_EDITOR
    public bool TryStartRestoredBgmForEditorTest()
    {
        if (started)
            return true;

        if (!CanStart() || !HasRestorationMission())
            return false;

        StartRestoredBgm();
        return true;
    }
#endif

    public void BeginFadeOutBeforeCredits()
    {
        if (!started || leavingStage || endingFadeOutStarted)
            return;

        endingFadeOutStarted = true;
        StartFade(0f, fadeOutSeconds, StopAndDestroy);
    }

    private bool CanStart()
    {
        return !started && !leavingStage && source.clip != null && IsStageActive();
    }

    private bool IsStageActive()
    {
        return SceneManager.GetActiveScene().handle == stageScene.handle;
    }

    private void StartRestoredBgm()
    {
        started = true;
        source.Play();
        StartFade(targetVolume, fadeInSeconds, null);
    }

    private bool IsActiveNetworkedStage()
    {
        return HasRestorationMission() &&
               restorationMission.IsSpawned &&
               restorationMission.Object != null &&
               restorationMission.Object.IsValid &&
               restorationMission.IsClear;
    }

    private bool HasRestorationMission()
    {
        if (restorationMission == null ||
            restorationMission.gameObject.scene.handle != stageScene.handle)
        {
            restorationMission = null;
            ElectricityCoreMission[] missions =
                FindObjectsByType<ElectricityCoreMission>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);

            foreach (ElectricityCoreMission mission in missions)
            {
                if (mission.gameObject.scene.handle != stageScene.handle)
                    continue;

                restorationMission = mission;
                break;
            }
        }

        return restorationMission != null &&
               restorationMission.gameObject.scene.handle == stageScene.handle;
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        if (previous.handle == stageScene.handle && next.handle != stageScene.handle)
            BeginFadeOutForSceneTransition();
    }

    private void BeginFadeOutForSceneTransition()
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
