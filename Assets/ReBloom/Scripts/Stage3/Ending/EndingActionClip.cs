using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>엔딩 타임라인 클립이 하는 일.</summary>
public enum EndingAction
{
    /// <summary>두 캐릭터가 내려 둔 손을 올려 손바닥을 맞댄다. 클립 끝 = 손바닥이 닿는 순간.</summary>
    HandReach,

    /// <summary>맞댄 손 근처에서 빛이 터지고 잦아든다.</summary>
    HandGlow,

    /// <summary>안개가 걷힌다. 클립 길이 = 걷히는 시간.</summary>
    FogClear,

    /// <summary>식생 매니저(VegetationManager)의 식생이 자란다. 클립 길이 = 자라는 시간.</summary>
    VegetationGrow,

    /// <summary>화면이 검게 닫힌다.</summary>
    FadeOut,

    /// <summary>맞댔던 손을 다리 옆으로 내린다.</summary>
    HandRelease,

    /// <summary>두 캐릭터가 온실 밖 WalkTarget까지 걸어가 멈춰 선다. 클립 길이 = 걷는 시간.</summary>
    Walk,

    /// <summary>Flower&amp;Tree&amp;Building&amp;Ivy의 자식들이 가까운 것부터 차례로 자란다.</summary>
    GroupGrow,
}

/// <summary>
/// <see cref="EndingActionTrack"/>의 클립. 재생 중 매 프레임 진행도(0~1)를
/// <see cref="Stage3EndingCutscene.ApplyAction"/>에 넘긴다.
/// </summary>
public class EndingActionClip : PlayableAsset, ITimelineClipAsset
{
    public EndingAction action;

    public ClipCaps clipCaps => ClipCaps.None;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
    {
        ScriptPlayable<EndingActionBehaviour> playable = ScriptPlayable<EndingActionBehaviour>.Create(graph);
        playable.GetBehaviour().action = action;
        return playable;
    }
}

public class EndingActionBehaviour : PlayableBehaviour
{
    public EndingAction action;

    private Stage3EndingCutscene target;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        target = playerData as Stage3EndingCutscene;
        if (target == null) return;

        double duration = playable.GetDuration();
        float progress = duration > 0d ? (float)(playable.GetTime() / duration) : 1f;
        target.ApplyAction(action, Mathf.Clamp01(progress), (float)duration);
    }

    // 프레임이 클립 끝을 건너뛰어도 마지막 상태(진행도 1)는 반드시 적용한다.
    public override void OnBehaviourPause(Playable playable, FrameData info)
    {
        if (target == null) return;

        double duration = playable.GetDuration();
        if (playable.GetTime() + info.deltaTime >= duration)
            target.ApplyAction(action, 1f, (float)duration);
    }
}
