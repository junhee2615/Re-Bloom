using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 지금 열린 씬(Play 중이면 실행 중인 씬)에서 화면에 핑크색으로 보이는 렌더러를 찾는 도구.
///
/// Unity는 다음 경우 오브젝트를 형광 핑크로 그린다 —
///  - 렌더러에 머티리얼이 비어 있다
///  - 머티리얼의 셰이더에 컴파일 오류가 있다(셰이더 그래프를 고치다 오류가 난 경우 포함)
///  - 셰이더가 지금 렌더 파이프라인(URP)·기기에서 지원되지 않는다(Built-in·HDRP 셰이더 등)
///
/// 찾은 오브젝트는 Console에 경로·머티리얼·셰이더 이름과 이유를 남기고 Hierarchy에서 선택해 둔다.
/// 씬은 바꾸지 않는다.
/// </summary>
public static class BrokenMaterialFinder
{
    [MenuItem("Re_Bloom/Tools/Find Pink (Broken) Materials", priority = 900)]
    private static void Find()
    {
        var found = new List<GameObject>();
        int checkedRenderers = 0;

        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;

            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                checkedRenderers++;
                Material[] materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    string problem = Diagnose(materials[i]);
                    if (problem == null) continue;

                    string matName = materials[i] != null ? materials[i].name : "(없음)";
                    string shaderName = materials[i] != null && materials[i].shader != null ? materials[i].shader.name : "(없음)";
                    string state = renderer.gameObject.activeInHierarchy && renderer.enabled ? "보임" : "꺼져 있음";

                    Debug.LogWarning($"[핑크 찾기] {Path(renderer.transform)} ({state}) — 머티리얼[{i}] {matName}, 셰이더 {shaderName}: {problem}",
                        renderer.gameObject);
                    found.Add(renderer.gameObject);
                }
            }
        }

        Selection.objects = found.Distinct().Cast<Object>().ToArray();
        Debug.Log(found.Count == 0
            ? $"[핑크 찾기] 렌더러 {checkedRenderers}개를 검사했고 문제를 찾지 못했습니다. 파티클·UI·데칼이면 해당 컴포넌트의 머티리얼도 확인하세요."
            : $"[핑크 찾기] 렌더러 {checkedRenderers}개 중 {found.Distinct().Count()}개에서 문제를 찾아 선택해 두었습니다.");
    }

    private static string Diagnose(Material material)
    {
        if (material == null)
            return "머티리얼이 비어 있음";

        Shader shader = material.shader;
        if (shader == null)
            return "셰이더가 없음";
        if (shader.name == "Hidden/InternalErrorShader")
            return "셰이더를 찾지 못해 오류 셰이더로 대체됨";
        if (ShaderUtil.ShaderHasError(shader))
            return "셰이더에 컴파일 오류가 있음";
        if (!shader.isSupported)
            return "이 렌더 파이프라인·기기에서 지원하지 않는 셰이더";

        return null;
    }

    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
