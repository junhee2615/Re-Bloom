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

    private void Awake() => ResonanceController.Register(this);
    private void OnDestroy() => ResonanceController.Unregister(this);
}
