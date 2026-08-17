using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Auditory constraint. Routes the given AudioSources to the Resonance mixer group
/// under constraint (and back to the normal group when released), and relieves the
/// muffle with distance by blending between two mixer snapshots (constrained ↔ relieved),
/// so the whole Resonance effect chain (Lowpass + ParamEQ) morphs together.
/// </summary>
[Serializable]
public sealed class AudioConstraint
{
    [Tooltip("제약 시 Resonance 그룹, 해제 시 Normal(Master) 그룹으로 출력을 전환할 AudioSource들.")]
    [SerializeField] private AudioSource[] constrainedAudioSources;
    [Tooltip("제약 상태의 믹서 그룹 (Resonance).")]
    [SerializeField] private AudioMixerGroup resonanceGroup;
    [Tooltip("제약 해제 상태의 믹서 그룹 (Master).")]
    [SerializeField] private AudioMixerGroup normalGroup;

    [Header("Distance Relief (Snapshot Blend)")]
    [Tooltip("멀리(완전 제약)일 때의 믹서 스냅샷.")]
    [SerializeField] private AudioMixerSnapshot constrainedSnapshot;
    [Tooltip("가까이서 부분 완화된 상태의 믹서 스냅샷.")]
    [SerializeField] private AudioMixerSnapshot relievedSnapshot;

    private AudioMixerSnapshot[] snapshots;
    private readonly float[] weights = new float[2];

    /// <summary>스테이지 전환 시 제약 대상 AudioSource를 교체한다.</summary>
    public void SetSources(AudioSource[] sources) => constrainedAudioSources = sources;

    /// <summary>공명 on/off에 따라 지정 AudioSource의 출력 믹서 그룹을 전환한다.</summary>
    public void Apply(bool inactive)
    {
        if (constrainedAudioSources == null) return;

        AudioMixerGroup target = inactive ? normalGroup : resonanceGroup;
        foreach (AudioSource source in constrainedAudioSources)
            if (source != null)
                source.outputAudioMixerGroup = target;
    }

    /// <summary>
    /// 거리 근접도로 두 스냅샷을 블렌드해 Resonance 이펙트 전체를 완화한다.
    /// </summary>
    public void UpdateRelief(float proximity)
    {
        if (constrainedSnapshot == null || relievedSnapshot == null) return;

        snapshots ??= new[] { constrainedSnapshot, relievedSnapshot };
        weights[0] = 1f - proximity;
        weights[1] = proximity; // 완화 비중

        // timeToReach 0 → 매 프레임 즉시 반영 (연속 블렌드)
        constrainedSnapshot.audioMixer.TransitionToSnapshots(snapshots, weights, 0f);
    }
}
