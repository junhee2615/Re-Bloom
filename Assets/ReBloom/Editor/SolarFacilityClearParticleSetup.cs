using ReBloom.Solar;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 태양광 시설 전체 클리어 빛 입자 효과를 테스트하기 위한 Editor 메뉴.
///
/// 메뉴:
///   Re_Bloom / Stage3 / Add Clear Particles to Broken Panels
///   Re_Bloom / Stage3 / Remove Clear Particles from Panels
///   Re_Bloom / Stage3 / Preview Clear Particles (Play Mode)
///
/// 고장 패널 4개 각각에 <see cref="SolarFacilityClearParticle"/> 컴포넌트를 붙인다.
/// 이미 붙어 있으면 건너뛴다. 프리팹 대신 씬 오브젝트에 직접 추가하는 방식이다.
/// </summary>
public static class SolarFacilityClearParticleSetup
{
    private const string ParticleTexturePath =
        "Assets/ReBloom/Environment_Art/Textures/Stage3/Teleporter/Particle.png";

    [MenuItem("Re_Bloom/Stage3/Add Clear Particles to Broken Panels")]
    private static void AddParticlesToPanels()
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ParticleTexturePath);

        SolarReflector[] allReflectors = Object.FindObjectsByType<SolarReflector>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int added = 0;

        foreach (SolarReflector r in allReflectors)
        {
            if (!r.broken) continue;

            SolarPanelCleanView cleanView = r.GetComponentInParent<SolarPanelCleanView>();
            Transform target = cleanView != null ? cleanView.transform : r.transform;

            if (target.GetComponentInChildren<SolarFacilityClearParticle>(true) != null)
            {
                Debug.Log($"  {target.name}: 이미 ClearParticle이 있습니다. 건너뜁니다.", target);
                continue;
            }

            GameObject particleHolder = new GameObject("FacilityClearParticle");
            particleHolder.transform.SetParent(target, false);
            particleHolder.transform.localPosition = Vector3.up * 0.5f;

            SolarFacilityClearParticle comp = particleHolder.AddComponent<SolarFacilityClearParticle>();
            Undo.RegisterCreatedObjectUndo(particleHolder, "Add Clear Particle");

            if (tex != null)
            {
                SerializedObject so = new SerializedObject(comp);
                so.FindProperty("particleTexture").objectReferenceValue = tex;
                so.ApplyModifiedProperties();
            }

            added++;
            Debug.Log($"  {target.name}: FacilityClearParticle 추가 완료.", particleHolder);
        }

        if (added == 0)
        {
            LinkTextureToExisting();
            return;
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[ClearParticleSetup] 고장 패널 {added}개에 FacilityClearParticle을 추가했습니다.\n" +
                  "Play 모드에서 'Re_Bloom → Stage3 → Preview Clear Particles'로 미리보기하세요.");
    }

    [MenuItem("Re_Bloom/Stage3/Link Clear Particle Texture")]
    private static void LinkTextureToExisting()
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ParticleTexturePath);
        if (tex == null)
        {
            Debug.LogError($"[ClearParticleSetup] 텍스처를 찾지 못했습니다: {ParticleTexturePath}");
            return;
        }

        SolarFacilityClearParticle[] particles = Object.FindObjectsByType<SolarFacilityClearParticle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (particles.Length == 0)
        {
            Debug.LogWarning("[ClearParticleSetup] 씬에 FacilityClearParticle이 없습니다.");
            return;
        }

        int linked = 0;
        foreach (SolarFacilityClearParticle p in particles)
        {
            SerializedObject so = new SerializedObject(p);
            SerializedProperty prop = so.FindProperty("particleTexture");
            if (prop.objectReferenceValue != null) continue;

            prop.objectReferenceValue = tex;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(p);
            linked++;
        }

        if (linked > 0)
        {
            EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        }

        Debug.Log($"[ClearParticleSetup] 파티클 텍스처 연결 완료: {linked}개 갱신.");
    }

    [MenuItem("Re_Bloom/Stage3/Remove Clear Particles from Panels")]
    private static void RemoveParticlesFromPanels()
    {
        SolarFacilityClearParticle[] existing = Object.FindObjectsByType<SolarFacilityClearParticle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (existing.Length == 0)
        {
            Debug.Log("[ClearParticleSetup] 씬에 FacilityClearParticle이 없습니다.");
            return;
        }

        foreach (SolarFacilityClearParticle p in existing)
        {
            string name = p.gameObject.name;
            Undo.DestroyObjectImmediate(p.gameObject);
            Debug.Log($"  제거: {name}");
        }

        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[ClearParticleSetup] FacilityClearParticle {existing.Length}개를 제거했습니다.");
    }

    [MenuItem("Re_Bloom/Stage3/Preview Clear Particles (Play Mode)")]
    private static void PreviewParticles()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ClearParticleSetup] Play 모드에서만 미리보기할 수 있습니다. " +
                             "Unity 에디터에서 Play를 누른 뒤 다시 실행하세요.");
            return;
        }

        SolarFacilityClearParticle[] particles = Object.FindObjectsByType<SolarFacilityClearParticle>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        if (particles.Length == 0)
        {
            Debug.LogWarning("[ClearParticleSetup] 씬에 FacilityClearParticle이 없습니다. " +
                             "'Add Clear Particles to Broken Panels'을 먼저 실행하세요.");
            return;
        }

        int played = 0;
        foreach (SolarFacilityClearParticle p in particles)
        {
            if (p.IsPlaying)
            {
                p.Stop();
                Debug.Log($"  {p.name}: 정지.", p);
            }
            else
            {
                p.Play();
                played++;
                Debug.Log($"  {p.name}: 재생 시작.", p);
            }
        }

        Debug.Log(played > 0
            ? $"[ClearParticleSetup] {played}개 파티클 재생 시작. 다시 실행하면 정지합니다."
            : "[ClearParticleSetup] 모든 파티클을 정지했습니다. 다시 실행하면 재생합니다.");
    }
}
