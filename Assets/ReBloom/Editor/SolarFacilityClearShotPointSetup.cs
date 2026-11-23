using ReBloom.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Stage 3 태양광 시설 전체 클리어 컷씬용 ShotPoint를 생성한다.
///
/// 메뉴: Re_Bloom / Stage3 / Create Facility Clear Shot Point
///
/// 고장 패널 4개의 중심에서 평균 법선 방향으로 떨어진 전망 위치에
/// 빈 GameObject를 배치한다. 이미 존재하면 아무것도 하지 않는다.
///
/// ShotPoint의 Transform은 VR 카메라(HMD)가 놓일 목표 위치·방향이다.
/// 런타임에 XR Origin을 옮겨 카메라 헤드를 이 자리에 맞추는 방식은
/// <see cref="SolarPanelRepairCutscene"/>의 ApplyShotTransform과 같다:
///   xrOrigin.position += shotPoint.position - xrHead.position
/// 즉 ShotPoint는 XR Origin 위치가 아니라 카메라(눈) 위치를 가리킨다.
/// </summary>
public static class SolarFacilityClearShotPointSetup
{
    private const string ShotPointName = "SolarFacilityClearShotPoint";
    private const float ShotDistance = 8f;
    private const float ShotHeight = 2f;

    [MenuItem("Re_Bloom/Stage3/Create Facility Clear Shot Point")]
    private static void CreateShotPoint()
    {
        GameObject existing = GameObject.Find(ShotPointName);
        if (existing != null)
        {
            Debug.Log($"[FacilityClearShotPoint] '{ShotPointName}'이(가) 이미 존재합니다. " +
                      "위치를 초기화하지 않고 그대로 둡니다. Inspector에서 수동으로 조절하세요.",
                      existing);
            Selection.activeGameObject = existing;
            return;
        }

        SolarReflector[] allReflectors = Object.FindObjectsByType<SolarReflector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        Vector3 center = Vector3.zero;
        Vector3 normalSum = Vector3.zero;
        int count = 0;

        foreach (SolarReflector r in allReflectors)
        {
            if (!r.broken) continue;
            center += r.transform.position;
            normalSum += r.Normal;
            count++;
            Debug.Log($"  패널: {r.name} 위치={r.transform.position} 법선={r.Normal}", r);
        }

        if (count == 0)
        {
            Debug.LogError("[FacilityClearShotPoint] 씬에서 고장 패널(SolarReflector.broken=true)을 " +
                           "찾지 못했습니다. Stage 3 씬을 열고 실행하세요.");
            return;
        }

        center /= count;

        Vector3 horizontal = Vector3.ProjectOnPlane(normalSum / count, Vector3.up);
        if (horizontal.sqrMagnitude < 0.01f)
            horizontal = Vector3.forward;
        horizontal = horizontal.normalized;

        Vector3 position = center + horizontal * ShotDistance + Vector3.up * ShotHeight;

        Vector3 lookDir = center - position;
        Quaternion rotation = lookDir.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(lookDir.normalized, Vector3.up)
            : Quaternion.identity;

        GameObject go = new GameObject(ShotPointName);
        go.transform.SetPositionAndRotation(position, rotation);
        Undo.RegisterCreatedObjectUndo(go, "Create Facility Clear Shot Point");

        Scene scene = go.scene;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = go;

        Debug.Log($"[FacilityClearShotPoint] '{ShotPointName}' 생성 완료:\n" +
                  $"  패널 {count}개의 중심: ({center.x:F1}, {center.y:F1}, {center.z:F1})\n" +
                  $"  ShotPoint 위치: ({position.x:F1}, {position.y:F1}, {position.z:F1})\n" +
                  $"  법선 방향 거리: {ShotDistance}m, 높이 오프셋: {ShotHeight}m\n" +
                  $"  Inspector에서 위치·방향을 자유롭게 조절하세요.", go);
    }
}
