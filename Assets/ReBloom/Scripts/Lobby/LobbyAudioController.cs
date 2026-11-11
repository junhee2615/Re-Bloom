using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class LobbyAudioController : MonoBehaviour
{
    [Header("Audio Sources")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("Character Selection SFX")]
    [SerializeField] private AudioClip hoverClip;
    [SerializeField, Range(0f, 1f)] private float hoverVolume = 0.7f;
    [SerializeField] private AudioClip selectClip;
    [SerializeField, Range(0f, 1f)] private float selectVolume = 0.7f;
    [SerializeField] private AudioClip confirmClip;
    [SerializeField, Range(0f, 1f)] private float confirmVolume = 0.7f;
    [SerializeField] private AudioClip cancelClip;
    [SerializeField, Range(0f, 1f)] private float cancelVolume = 0.7f;

    [Header("Lobby BGM")]
    [SerializeField, Range(0f, 1f)] private float targetBgmVolume = 0.18f;
    [SerializeField, Min(0f)] private float fadeInSeconds = 2f;
    [SerializeField, Min(0f)] private float fadeOutSeconds = 2f;

    private Scene lobbyScene;
    private Coroutine startRoutine;
    private Coroutine fadeRoutine;
    private bool carriedToNextScene;
    private bool leavingLobby;

    private void Awake()
    {
        lobbyScene = gameObject.scene;

        if (bgmSource == null)
        {
            Debug.LogError("[Lobby Audio] BGM AudioSource가 연결되지 않았습니다.", this);
        }
        else
        {
            if (bgmSource.clip == null)
                Debug.LogError("[Lobby Audio] Lobby BGM AudioClip이 연결되지 않았습니다.", this);

            bgmSource.loop = true;
            bgmSource.spatialBlend = 0f;
            bgmSource.playOnAwake = false;
            bgmSource.volume = 0f;

            if (bgmSource.isPlaying)
                bgmSource.Stop();
        }

        if (sfxSource == null)
        {
            Debug.LogError("[Lobby Audio] SFX AudioSource가 연결되지 않았습니다.", this);
        }
        else
        {
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.playOnAwake = false;
            sfxSource.volume = 1f;
        }

        if (hoverClip == null)
            Debug.LogError("[Lobby Audio] Hover AudioClip이 연결되지 않았습니다.", this);
        if (selectClip == null)
            Debug.LogError("[Lobby Audio] Select AudioClip이 연결되지 않았습니다.", this);
        if (confirmClip == null)
            Debug.LogError("[Lobby Audio] Confirm AudioClip이 연결되지 않았습니다.", this);
        if (cancelClip == null)
            Debug.LogError("[Lobby Audio] Cancel AudioClip이 연결되지 않았습니다.", this);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    private void Start()
    {
        if (bgmSource != null && bgmSource.clip != null)
            startRoutine = StartCoroutine(WaitForOpeningBgmAndPlay());
    }

    private void Update()
    {
        if (leavingLobby)
            return;

        LobbyManager lobbyManager = LobbyManager.Instance;
        bool transitionStarted = lobbyManager != null && lobbyManager.SceneTransitionStarted;

        if (transitionStarted && !carriedToNextScene)
        {
            DontDestroyOnLoad(gameObject);
            carriedToNextScene = true;
        }
        else if (!transitionStarted &&
                 carriedToNextScene &&
                 lobbyScene.IsValid() &&
                 lobbyScene.isLoaded)
        {
            SceneManager.MoveGameObjectToScene(gameObject, lobbyScene);
            carriedToNextScene = false;
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
    }

    public void PlayHover()
    {
        PlaySfx(hoverClip, hoverVolume);
    }

    public void PlaySelect()
    {
        PlaySfx(selectClip, selectVolume);
    }

    public void PlayConfirm()
    {
        PlaySfx(confirmClip, confirmVolume);
    }

    public void PlayCancel()
    {
        PlaySfx(cancelClip, cancelVolume);
    }

    private void PlaySfx(AudioClip clip, float volume)
    {
        if (sfxSource == null || clip == null)
            return;

        sfxSource.PlayOneShot(clip, volume);
    }

    private IEnumerator WaitForOpeningBgmAndPlay()
    {
        while (FindAnyObjectByType<OpeningBgm>() != null)
        {
            if (leavingLobby)
                yield break;

            yield return new WaitForSecondsRealtime(0.1f);
        }

        startRoutine = null;
        if (leavingLobby || bgmSource == null || bgmSource.clip == null)
            yield break;

        bgmSource.Play();
        StartFade(targetBgmVolume, fadeInSeconds, null);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.handle != lobbyScene.handle && !lobbyScene.isLoaded && carriedToNextScene)
            BeginFadeOut();
    }

    private void OnSceneUnloaded(Scene scene)
    {
        if (scene.handle != lobbyScene.handle)
            return;

        if (!carriedToNextScene)
        {
            DontDestroyOnLoad(gameObject);
            carriedToNextScene = true;
        }

        BeginFadeOut();
    }

    private void BeginFadeOut()
    {
        if (leavingLobby)
            return;

        leavingLobby = true;

        if (startRoutine != null)
        {
            StopCoroutine(startRoutine);
            startRoutine = null;
        }

        StartFade(0f, fadeOutSeconds, StopBgmAndWaitForSfx);
    }

    private void StartFade(float targetVolume, float duration, Action onComplete)
    {
        if (bgmSource == null)
        {
            onComplete?.Invoke();
            return;
        }

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeTo(targetVolume, duration, onComplete));
    }

    private IEnumerator FadeTo(float targetVolume, float duration, Action onComplete)
    {
        float startVolume = bgmSource.volume;

        if (duration <= 0f)
        {
            bgmSource.volume = targetVolume;
        }
        else
        {
            for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                bgmSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
                yield return null;
            }

            bgmSource.volume = targetVolume;
        }

        fadeRoutine = null;
        onComplete?.Invoke();
    }

    private void StopBgmAndWaitForSfx()
    {
        if (bgmSource != null)
            bgmSource.Stop();

        StartCoroutine(DestroyAfterSfxPlayback());
    }

    private IEnumerator DestroyAfterSfxPlayback()
    {
        while (sfxSource != null && sfxSource.isPlaying)
            yield return null;

        Destroy(gameObject);
    }
}
