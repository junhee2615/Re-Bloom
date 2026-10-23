using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// 엔딩 크레딧 오른쪽에 나올 컷씬 영상(Stage1~3 복원 전 / 후)을 녹화하는 도구. Unity Recorder로 mp4를 만든다.
///
/// 쓰는 법 —
///  1. 스테이지 씬을 Play한다. 복원 후 영상이면 그 스테이지를 끝까지 클리어해 둔다.
///  2. 씬 뷰에서 시작 구도를 잡는다(카메라는 여기서 앞으로 천천히 다가가며 살짝 돈다).
///  3. 메뉴 Re_Bloom/Credits/Record - StageN Before/After 를 누른다. <see cref="Duration"/>초 동안 녹화된다.
///  4. 6개를 다 찍으면 Play를 끄고 엔딩 셋업(Apply)을 다시 실행한다 — 크레딧에 자동으로 들어간다.
///
/// 녹화하는 동안 —
///  - 스테이지의 Tutorial Manager Current Tutorial을 복원 전은 None, 복원 후는 AllComplete로 둔다
///    (미션 안내 UI·강조가 사라진다. 이 값만으로 월드가 복원되지는 않으므로 복원 후는 직접 클리어해야 한다).
///  - 씬의 모든 Outline을 꺼 둔다(미션 강조가 다시 켜도 계속 끈다).
///  - 플레이어 시점과 별개인 녹화용 카메라로 찍는다(VR 화면에는 영향이 없다).
/// </summary>
public static class EndingCreditClipRecorder
{
    public const string ClipFolder = "Assets/ReBloom/CutScenes/Credits/Clips";

    private const float Duration = 8f;        // 크레딧의 장면 하나(7초)보다 조금 길게
    private const float DollyDistance = 2.5f; // 그동안 앞으로 다가가는 거리(m)
    private const float YawSweep = 8f;        // 그동안 오른쪽으로 도는 각도(도)
    private const int Width = 1920;
    private const int Height = 1080;
    private const float FrameRate = 30f;

    private static RecorderController controller;
    private static GameObject cameraObject;
    private static RenderTexture target;
    private static Vector3 startPosition;
    private static Quaternion startRotation;
    private static float startTime;
    private static float nextOutlineSweep;
    private static string recordingName;

    [MenuItem("Re_Bloom/Credits/Record - Stage1 Before", priority = 510)]
    private static void Stage1Before() => Record(0, false);

    [MenuItem("Re_Bloom/Credits/Record - Stage1 After", priority = 511)]
    private static void Stage1After() => Record(1, true);

    [MenuItem("Re_Bloom/Credits/Record - Stage2 Before", priority = 512)]
    private static void Stage2Before() => Record(2, false);

    [MenuItem("Re_Bloom/Credits/Record - Stage2 After", priority = 513)]
    private static void Stage2After() => Record(3, true);

    [MenuItem("Re_Bloom/Credits/Record - Stage3 Before", priority = 514)]
    private static void Stage3Before() => Record(4, false);

    [MenuItem("Re_Bloom/Credits/Record - Stage3 After", priority = 515)]
    private static void Stage3After() => Record(5, true);

    public static string ClipPath(int index) => $"{ClipFolder}/{EndingCreditShotCapture.ShotNames[index]}.mp4";

    private static void Record(int index, bool restored)
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("크레딧 영상 녹화",
                "Play 모드에서 녹화합니다.\n스테이지 씬을 Play하고(복원 후는 클리어까지), 씬 뷰로 구도를 잡은 뒤 다시 누르세요.", "확인");
            return;
        }

        if (controller != null)
        {
            Debug.LogWarning($"[크레딧 녹화] {recordingName} 녹화 중입니다. 끝난 뒤 다시 누르세요.");
            return;
        }

        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null)
        {
            EditorUtility.DisplayDialog("크레딧 영상 녹화", "씬 뷰를 열고 시작 구도를 잡은 뒤 다시 누르세요.", "확인");
            return;
        }

        recordingName = EndingCreditShotCapture.ShotNames[index];
        SetTutorialState(restored);
        DisableOutlines();

        // 녹화용 카메라: 씬 뷰 구도에서 시작한다. VR 화면과 상관없이 RenderTexture에만 그린다.
        startPosition = view.camera.transform.position;
        startRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(view.camera.transform.forward, Vector3.up).sqrMagnitude > 0.001f
            ? view.camera.transform.forward
            : Vector3.forward, Vector3.up);

        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        cameraObject = new GameObject("CreditClipCamera");
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 55f;
        camera.stereoTargetEye = StereoTargetEyeMask.None;
        camera.targetTexture = target;
        cameraObject.transform.SetPositionAndRotation(startPosition, startRotation);

        Directory.CreateDirectory(ClipFolder);

        var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movie.name = recordingName;
        movie.Enabled = true;
        movie.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
        };
        movie.ImageInputSettings = new RenderTextureInputSettings { RenderTexture = target };
        movie.AudioInputSettings.PreserveAudio = false;
        movie.OutputFile = Path.GetFullPath(Path.Combine(ClipFolder, recordingName));   // 확장자는 Recorder가 붙인다

        settings.AddRecorderSettings(movie);
        settings.SetRecordModeToManual();
        settings.FrameRate = FrameRate;
        settings.CapFrameRate = true;

        controller = new RecorderController(settings);
        controller.PrepareRecording();
        if (!controller.StartRecording())
        {
            Debug.LogError("[크레딧 녹화] Recorder를 시작하지 못했습니다.");
            Cleanup();
            return;
        }

        startTime = Time.time;
        nextOutlineSweep = 0f;
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;

        Debug.Log($"[크레딧 녹화] {recordingName} 녹화 시작 ({Duration}초, {(restored ? "AllComplete" : "None")}, Outline 끔)");
    }

    // 앞으로 천천히 다가가며 살짝 돈다. 시간이 다 되면 저장한다.
    private static void Tick()
    {
        if (controller == null || cameraObject == null) return;

        float elapsed = Time.time - startTime;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / Duration));

        Quaternion rotation = Quaternion.Euler(0f, YawSweep * k, 0f) * startRotation;
        Vector3 position = startPosition + Vector3.ProjectOnPlane(startRotation * Vector3.forward, Vector3.up).normalized * (DollyDistance * k);
        cameraObject.transform.SetPositionAndRotation(position, rotation);

        // 미션 강조가 Outline을 다시 켜도 계속 끈다
        if (elapsed >= nextOutlineSweep)
        {
            DisableOutlines();
            nextOutlineSweep = elapsed + 0.5f;
        }

        if (elapsed >= Duration)
            Finish();
    }

    private static void Finish()
    {
        string name = recordingName;
        controller.StopRecording();
        Cleanup();

        EditorApplication.delayCall += () =>
        {
            AssetDatabase.Refresh();
            Debug.Log($"[크레딧 녹화] {name} 저장: {ClipFolder}/{name}.mp4 — Play를 끈 뒤 엔딩 셋업(Apply)을 다시 실행하면 크레딧에 들어갑니다.");
        };
    }

    private static void OnPlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.ExitingPlayMode || controller == null) return;

        Debug.LogWarning($"[크레딧 녹화] Play가 끝나 {recordingName} 녹화를 멈춥니다.");
        Finish();
    }

    private static void Cleanup()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;

        controller = null;
        if (cameraObject != null) Object.Destroy(cameraObject);
        cameraObject = null;
        if (target != null) target.Release();
        target = null;
    }

    // ── 씬 상태 ───────────────────────────────────────────────────────────

    private static void DisableOutlines()
    {
        foreach (Outline outline in Object.FindObjectsByType<Outline>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            outline.enabled = false;
    }

    // 지금 씬에 있는 스테이지의 Tutorial Manager를 None / AllComplete로 둔다.
    // [Networked] 값이라 스폰된 뒤, 상태 권한(호스트)에서만 바꿀 수 있다.
    private static void SetTutorialState(bool restored)
    {
        var stage1 = Object.FindFirstObjectByType<TutorialMissionManager>();
        if (CanWrite(stage1))
        {
            stage1.CurrentTutorial = restored ? TutorialStep.AllComplete : TutorialStep.None;
            return;
        }

        var stage2 = Object.FindFirstObjectByType<TutorialMissionManager_2>();
        if (CanWrite(stage2))
        {
            stage2.CurrentTutorial = restored ? TutorialStep_2.AllComplete : TutorialStep_2.None;
            return;
        }

        var stage3 = Object.FindFirstObjectByType<TutorialMissionManager_3>();
        if (CanWrite(stage3))
        {
            stage3.CurrentTutorial = restored ? TutorialStep_3.AllComplete : TutorialStep_3.None;
            return;
        }

        Debug.LogWarning("[크레딧 녹화] Tutorial Manager를 바꾸지 못했습니다(씬에 없거나, 아직 스폰 전이거나, 호스트가 아닙니다). " +
                         "미션 안내 UI가 영상에 보이지 않는지 확인하세요.");
    }

    private static bool CanWrite(Fusion.NetworkBehaviour manager) =>
        manager != null && manager.Object != null && manager.Object.IsValid && manager.HasStateAuthority;
}
