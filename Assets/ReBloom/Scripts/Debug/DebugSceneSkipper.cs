#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 테스트용 디버그 도구. 키 하나로 지정한 빌드 인덱스의 씬을 강제 로드한다.
/// Fusion에서는 호스트(서버)만 씬 로드를 요청할 수 있으므로 호스트에서만 동작하며,
/// 클라이언트는 호스트가 로드한 씬을 자동으로 따라온다.
/// 에디터와 Development Build에서만 컴파일된다.
/// </summary>
public class DebugSceneSkipper : MonoBehaviour
{
    [SerializeField, Tooltip("이 키를 누르면 Target Scene Build Index 씬으로 이동한다.")]
    private Key skipKey = Key.F9;

    [SerializeField, Tooltip("이동할 씬의 빌드 인덱스. 0=StartScene, 1=Stage1, 2=Stage2, 3=Stage2-1")]
    private int targetSceneBuildIndex = 2;

    [SerializeField, Tooltip("씬 로드 전에 화면을 페이드 아웃한다. 로컬(누른 사람) 화면에만 적용된다.")]
    private bool fadeBeforeLoad = true;

    [SerializeField] private float fadeDuration = 0.5f;

    [SerializeField, Tooltip("비워두면 씬에서 자동으로 찾는다. Stage1에서는 TrainFloor 오브젝트에 있다.")]
    private CutscenePlayer cutscenePlayer;

    private bool loadTriggered;

    // private void Start()
    // {
    //     if (cutscenePlayer == null)
    //         cutscenePlayer = FindFirstObjectByType<CutscenePlayer>();
    // }


    private void Update()
    {
        if (loadTriggered)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[skipKey].wasPressedThisFrame)
            return;

        TrySkip();
    }

    private void TrySkip()
    {
        NetworkRunner runner = NetworkManager.Instance != null ? NetworkManager.Instance.Runner : null;

        if (runner == null || !runner.IsRunning)
        {
            Debug.LogWarning("[DebugSceneSkipper] NetworkRunner가 없습니다. StartScene에서 방을 만든 뒤 시도하세요.", this);
            return;
        }

        if (!runner.IsServer)
        {
            Debug.LogWarning("[DebugSceneSkipper] 클라이언트는 씬을 로드할 수 없습니다. 호스트에서 눌러주세요.", this);
            return;
        }

        // if (cutscenePlayer == null)
        //     cutscenePlayer = FindFirstObjectByType<CutscenePlayer>();

        if (cutscenePlayer == null)
        {
            Debug.LogWarning("[DebugSceneSkipper] 씬에서 CutscenePlayer를 찾지 못했습니다. Stage1의 TrainFloor 오브젝트를 확인하세요.", this);
            return;
        }

        loadTriggered = true;
        Debug.Log("[DebugSceneSkipper] 컷신을 강제로 시작합니다.", this);
        cutscenePlayer.BeginCutscene();
    }

    // private IEnumerator SkipRoutine(NetworkRunner runner) // beginCutscene?
    // {
    //     Debug.Log($"[DebugSceneSkipper] 빌드 인덱스 {targetSceneBuildIndex} 씬으로 강제 이동합니다.", this);
    //
    //     if (fadeBeforeLoad)
    //     {
    //         ScreenFade screenFade = FindFirstObjectByType<ScreenFade>();
    //         if (screenFade != null)
    //             yield return screenFade.FadeOut(fadeDuration);
    //         else
    //             Debug.LogWarning("[DebugSceneSkipper] ScreenFade를 찾지 못해 페이드 없이 이동합니다.", this);
    //     }
    //
    //     runner.LoadScene(SceneRef.FromIndex(targetSceneBuildIndex));
    // }
}
#endif
