using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Opening 씬의 BGM을 담당한다. <b>오프닝 전용</b>이다.
///
/// 하는 일은 둘뿐이다.
///   ① 오프닝이 시작되면 볼륨 0에서 <see cref="targetVolume"/> 까지 서서히 올린다
///   ② 오프닝이 끝나면(정상 종료 / 스킵 둘 다) 서서히 내린 뒤 <b>스스로 사라진다</b>
///
/// 전역 BGM 매니저가 아니다 (2026-11-10):
/// 다른 씬의 음악을 관리하지 않고, 어떤 공개 API도 노출하지 않으며, 오프닝이 끝난 뒤
/// 몇 초 안에 자기 GameObject를 파괴한다. <see cref="active"/> static 필드는 음악을
/// 관리하려고 둔 것이 아니라 <b>중복 재생을 막는 빗장</b> 하나일 뿐이다.
/// <see cref="TitleBgm"/> 과 같은 구조이지만 서로를 참조하지 않는 별개의 스크립트다.
///
/// ── 종료를 어떻게 아는가 ────────────────────────────────────────
/// <see cref="OpeningNarrationController.IsFinishing"/> 하나만 본다.
///
/// 이 플래그는 정상 종료(<c>FinishOpeningAndEnterSession</c>)와
/// 스킵(<c>RequestSkip</c> → <c>SkipOpeningRoutine</c>)이 <b>공유하는 단일 게이트</b>
/// <c>TryClaimFinish()</c> 가 올린다. 그래서 두 경로 모두 추가 배선 없이 덮인다 —
/// <see cref="OpeningNarrationController"/> / <c>OpeningSkipInput</c> / <c>OpeningSkipUI</c>
/// 를 한 줄도 수정하지 않았다.
///
/// 왜 <c>SceneManager.activeSceneChanged</c> 를 쓰지 않는가 (중요):
/// <see cref="TitleBgm"/> 은 그 이벤트로 이탈을 감지하지만 <b>Opening에서는 쓸 수 없다.</b>
/// <c>OpeningEnvironmentController</c> 가 Stage의 RenderSettings(Skybox/Ambient/Fog)를
/// 적용하기 위해 연출 중 <c>SceneManager.SetActiveScene</c> 을 반복 호출한다
/// (<c>OpeningEnvironmentController.cs:246</c> Stage로, <c>:320</c> Opening으로 복귀).
/// 즉 Active Scene은 Opening ↔ Stage1 ↔ Stage2 ↔ Stage3 로 여러 번 바뀌므로,
/// 그 이벤트를 종료 신호로 쓰면 <b>오프닝 중간에 음악이 꺼진다.</b>
///
/// 그래서 안전망도 씬 이름에 의존하지 않는다:
/// Awake에서 <b>DontDestroyOnLoad로 옮기기 전에</b> Opening 씬 핸들을 잡아 두고,
/// 그 씬의 <c>isLoaded</c> 가 false가 되는 순간을 이탈로 본다. Active Scene이 무엇이든,
/// 씬 이름을 전혀 모르고도, 내레이션 컨트롤러를 못 찾았더라도 반응한다.
///
/// ── 왜 씬 전환을 넘어가야 하는가 ───────────────────────────────
/// <c>FinishOpeningCore</c> 는 <c>IsFinishing</c> 을 올린 직후
/// <c>RestoreScreenFadeForLobby()</c> → <c>NetworkManager.EnterSession()</c> 으로
/// 이어지고, Fusion이 Lobby를 Network Scene으로 로드하면서 Opening이 언로드된다.
/// 그 간격이 <see cref="fadeOutSeconds"/> 보다 길다는 보장이 없으므로, 음악만
/// DontDestroyOnLoad로 넘겨 Fade Out을 끝낸 뒤 파괴한다. 덕분에 Lobby로 넘어갈 때
/// 소리가 뚝 끊기지 않는다.
///
/// ── 타이틀 BGM과의 관계 ────────────────────────────────────────
/// <see cref="TitleBgm"/> 은 StartScene을 떠날 때 자기 음악을 2초에 걸쳐 내리며
/// Opening까지 따라와 잦아든다. 여기서 Fade In을 2초로 두면 두 곡이 겹치며
/// 자연스럽게 교차된다. 겹치는 것이 싫으면 <see cref="startDelaySeconds"/> 를
/// 타이틀의 Fade Out 시간(기본 2초)만큼 올려 순차 재생으로 바꿀 수 있다.
/// 두 스크립트는 서로를 참조하지 않으므로 어느 쪽을 꺼도 다른 쪽이 그대로 동작한다.
///
/// ── 기존 오디오 시스템과의 관계 ────────────────────────────────
///  · AudioSource의 Output이 비어 있어 Master로 바로 나간다. <c>AudioMixer</c> 의
///    Resonance 그룹을 쓰지 않으므로 에코의 청각 제약(<c>AudioConstraint</c>)과
///    Snapshot 전환에 영향을 주지도, 받지도 않는다.
///    (애초에 Opening에는 <c>ResonanceController</c> 가 없다.)
///  · Spatial Blend 0(2D)이라 리그 위치나 <c>AudioListener</c> 방향과 무관하다.
///    <c>OpeningEnvironmentController</c> 가 리그를 Stage 촬영 시점으로 옮겨도
///    소리가 변하지 않는다.
///  · Loop를 끈다. 오프닝은 길이가 정해진 연출이고, 음원이 그보다 길다.
///
/// 배치: Opening 씬 루트의 <c>OpeningBGM</c> 오브젝트. 같은 오브젝트의 AudioSource를 쓴다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("ReBloom/Opening/Opening BGM")]
public class OpeningBgm : MonoBehaviour
{
    [Header("Fade")]
    [Tooltip("재생을 시작하기까지 기다리는 시간(초). 0이면 오프닝과 동시에 올라온다. " +
             "타이틀 BGM의 Fade Out(기본 2초)과 겹치지 않게 하려면 그 값만큼 올린다.")]
    [SerializeField, Min(0f)] private float startDelaySeconds;

    [Tooltip("볼륨 0에서 목표 볼륨까지 올리는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 2f;

    [Tooltip("Fade In이 끝난 뒤의 볼륨.")]
    [SerializeField, Range(0f, 1f)] private float targetVolume = 0.25f;

    [Tooltip("오프닝이 끝날 때 볼륨을 0까지 내리는 데 걸리는 시간(초). 이 시간이 지나면 스스로 사라진다.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 2f;

    [Header("References")]
    [Tooltip("종료 시점을 알려 줄 내레이션 컨트롤러. 비워 두면 씬에서 자동으로 찾는다.")]
    [SerializeField] private OpeningNarrationController narrationOverride;

    [Header("씬 전환")]
    [Tooltip("켜면 음악만 씬 전환을 넘겨 살려 두고 Fade Out을 끝낸 뒤 스스로 파괴한다(기본). " +
             "끄면 Opening이 언로드되는 순간 음악이 그대로 끊긴다.")]
    [SerializeField] private bool carryOverOnSceneChange = true;

    [Header("Debug")]
    [Tooltip("재생·Fade·종료 시점에 로그를 남긴다.")]
    [SerializeField] private bool logBgm = true;

    /// <summary>
    /// 지금 울리고 있는 오프닝 BGM. Fade Out이 남아 있는 동안 Opening이 다시 로드되는
    /// 경우에 두 번 겹쳐 울리는 것을 막는 빗장이다. 음악을 관리하지는 않는다.
    /// </summary>
    private static OpeningBgm active;

    private AudioSource source;
    private OpeningNarrationController narration;
    private Coroutine fade;

    /// <summary>Fade Out에 들어갔는지. 중복 진입을 막는다.</summary>
    private bool leaving;

    /// <summary>
    /// 이 오브젝트가 원래 속해 있던 Opening 씬. <b>DontDestroyOnLoad로 옮기기 전에</b>
    /// 잡아야 한다 — 옮긴 뒤의 <c>gameObject.scene</c> 은 DontDestroyOnLoad 씬이 된다.
    /// </summary>
    private Scene openingScene;

    private void Awake()
    {
        // ── 중복 재생 빗장 ───────────────────────────────────────
        // 앞선 오프닝 BGM이 아직 Fade Out 중인데 Opening이 다시 로드된 경우다.
        // 새로 생긴 쪽이 물러난다 — 이미 울리고 있는 음악을 끊지 않는 편이 자연스럽다.
        if (active != null && active != this)
        {
            if (logBgm)
                Debug.Log("[OpeningBGM] 이미 재생 중인 오프닝 BGM이 있어 이 오브젝트는 물러납니다.", this);

            Destroy(gameObject);
            return;
        }

        active = this;
        source = GetComponent<AudioSource>();

        // 반드시 DontDestroyOnLoad 보다, 그리고 어떤 조기 return 보다 먼저 잡는다.
        // 옮긴 뒤의 gameObject.scene 은 Opening을 가리키지 않는다.
        openingScene = gameObject.scene;

        if (source.clip == null)
        {
            Debug.LogError(
                "[OpeningBGM] AudioSource에 AudioClip이 없습니다. " +
                "Opening의 OpeningBGM 오브젝트에 Assets/ReBloom/Sounds/BGM 의 BGM을 연결하세요.", this);
            return;
        }

        if (source.loop)
        {
            Debug.LogWarning(
                "[OpeningBGM] AudioSource의 Loop가 켜져 있습니다. 오프닝은 길이가 정해진 연출이므로 " +
                "Loop는 끄는 것이 맞습니다.", this);
        }

        // 씬에 어떤 값이 저장되어 있든 Fade In은 0에서 시작해야 한다.
        // 이것을 강제하지 않으면 누군가 Inspector에서 볼륨을 올려 저장한 순간
        // 오프닝 진입이 "뚝 하고 큰 소리로" 시작된다.
        source.volume = 0f;

        // Play On Awake는 꺼 둔다 — startDelaySeconds 를 지키려면 재생 시점을 코드가 쥐어야 한다.
        source.playOnAwake = false;

        if (source.isPlaying)
            source.Stop();

        if (carryOverOnSceneChange)
        {
            // 씬 전환을 넘겨 Fade Out을 끝내기 위해 영속 씬으로 옮긴다.
            // Fade Out이 끝나면 아래 FadeOutAndDie가 이 오브젝트를 파괴하므로 남지 않는다.
            DontDestroyOnLoad(gameObject);
        }
    }

    private void Start()
    {
        if (source == null || source.clip == null)
            return;

        ResolveNarration();

        StartFade(PlayAfterDelayAndFadeIn());
    }

    private void OnDestroy()
    {
        if (active == this)
            active = null;
    }

    /// <summary>
    /// 종료 시점을 알려 줄 내레이션 컨트롤러를 잡는다.
    ///
    /// <c>DontDestroyOnLoad</c> 로 옮긴 뒤라 <c>gameObject.scene</c> 으로는 Opening을
    /// 찾을 수 없으므로 타입으로 찾는다. Opening 씬에만 존재하는 컴포넌트다.
    /// </summary>
    private void ResolveNarration()
    {
        narration = narrationOverride != null
            ? narrationOverride
            : FindAnyObjectByType<OpeningNarrationController>(FindObjectsInactive.Include);

        if (narration == null)
        {
            // 치명적이지는 않다 — Update의 안전망(Opening 씬 언로드)이 그대로 동작한다.
            // 다만 "오프닝이 끝나는 순간"에 맞춘 Fade Out은 되지 않으므로 알린다.
            Debug.LogWarning(
                "[OpeningBGM] OpeningNarrationController를 찾지 못했습니다. " +
                "오프닝 종료에 맞춘 Fade Out 대신 Opening 씬이 사라질 때 Fade Out합니다.", this);
        }
    }

    /// <summary>
    /// 오프닝이 끝났는지 지켜본다.
    ///
    /// 두 가지를 본다 —
    ///   ① <c>IsFinishing</c>      : 정상 종료와 스킵이 공유하는 게이트. 정상 경로다.
    ///   ② Opening 씬이 언로드됨   : 어떤 이유로든 씬이 사라진 경우. 안전망이다.
    ///
    /// ②는 컨트롤러를 찾지 못했을 때도 동작한다. 그래서 이 오브젝트가
    /// DontDestroyOnLoad에 영구히 남는 경로가 없다.
    ///
    /// 씬 이름이나 Active Scene을 보지 않는다 — 클래스 주석의 <c>SetActiveScene</c> 설명 참고.
    /// </summary>
    private void Update()
    {
        if (leaving || source == null || source.clip == null)
            return;

        // ① 오프닝이 끝났다 (정상 종료 / 스킵 공통 게이트).
        //    씬이 이미 사라졌다면 narration 은 null 이므로 이 검사는 건너뛰고 ②로 간다.
        if (narration != null)
        {
            if (narration.IsFinishing)
                BeginFadeOut("오프닝 종료(정상 또는 스킵)");

            return;
        }

        // ② Opening 씬 자체가 사라졌다.
        if (!openingScene.isLoaded)
            BeginFadeOut("Opening 씬 종료");
    }

    private void BeginFadeOut(string reason)
    {
        if (leaving)
            return;

        leaving = true;

        if (logBgm)
            Debug.Log($"[OpeningBGM] {reason} - {fadeOutSeconds:0.##}초 Fade Out 후 정리합니다.", this);

        StartFade(FadeOutAndDie());
    }

    /// <summary>진행 중인 Fade를 멈추고 새 Fade를 시작한다. 두 Fade가 겹쳐 볼륨이 튀는 것을 막는다.</summary>
    private void StartFade(IEnumerator routine)
    {
        if (fade != null)
            StopCoroutine(fade);

        fade = StartCoroutine(routine);
    }

    /// <summary>
    /// <see cref="startDelaySeconds"/> 만큼 기다린 뒤 재생을 시작하고 Fade In한다.
    ///
    /// 기다리는 동안 오프닝이 이미 끝났다면(스킵을 아주 빨리 누른 경우) 아예 재생하지 않는다.
    /// 들리지도 않은 음악을 Fade Out하는 것은 의미가 없다.
    /// </summary>
    private IEnumerator PlayAfterDelayAndFadeIn()
    {
        for (float time = 0f; time < startDelaySeconds; time += Time.unscaledDeltaTime)
        {
            if (leaving)
                yield break;

            yield return null;
        }

        if (leaving)
            yield break;

        source.Play();

        if (logBgm)
            Debug.Log($"[OpeningBGM] 재생 시작 - {fadeInSeconds:0.##}초 Fade In, 목표 볼륨 {targetVolume:0.###}", this);

        yield return FadeTo(targetVolume, fadeInSeconds, "Fade In");
    }

    /// <summary>
    /// 현재 볼륨에서 <paramref name="volume"/> 까지 선형으로 옮긴다.
    ///
    /// <see cref="Time.unscaledDeltaTime"/> 을 쓰는 이유: 컷씬이나 로딩이 timeScale을
    /// 건드려도 Fade가 멈추거나 느려지지 않아야 한다. BGM은 연출 시간과 무관하다.
    /// </summary>
    private IEnumerator FadeTo(float volume, float seconds, string label)
    {
        float from = source.volume;

        if (seconds <= 0f)
        {
            source.volume = volume;
        }
        else
        {
            for (float time = 0f; time < seconds; time += Time.unscaledDeltaTime)
            {
                source.volume = Mathf.Lerp(from, volume, time / seconds);
                yield return null;
            }

            source.volume = volume;
        }

        if (logBgm)
            Debug.Log($"[OpeningBGM] {label} 완료 - 볼륨 {from:0.###} → {volume:0.###}", this);

        fade = null;
    }

    /// <summary>
    /// 볼륨을 0까지 내린 뒤 재생을 멈추고 <b>자기 GameObject를 파괴한다.</b>
    ///
    /// 여기서 반드시 파괴해야 한다 — <see cref="carryOverOnSceneChange"/> 가 켜져 있으면
    /// 이 오브젝트는 DontDestroyOnLoad라서, 파괴하지 않으면 플레이 세션이 끝날 때까지
    /// 남아 Lobby·Stage까지 따라간다.
    /// </summary>
    private IEnumerator FadeOutAndDie()
    {
        yield return FadeTo(0f, fadeOutSeconds, "Fade Out");

        source.Stop();

        if (logBgm)
            Debug.Log("[OpeningBGM] 정리 완료 - 오프닝 BGM 오브젝트를 파괴합니다.", this);

        Destroy(gameObject);
    }
}
