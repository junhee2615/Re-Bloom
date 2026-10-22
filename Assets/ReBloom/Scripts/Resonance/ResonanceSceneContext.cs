using UnityEngine;

/// <summary>
/// 스테이지 씬마다 하나씩 배치한다. 공명 시스템이 필요로 하는 "씬 전용" 참조와
/// 스테이지별 튜닝 값을 영속 <see cref="ResonanceController"/> 에 주입한다.
/// </summary>
public sealed class ResonanceSceneContext : MonoBehaviour
{
    [Header("Distance Reference (최초 1회 측정용)")]
    [Tooltip("공명 거리는 최초로 바인딩된 스테이지에서 한 번만 책정되고 이후 스테이지에서는 그대로 유지된다. " +
             "정상 플로우에서는 Stage1에서만 쓰이며, 다른 스테이지에서는 비워둬도 된다.")]
    [SerializeField] private Transform hostSpawnPoint;
    [SerializeField] private Transform clientSpawnPoint;

    [Header("Audio")]
    [Tooltip("제약 시 Resonance 믹서 그룹으로 라우팅할 이 스테이지의 AudioSource들.")]
    [SerializeField] private AudioSource[] constrainedAudioSources;

    [Header("Per-Stage Tuning")]
    [Tooltip("체크 시 이 스테이지에서만 안개 강도를 덮어쓴다.")]
    [SerializeField] private bool overrideFogIntensity;
    [SerializeField, Range(0f, 1f)] private float fogActiveIntensity = 1f;
    [Tooltip("체크 시 이 스테이지는 공명이 이미 해제된 상태로 시작한다.")]
    [SerializeField] private bool startReleased;

    public Transform HostSpawnPoint => hostSpawnPoint;
    public Transform ClientSpawnPoint => clientSpawnPoint;
    public AudioSource[] ConstrainedAudioSources => constrainedAudioSources;
    public bool OverrideFogIntensity => overrideFogIntensity;
    public float FogActiveIntensity => fogActiveIntensity;
    public bool StartReleased => startReleased;

    // ── 등록 수명 ──────────────────────────────────────────────────────────
    //
    // 등록을 Awake가 아니라 OnEnable에서 하는 이유 (2026-10-19):
    //
    // Opening은 Stage1~3을 Additive로 올려 연출 배경으로만 쓰고, 미션·연출 오브젝트를
    // 이름으로 비활성화한다(OpeningEnvironmentController.disableObjectNames). 그 목록에
    // 이 컨텍스트도 들어 있다 — 세 스테이지 전부.
    //
    // 그런데 Awake에서 등록하면 그 비활성화가 아무 효과가 없었다.
    //   · Awake는 씬 로드 시점에 이미 실행된다. 비활성화는 로드가 끝난 뒤에 수행된다.
    //   · 해제가 OnDestroy에만 있어, GameObject를 꺼도 등록이 남는다.
    // 결과적으로 ResonanceController.hasContext가 true로 유지되고, Opening에는
    // 플레이어가 없어 proximity가 0이므로 제약 안개가 최대 강도로 고정됐다.
    //
    // OnEnable/OnDisable 짝으로 두면 "켜져 있는 동안만 등록"이 되어 그 비활성화가
    // 의도대로 동작한다. 로드 시 등록 시점은 사실상 그대로다 — Unity는 Awake 직후
    // OnEnable을 호출하므로 첫 프레임 전에 등록이 끝난다.
    //
    // 비활성 상태로 authoring된 경우의 동작도 전과 같다. Unity는 비활성 GameObject의
    // Awake도 OnEnable도 부르지 않으므로, 어느 쪽이든 등록되지 않는다.
    //
    // OnDestroy를 남겨 두는 이유:
    // 활성 오브젝트가 파괴될 때는 OnDisable이 먼저 불려 이미 해제되지만, 해제가
    // 한 번 더 불려도 Unregister의 가드(_currentContext != context)가 조용히 무시한다.
    // 공짜인 안전망이라 지우지 않는다.

    private void OnEnable() => ResonanceController.Register(this);
    private void OnDisable() => ResonanceController.Unregister(this);
    private void OnDestroy() => ResonanceController.Unregister(this);
}
