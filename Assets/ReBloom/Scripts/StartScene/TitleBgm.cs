using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// StartScene 타이틀 화면의 BGM을 담당한다. <b>타이틀 전용</b>이다.
///
/// 하는 일은 둘뿐이다.
///   ① 타이틀에 들어오면 볼륨 0에서 <see cref="targetVolume"/> 까지 서서히 올린다
///   ② 타이틀을 떠나면 서서히 내린 뒤 <b>스스로 사라진다</b>
///
/// 전역 BGM 매니저가 아니다 (2026-11-10):
/// 다른 씬의 음악을 관리하지 않고, 어떤 공개 API도 노출하지 않으며, 타이틀을 떠난 뒤
/// 몇 초 안에 자기 GameObject를 파괴한다. <see cref="active"/> static 필드는 음악을
/// 관리하려고 둔 것이 아니라 <b>중복 재생을 막는 빗장</b> 하나일 뿐이다.
/// 나중에 스테이지 BGM이 필요해지면 그때 별도 구조를 세우면 되고, 이 스크립트는
/// 그 설계에 아무 제약도 걸지 않는다.
///
/// 왜 Fade Out 때문에 씬을 넘어가야 하는가:
/// 타이틀을 떠나는 유일한 경로는 <c>ConnectionManager.Enter</c> 의
/// <c>SceneManager.LoadScene("Opening", Single)</c> 이고, 이 호출은 코루틴이 아니라
/// <b>그 자리에서 즉시</b> 씬을 교체한다. 즉 StartScene의 오브젝트는 Fade Out을 할
/// 시간을 한 프레임도 받지 못한다. 그래서 둘 중 하나를 골라야 한다.
///   ⒜ 씬 로드를 Fade 시간만큼 늦춘다 → 버튼을 눌러도 몇 초간 아무 일도 없어 보인다
///   ⒝ 음악만 로드를 넘겨 살려 두고 그 위에서 Fade Out한다
/// ⒝ 가 플레이 감각이 자연스럽고 <b>기존 씬 전환 코드를 전혀 건드리지 않는다</b>.
/// 그래서 <see cref="carryOverOnSceneChange"/> 기본값이 켜짐이다.
/// 음악은 Opening이 시작되는 동안 <see cref="fadeOutSeconds"/> 에 걸쳐 잦아들고 끝난다.
///
/// 떠나는 것을 어떻게 아는가:
/// <see cref="SceneManager.activeSceneChanged"/> 를 듣는다. Active Scene이 StartScene이
/// 아니게 된 순간이 곧 타이틀을 떠난 순간이다.
/// 배경 씬 Additive 로드는 이 이벤트를 발생시키지 않는다 —
/// <see cref="TitleBackgroundController"/> 가 Active Scene을 StartScene 그대로 두기
/// 때문이다(그 클래스의 "Active Scene은 StartScene 그대로 둔다" 주석 참고).
/// 따라서 배경이 올라올 때 음악이 끊기지 않는다.
///
/// 왜 OnDestroy에서 Fade용 오브젝트를 만들지 않는가:
/// 씬 언로드 중의 <c>OnDestroy</c> 는 애플리케이션 종료·에디터 정지에서도 호출되고,
/// 그 시점에 GameObject를 새로 만드는 것은 Unity가 보장하지 않는 동작이다.
/// 처음부터 DontDestroyOnLoad로 두면 그런 경계 조건이 아예 생기지 않는다.
///
/// 기존 환경음·오디오 시스템과의 관계:
///  · 타이틀의 천둥소리는 배경 씬 <c>Stage1_TitleBackground</c> 의 <c>ThunderManager</c>
///    가 PlayOneShot으로 낸다. 여기서는 그 오브젝트를 전혀 건드리지 않으므로 그대로 울린다.
///  · AudioSource의 Output이 비어 있어 Master로 바로 나간다. <c>AudioMixer</c> 의
///    Resonance 그룹을 쓰지 않으므로 에코의 청각 제약(<c>AudioConstraint</c>)과
///    Snapshot 전환에 영향을 주지도, 받지도 않는다.
///    (애초에 StartScene에는 <c>ResonanceController</c> 가 없다.)
///  · Spatial Blend 0(2D)이라 리그 위치나 <c>AudioListener</c> 방향과 무관하다.
///    <see cref="TitleBackgroundController"/> 가 리그를 촬영 시점으로 옮겨도 소리가 변하지 않는다.
///
/// 배치: StartScene 루트의 <c>TitleBGM</c> 오브젝트. 같은 오브젝트의 AudioSource를 쓴다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
[AddComponentMenu("ReBloom/StartScene/Title BGM")]
public class TitleBgm : MonoBehaviour
{
    /// <summary>타이틀 씬 이름. Active Scene이 이 이름이 아니게 되면 떠난 것으로 본다.</summary>
    private const string TitleSceneName = "StartScene";

    [Header("Fade")]
    [Tooltip("타이틀에 들어와 볼륨 0에서 목표 볼륨까지 올리는 데 걸리는 시간(초).")]
    [SerializeField, Min(0f)] private float fadeInSeconds = 3f;

    [Tooltip("Fade In이 끝난 뒤의 볼륨. 초기 테스트 기준값은 0.25다.")]
    [SerializeField, Range(0f, 1f)] private float targetVolume = 0.25f;

    [Tooltip("타이틀을 떠날 때 볼륨을 0까지 내리는 데 걸리는 시간(초). 이 시간이 지나면 스스로 사라진다.")]
    [SerializeField, Min(0f)] private float fadeOutSeconds = 2f;

    [Header("씬 전환")]
    [Tooltip("켜면 음악만 씬 전환을 넘겨 살려 두고 Fade Out을 끝낸 뒤 스스로 파괴한다(기본). " +
             "끄면 StartScene이 언로드되는 순간 음악이 그대로 끊긴다 — Fade Out은 들리지 않는다.")]
    [SerializeField] private bool carryOverOnSceneChange = true;

    [Header("Debug")]
    [Tooltip("재생·Fade·종료 시점에 로그를 남긴다.")]
    [SerializeField] private bool logBgm = true;

    /// <summary>
    /// 지금 울리고 있는 BGM. 타이틀을 떠난 뒤 Fade Out이 남아 있는 동안 StartScene이
    /// 다시 로드되는 경우에 두 번 겹쳐 울리는 것을 막는 빗장이다. 음악을 관리하지는 않는다.
    /// </summary>
    private static TitleBgm active;

    private AudioSource source;
    private Coroutine fade;

    /// <summary>타이틀을 떠나 Fade Out에 들어갔는지. 중복 진입을 막는다.</summary>
    private bool leaving;

    private void Awake()
    {
        // ── 중복 재생 빗장 ───────────────────────────────────────
        // 앞선 BGM이 아직 Fade Out 중인데 타이틀이 다시 로드된 경우다.
        // 새로 생긴 쪽이 물러난다 — 이미 울리고 있는 음악을 끊지 않는 편이 자연스럽다.
        if (active != null && active != this)
        {
            if (logBgm)
                Debug.Log("[TitleBGM] 이미 재생 중인 BGM이 있어 이 오브젝트는 물러납니다.", this);

            Destroy(gameObject);
            return;
        }

        active = this;
        source = GetComponent<AudioSource>();

        if (source.clip == null)
        {
            Debug.LogError(
                "[TitleBGM] AudioSource에 AudioClip이 없습니다. " +
                "StartScene의 TitleBGM 오브젝트에 Assets/ReBloom/Sounds/BGM 의 BGM을 연결하세요.", this);
            return;
        }

        if (!source.loop)
        {
            Debug.LogWarning(
                "[TitleBGM] AudioSource의 Loop가 꺼져 있어 BGM이 한 번만 재생됩니다. " +
                "타이틀은 머무는 시간이 정해져 있지 않으므로 Loop를 켜는 것이 맞습니다.", this);
        }

        // 씬에 어떤 값이 저장되어 있든 Fade In은 0에서 시작해야 한다.
        // 이것을 강제하지 않으면 누군가 Inspector에서 볼륨을 올려 저장한 순간
        // 타이틀 진입이 "뚝 하고 큰 소리로" 시작된다.
        source.volume = 0f;

        if (!carryOverOnSceneChange)
            return;

        // 씬 전환을 넘겨 Fade Out을 끝내기 위해 영속 씬으로 옮긴다.
        // Fade Out이 끝나면 아래 FadeOutAndDie가 이 오브젝트를 파괴하므로 남지 않는다.
        DontDestroyOnLoad(gameObject);
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void Start()
    {
        if (source == null || source.clip == null)
            return;

        // Play On Awake가 꺼져 있어도 울리게 한다. 꺼 두고 저장하는 사고를 막는 보험이다.
        if (!source.isPlaying)
            source.Play();

        StartFade(FadeTo(targetVolume, fadeInSeconds, "Fade In"));
    }

    private void OnDestroy()
    {
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;

        if (active == this)
            active = null;
    }

    /// <summary>
    /// Active Scene이 바뀌었다. StartScene이 아니게 되었다면 타이틀을 떠난 것이다.
    ///
    /// 배경 씬 Additive 로드는 Active Scene을 바꾸지 않으므로 여기에 걸리지 않는다.
    /// </summary>
    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        if (next.name == TitleSceneName)
            return;

        BeginFadeOut(next.name);
    }

    private void BeginFadeOut(string nextSceneName)
    {
        if (leaving)
            return;

        leaving = true;

        if (logBgm)
            Debug.Log($"[TitleBGM] '{nextSceneName}'(으)로 전환 - {fadeOutSeconds:0.##}초 Fade Out 후 정리합니다.", this);

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
            Debug.Log($"[TitleBGM] {label} 완료 - 볼륨 {from:0.###} → {volume:0.###}", this);

        fade = null;
    }

    /// <summary>
    /// 볼륨을 0까지 내린 뒤 재생을 멈추고 <b>자기 GameObject를 파괴한다.</b>
    ///
    /// 여기서 반드시 파괴해야 한다 — 이 오브젝트는 DontDestroyOnLoad라서
    /// 파괴하지 않으면 플레이 세션이 끝날 때까지 남아 Lobby·Stage까지 따라간다.
    /// </summary>
    private IEnumerator FadeOutAndDie()
    {
        yield return FadeTo(0f, fadeOutSeconds, "Fade Out");

        source.Stop();

        if (logBgm)
            Debug.Log("[TitleBGM] 정리 완료 - 타이틀 BGM 오브젝트를 파괴합니다.", this);

        Destroy(gameObject);
    }
}
