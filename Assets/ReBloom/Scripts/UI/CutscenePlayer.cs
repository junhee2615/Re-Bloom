using System.Collections;
using System.Linq;
using Fusion;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

[RequireComponent(typeof(TrainFloor))]
public class CutscenePlayer : NetworkBehaviour
{
    [Header("Video")]
    [Tooltip("씬에 있는 TrainVideoPlayer의 VideoPlayer. 각 피어의 로컬 인스턴스에서 재생된다.")]
    [SerializeField] private VideoPlayer videoPlayer;

    [Header("Timing")]
    [SerializeField] private float fadeDuration = 1f;
    [Tooltip("영상이 끝나지 않아도 강제로 진행하는 최대 재생 시간(초). 0 이하이면 무제한.")]
    [SerializeField] private float maxVideoSeconds = 60f;
    [Tooltip("Host가 자기 컷신을 끝낸 뒤, 다른 클라이언트 완료 보고를 기다리는 최대 시간(초).")]
    [SerializeField] private float clientWaitTimeout = 15f;

    [Header("Scene")]
    [Tooltip("컷신 후 로드할 다음 씬 이름. Build Profiles > Scene List에 등록되어 있어야 한다.")]
    [SerializeField] private string nextSceneName = "Stage2";

    private ScreenFade screenFade;
    private RawImage videoRawImage;
    private TrainFloor trainFloor;

    // Host 전용 상태
    private int reportsReceived;
    private bool loadTriggered;
    private bool cutsceneStarted;

    private void Awake()
    {
        trainFloor = GetComponent<TrainFloor>();
    }

    
    public void BeginCutscene()
    {
        if (!HasStateAuthority || cutsceneStarted)
            return;

        cutsceneStarted = true;
        reportsReceived = 0;
        loadTriggered = false;

        RPC_PlayCutscene();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_PlayCutscene()
    {
        StartCoroutine(CutsceneRoutine());
    }

    private IEnumerator CutsceneRoutine()
    {
        ResolveLocalReferences();

        // 문 닫힘
        if (trainFloor != null)
            yield return StartCoroutine(trainFloor.CloseDoorsRoutine());

        // 페이드아웃
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));

        // 검정 화면 상태에서 영상 노출 + 준비/재생 시작
        ShowVideo();
        yield return StartCoroutine(PrepareAndPlayVideo());

        // 페이드인
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeIn(fadeDuration));

        // 영상 종료 대기
        yield return StartCoroutine(WaitForVideoEnd());

        // 페이드아웃
        if (screenFade != null)
            yield return StartCoroutine(screenFade.FadeOut(fadeDuration));

        HideVideo();

        // Host에 완료 보고
        RPC_ReportDone();

        // 클라이언트 지연/이탈 대비 워치독 시작
        if (HasStateAuthority)
            StartCoroutine(LoadWatchdog());
    }

    private IEnumerator PrepareAndPlayVideo()
    {
        if (videoPlayer == null)
            yield break;

        videoPlayer.Prepare();

        float t = 0f;
        while (!videoPlayer.isPrepared && t < 5f)
        {
            t += Time.deltaTime;
            yield return null;
        }

        videoPlayer.Play();
    }

    private IEnumerator WaitForVideoEnd()
    {
        if (videoPlayer == null)
            yield break;

        bool finished = false;
        void OnEnd(VideoPlayer vp) => finished = true;
        videoPlayer.loopPointReached += OnEnd;

        // 재생이 실제로 시작될 때까지 잠깐 보장
        float startGuard = 0f;
        while (!videoPlayer.isPlaying && startGuard < 2f)
        {
            startGuard += Time.deltaTime;
            yield return null;
        }

        // 종료 또는 최대 시간까지 대기
        float elapsed = 0f;
        while (!finished)
        {
            if (maxVideoSeconds > 0f && elapsed >= maxVideoSeconds)
                break;

            elapsed += Time.deltaTime;
            yield return null;
        }

        videoPlayer.loopPointReached -= OnEnd;
    }

    private void ShowVideo()
    {
        if (videoRawImage == null)
            return;

        if (videoPlayer != null && videoPlayer.targetTexture != null)
            videoRawImage.texture = videoPlayer.targetTexture;

        videoRawImage.gameObject.SetActive(true);
    }

    private void HideVideo()
    {
        if (videoRawImage != null)
            videoRawImage.gameObject.SetActive(false);

        if (videoPlayer != null)
            videoPlayer.Stop();
    }

    private void ResolveLocalReferences()
    {
        if (screenFade == null)
            screenFade = FindFirstObjectByType<ScreenFade>();

        if (videoRawImage == null && screenFade != null)
        {
            RawImage[] images = screenFade.GetComponentsInChildren<RawImage>(true);
            foreach (RawImage img in images)
            {
                if (img.name.Contains("Video"))
                {
                    videoRawImage = img;
                    break;
                }
            }

            if (videoRawImage == null && images.Length > 0)
                videoRawImage = images[0];
        }

        if (screenFade == null)
            Debug.LogWarning("[CutscenePlayer] ScreenFade를 찾지 못했습니다. 로컬 리그에 ScreenFade가 있는지 확인하세요.");

        if (videoRawImage == null)
            Debug.LogWarning("[CutscenePlayer] VideoRawImage를 찾지 못했습니다. ScreenFadeCanvas 하위에 RawImage가 있는지 확인하세요.");
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportDone()
    {
        if (!HasStateAuthority)
            return;

        reportsReceived++;

        int expected = Runner != null ? Runner.ActivePlayers.Count() : reportsReceived;
        if (reportsReceived >= expected)
            TriggerLoad();
    }

    private IEnumerator LoadWatchdog()
    {
        yield return new WaitForSeconds(clientWaitTimeout);
        TriggerLoad();
    }

    private void TriggerLoad()
    {
        if (!HasStateAuthority || loadTriggered)
            return;

        // 빌드 인덱스는 Scene List에 씬을 추가하면 밀리므로 이름으로 찾는다.
        SceneRef next = NetworkManager.Instance != null
            ? NetworkManager.Instance.GetSceneRef(nextSceneName)
            : SceneRef.None;

        if (next == SceneRef.None)
        {
            Debug.LogError($"[CutscenePlayer] 씬 '{nextSceneName}'을 찾을 수 없습니다. Build Profiles > Scene List를 확인하세요.", this);
            return;
        }

        loadTriggered = true;
        Runner.LoadScene(next);
    }
}
