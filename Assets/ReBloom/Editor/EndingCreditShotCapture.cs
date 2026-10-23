using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 엔딩 크레딧에 쓸 장면 사진 6장(Stage1~3 복원 전 / 후)을 찍는 도구.
///
/// 쓰는 법 —
///  1. 스테이지 씬을 Play한다. 복원 후 사진이면 그 스테이지를 끝까지 클리어해 둔다.
///  2. 씬 뷰에서 구도를 잡는다(위아래 각도·화각까지 그대로 찍힌다).
///  3. 메뉴 Re_Bloom/Credits/Capture - StageN Before/After 를 누른다.
///  4. 6장을 다 찍으면 Play를 끄고 엔딩 셋업(Apply)을 다시 실행한다 — 크레딧에 자동으로 들어간다.
///
/// 찍을 때 —
///  - Play 중이면 스테이지의 Tutorial Manager Current Tutorial을 복원 전은 None, 복원 후는 AllComplete로 둔다
///    (미션 안내 UI·강조가 사라진다. 이 값만으로 월드가 복원되지는 않으므로 복원 후는 직접 클리어해야 한다).
///  - 씬의 모든 Outline을 끈다.
///  - 씬 뷰 카메라 대신 같은 구도의 임시 카메라로 찍는다 — 게임과 같은 후처리(색감·Bloom)와
///    안티앨리어싱(MSAA 4x + SMAA)이 들어간다.
///
/// 같은 메뉴를 다시 누르면 덮어쓴다. 직접 만든 이미지를 같은 이름으로 넣어도 된다.
/// </summary>
public static class EndingCreditShotCapture
{
    public const string ShotFolder = "Assets/ReBloom/CutScenes/Credits/Shots";

    // 크레딧에 나오는 순서. 짝수 칸이 복원 전, 홀수 칸이 복원 후.
    // 그 뒤의 손 흔들기 컷은 사진이 아니라 엔딩이 실시간으로 그린다(Stage3EndingCutscene.BeginWaveShot).
    public static readonly string[] ShotNames =
    {
        "Stage1_Before", "Stage1_After",
        "Stage2_Before", "Stage2_After",
        "Stage3_Before", "Stage3_After",
    };

    private const int Width = 1920;
    private const int Height = 1080;
    private const int MsaaSamples = 4;

    [MenuItem("Re_Bloom/Credits/Capture - Stage1 Before", priority = 500)]
    private static void Stage1Before() => Capture(0);

    [MenuItem("Re_Bloom/Credits/Capture - Stage1 After", priority = 501)]
    private static void Stage1After() => Capture(1);

    [MenuItem("Re_Bloom/Credits/Capture - Stage2 Before", priority = 502)]
    private static void Stage2Before() => Capture(2);

    [MenuItem("Re_Bloom/Credits/Capture - Stage2 After", priority = 503)]
    private static void Stage2After() => Capture(3);

    [MenuItem("Re_Bloom/Credits/Capture - Stage3 Before", priority = 504)]
    private static void Stage3Before() => Capture(4);

    [MenuItem("Re_Bloom/Credits/Capture - Stage3 After", priority = 505)]
    private static void Stage3After() => Capture(5);

    public static string ShotPath(int index) => $"{ShotFolder}/{ShotNames[index]}.png";

    // 씬 뷰의 지금 구도를 1920x1080으로 렌더해 PNG로 저장한다.
    private static void Capture(int index)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null)
        {
            EditorUtility.DisplayDialog("크레딧 장면 캡처", "씬 뷰를 열고 구도를 잡은 뒤 다시 실행하세요.", "확인");
            return;
        }

        bool restored = index % 2 == 1;
        if (EditorApplication.isPlaying)
            SetTutorialState(restored);
        DisableOutlines();

        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = MsaaSamples };
        rt.Create();
        var cameraObject = new GameObject("CreditShotCamera") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(view.camera.transform.position, view.camera.transform.rotation);
            camera.fieldOfView = view.camera.fieldOfView;
            camera.nearClipPlane = view.camera.nearClipPlane;
            camera.farClipPlane = Mathf.Max(view.camera.farClipPlane, 1000f);
            camera.allowMSAA = true;
            camera.targetTexture = rt;
            camera.enabled = false;   // 이번 한 장만 직접 그린다

            UniversalAdditionalCameraData cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            camera.Render();

            RenderTexture.active = rt;
            var image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            image.Apply();

            Directory.CreateDirectory(ShotFolder);
            string path = ShotPath(index);
            File.WriteAllBytes(path, image.EncodeToPNG());
            Object.DestroyImmediate(image);

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            Debug.Log($"[크레딧 캡처] {ShotNames[index]} 저장: {path}" +
                      (EditorApplication.isPlaying ? " — 6장을 다 찍으면 Play를 끄고 엔딩 셋업(Apply)을 다시 실행하세요." : ""));
        }
        finally
        {
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(cameraObject);
            rt.Release();
            Object.DestroyImmediate(rt);
        }
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

        Debug.LogWarning("[크레딧 캡처] Tutorial Manager를 바꾸지 못했습니다(씬에 없거나, 아직 스폰 전이거나, 호스트가 아닙니다). " +
                         "미션 안내 UI가 사진에 보이지 않는지 확인하세요.");
    }

    private static bool CanWrite(Fusion.NetworkBehaviour manager) =>
        manager != null && manager.Object != null && manager.Object.IsValid && manager.HasStateAuthority;
}
