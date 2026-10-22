using UnityEngine.Timeline;

/// <summary>
/// 엔딩 연출(손 뻗기, 빛, 안개, 식생, 페이드)을 타임라인 위에 클립으로 올리는 트랙.
/// <see cref="Stage3EndingCutscene"/>에 바인딩한다. 클립 하나가 동작 하나이고,
/// 클립의 시작·길이가 곧 그 연출의 시작·길이다.
/// </summary>
[TrackColor(0.55f, 0.85f, 0.55f)]
[TrackClipType(typeof(EndingActionClip))]
[TrackBindingType(typeof(Stage3EndingCutscene))]
public class EndingActionTrack : TrackAsset
{
}
