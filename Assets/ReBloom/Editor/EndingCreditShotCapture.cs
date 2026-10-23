using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 엔딩 크레딧에 쓸 장면 6장(Stage1~3 복원 전 / 후)을 씬 뷰에서 찍는 도구.
///
/// 쓰는 법 —
///  1. 해당 스테이지 씬을 열고, 씬 뷰에서 보여 주고 싶은 구도를 잡는다.
///     복원 후 장면은 Play 모드에서 미션을 깨거나 복원 상태로 만든 뒤 씬 뷰로 구도를 잡는다
///     (씬 뷰는 Play 중에도 지금 상태를 그대로 그린다).
///  2. 메뉴 Re_Bloom/Credits/Capture - StageN Before/After 를 누른다.
///  3. 6장을 다 찍으면 Play를 끄고 엔딩 셋업(Apply)을 다시 실행한다 — 크레딧에 자동으로 들어간다.
///
/// 같은 메뉴를 다시 누르면 덮어쓴다. 직접 만든 이미지를 같은 이름으로 넣어도 된다.
/// </summary>
public static class EndingCreditShotCapture
{
    public const string ShotFolder = "Assets/ReBloom/CutScenes/Credits/Shots";

    // 크레딧에 나오는 순서
    public static readonly string[] ShotNames =
    {
        "Stage1_Before", "Stage1_After",
        "Stage2_Before", "Stage2_After",
        "Stage3_Before", "Stage3_After",
    };

    private const int Width = 1920;
    private const int Height = 1080;

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

    // 씬 뷰 카메라의 지금 구도를 1920x1080으로 렌더해 PNG로 저장한다.
    private static void Capture(int index)
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null || view.camera == null)
        {
            EditorUtility.DisplayDialog("크레딧 장면 캡처", "씬 뷰를 열고 구도를 잡은 뒤 다시 실행하세요.", "확인");
            return;
        }

        Camera camera = view.camera;
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            camera.targetTexture = rt;
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
                      (EditorApplication.isPlaying ? " — Play를 끈 뒤 엔딩 셋업(Apply)을 다시 실행하면 크레딧에 들어갑니다." : ""));
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(rt);
        }
    }
}
