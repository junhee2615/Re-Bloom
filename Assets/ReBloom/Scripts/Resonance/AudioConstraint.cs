using System;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Routes the given AudioSources to the Resonance mixer group under constraint and
/// back to the normal group when released, giving the auditory constraint effect.
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

    /// <summary>공명 on/off에 따라 지정 AudioSource의 출력 믹서 그룹을 전환한다.</summary>
    public void Apply(bool inactive)
    {
        if (constrainedAudioSources == null) return;

        AudioMixerGroup target = inactive ? normalGroup : resonanceGroup;
        foreach (AudioSource source in constrainedAudioSources)
            if (source != null)
                source.outputAudioMixerGroup = target;
    }
}
