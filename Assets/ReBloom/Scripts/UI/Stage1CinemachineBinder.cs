using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

public class Stage1CinemachineBinder : MonoBehaviour
{
    [Header("Timeline")]
    [SerializeField] private PlayableDirector playableDirector;

    [Header("Editor Preview Camera")]
    [Tooltip("편집용 Stage1CutsceneCamera. 런타임 XR 카메라 연결 후 비활성화합니다.")]
    [SerializeField] private GameObject previewCamera;

    private IEnumerator Start()
    {
        // XR Origin은 방 입장 후 생성되므로 기다린다.
        CinemachineBrain runtimeBrain = null;

        while (runtimeBrain == null)
        {
            Camera mainCamera = Camera.main;

            if (mainCamera != null)
                runtimeBrain = mainCamera.GetComponent<CinemachineBrain>();

            yield return null;
        }

        if (playableDirector == null)
        {
            Debug.LogError(
                "[Stage1CinemachineBinder] PlayableDirector가 연결되지 않았습니다.",
                this);

            yield break;
        }

        TimelineAsset timeline =
            playableDirector.playableAsset as TimelineAsset;

        if (timeline == null)
        {
            Debug.LogError(
                "[Stage1CinemachineBinder] TimelineAsset을 찾지 못했습니다.",
                this);

            yield break;
        }

        bool bound = false;

        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (track is CinemachineTrack)
            {
                playableDirector.SetGenericBinding(
                    track,
                    runtimeBrain);

                bound = true;

                Debug.Log(
                    $"[Stage1CinemachineBinder] Cinemachine Track을 " +
                    $"{runtimeBrain.name}에 연결했습니다.",
                    this);
            }
        }

        if (!bound)
        {
            Debug.LogWarning(
                "[Stage1CinemachineBinder] Cinemachine Track을 찾지 못했습니다.",
                this);
        }

        // 편집/Preview용 카메라는 실제 게임에서는 사용하지 않는다.
        // if (previewCamera != null)
        //     previewCamera.SetActive(false);

        // 새 바인딩 반영
        playableDirector.RebuildGraph();
    }
}