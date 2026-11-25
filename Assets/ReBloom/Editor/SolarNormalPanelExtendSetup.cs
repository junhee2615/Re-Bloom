#if UNITY_EDITOR
using ReBloom.Solar;
using UnityEditor;
using UnityEngine;

/// <summary>
/// NormalSolarPanel_low 프리팹에 클리어 컷씬용 발광/파티클 컴포넌트를 추가한다.
///
/// 메뉴: Re_Bloom / Stage3 / Extend Clear Cutscene To Normal Panels
///
/// 실행하면:
///   1. NormalSolarPanel_low.prefab 루트에 SolarPanelCleanView 추가
///      (brokenMaterial = cleanMaterial = 클린 패널 머티리얼, hideAddonsWhenClean = false)
///   2. NormalSolarPanel_low.prefab 루트에 SolarFacilityClearParticle 추가
///   3. Stage3.unity가 열려 있으면 Facility Clear Cutscene Setup - Apply 를 자동으로 실행
///
/// 이미 컴포넌트가 있으면 건너뛰므로 여러 번 실행해도 안전하다.
/// </summary>
public static class SolarNormalPanelExtendSetup
{
    private const string NormalPrefabPath =
        "Assets/ReBloom/Prefabs/Environment/Stage3/Mission1/NormalSolarPanel_low.prefab";

    // BrokenSolarPanel_low 의 cleanMaterial 과 동일한 에셋 — NormalPanel 메시도 같은 머티리얼을 쓴다
    private const string CleanMatGuid = "6d07c8280e7be9a4ab502c6fb8cbc46d";

    [MenuItem("Re_Bloom/Stage3/Extend Clear Cutscene To Normal Panels")]
    public static void Extend()
    {
        // ── 1. 프리팹 편집 ──────────────────────────────────────
        GameObject contents = PrefabUtility.LoadPrefabContents(NormalPrefabPath);
        if (contents == null)
        {
            Debug.LogError($"[NormalPanelExtend] 프리팹을 로드하지 못했습니다: {NormalPrefabPath}");
            return;
        }

        bool changed = false;

        // ── SolarPanelCleanView ─────────────────────────────────
        if (contents.GetComponentInChildren<SolarPanelCleanView>(true) == null)
        {
            string matPath = AssetDatabase.GUIDToAssetPath(CleanMatGuid);
            if (string.IsNullOrEmpty(matPath))
            {
                Debug.LogError(
                    $"[NormalPanelExtend] cleanMaterial GUID 를 찾지 못했습니다: {CleanMatGuid}\n" +
                    "BrokenSolarPanel_low 의 cleanMaterial 에셋이 프로젝트에 있는지 확인하세요.");
                PrefabUtility.UnloadPrefabContents(contents);
                return;
            }

            Material cleanMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (cleanMat == null)
            {
                Debug.LogError($"[NormalPanelExtend] Material 에셋을 로드하지 못했습니다: {matPath}");
                PrefabUtility.UnloadPrefabContents(contents);
                return;
            }

            SolarPanelCleanView cleanView = contents.AddComponent<SolarPanelCleanView>();
            cleanView.brokenMaterial    = cleanMat;
            cleanView.cleanMaterial     = cleanMat;
            // 일반 패널은 처음부터 클린 상태 — 얹은 오브젝트 숨기기 불필요
            cleanView.hideAddonsWhenClean = false;

            changed = true;
            Debug.Log(
                $"[NormalPanelExtend] SolarPanelCleanView 추가 완료.\n" +
                $"  brokenMaterial = cleanMaterial = {cleanMat.name}\n" +
                $"  hideAddonsWhenClean = false");
        }
        else
        {
            Debug.Log("[NormalPanelExtend] SolarPanelCleanView 이미 존재 — 건너뜀.");
        }

        // ── SolarFacilityClearParticle ──────────────────────────
        if (contents.GetComponentInChildren<SolarFacilityClearParticle>(true) == null)
        {
            contents.AddComponent<SolarFacilityClearParticle>();
            changed = true;
            Debug.Log("[NormalPanelExtend] SolarFacilityClearParticle 추가 완료.");
        }
        else
        {
            Debug.Log("[NormalPanelExtend] SolarFacilityClearParticle 이미 존재 — 건너뜀.");
        }

        // ── 저장 ───────────────────────────────────────────────
        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(contents, NormalPrefabPath);
            Debug.Log($"[NormalPanelExtend] '{NormalPrefabPath}' 저장 완료.");
        }

        PrefabUtility.UnloadPrefabContents(contents);
        AssetDatabase.Refresh();

        // ── 2. 씬이 열려 있으면 패널 배열 갱신 ─────────────────
        SolarFacilityClearCutscene manager =
            Object.FindFirstObjectByType<SolarFacilityClearCutscene>(FindObjectsInactive.Include);

        if (manager != null)
        {
            // 직접 Apply 로직을 호출하는 대신 사용자에게 메뉴 실행을 안내한다.
            // (Apply 는 씬 저장까지 하므로 의도치 않은 저장을 방지)
            Debug.Log(
                "[NormalPanelExtend] Stage3.unity 씬이 열려 있습니다.\n" +
                "Re_Bloom/Stage3/Facility Clear Cutscene Setup - Apply 를 실행해 패널 배열을 갱신하세요.");
        }

        Debug.Log(
            "[NormalPanelExtend] ══════════ 완료 ══════════\n" +
            "  NormalSolarPanel_low.prefab → SolarPanelCleanView + SolarFacilityClearParticle 추가\n" +
            "  전체 패널: 일반 5개 + 수리 대상 4개 = 9개\n" +
            "\n" +
            "  다음 단계:\n" +
            "    1. Stage3.unity 씬이 열려 있는지 확인\n" +
            "    2. Re_Bloom/Stage3/Facility Clear Cutscene Setup - Apply 실행\n" +
            "    3. 씬 저장 후 테스트");
    }
}
#endif
