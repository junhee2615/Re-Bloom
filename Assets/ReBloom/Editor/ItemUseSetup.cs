using UnityEditor;
using UnityEngine;

/// <summary>
/// 아이템 사용 기능에 필요한 컴포넌트를 플레이어 프리팹에 붙이는 셋업 도구.
///
/// 붙이는 것은 <see cref="ItemSensoryEffect"/> 하나뿐이다.
/// <c>PlayerItemInventory</c> 와 <c>ItemPickupInput</c> 은 이미 두 프리팹에 있고,
/// 사용 흐름은 그 두 컴포넌트를 수정해 넣었으므로 추가 배선이 없다.
///
/// 계층 (변경 없음 — 루트에 컴포넌트만 추가)
/// <code>
/// Player_ear / Player_mental (루트)
/// ├── NetworkPlayer                  (기존)
/// ├── CooperativeActivationController (기존)
/// ├── PlayerItemInventory            (기존 — 사용 상태를 네트워크로 소유)
/// ├── ItemPickupInput                (기존 — Y 버튼 획득/사용 분기)
/// └── ItemSensoryEffect              ← 신규. 로컬 전용 제약 해제 연출
/// </code>
///
/// 안전 규칙 —
///  · 이미 붙어 있으면 <b>중복 추가하지 않는다</b>. 몇 번 실행해도 결과가 같다.
///  · 두 프리팹의 루트 컴포넌트만 건드린다. Transform·자식·다른 컴포넌트는 읽기만 한다.
///  · <see cref="ItemSensoryEffect"/> 는 연결할 참조가 없다(같은 오브젝트에서
///    <c>GetComponent</c> 로 찾는다). 따라서 Inspector 수동 배선이 필요 없다.
/// </summary>
public static class ItemUseSetup
{
    private static readonly string[] PrefabPaths =
    {
        "Assets/ReBloom/Prefabs/Characters/Player_ear.prefab",
        "Assets/ReBloom/Prefabs/Characters/Player_mental.prefab",
    };

    [MenuItem("Re_Bloom/Items/Item Use Setup - Dry Run", priority = 340)]
    private static void DryRun() => Run(false);

    [MenuItem("Re_Bloom/Items/Item Use Setup - Apply", priority = 341)]
    private static void Apply() => Run(true);

    [MenuItem("Re_Bloom/Items/Item Use Setup - Validate", priority = 342)]
    private static void ValidateMenu() => Validate();

    private static void Run(bool apply)
    {
        string mode = apply ? "APPLY" : "DRY RUN";
        Debug.Log($"===== 아이템 사용 셋업 [{mode}] =====");

        int changed = 0;
        bool failed = false;

        foreach (string path in PrefabPaths)
        {
            Debug.Log($"--- {path}");

            GameObject root = PrefabUtility.LoadPrefabContents(path);

            if (root == null)
            {
                Debug.LogError($"  ✗ 프리팹을 열 수 없습니다: {path}");
                failed = true;
                continue;
            }

            bool dirty = false;

            try
            {
                // 전제 확인 — 이 둘이 없으면 사용 흐름이 성립하지 않는다.
                if (root.GetComponent<PlayerItemInventory>() == null)
                {
                    Debug.LogError("  ✗ PlayerItemInventory가 없습니다. " +
                                   "'Stage1 Item Setup' 단계가 끝났는지 확인하세요.");
                    failed = true;
                    continue;
                }

                if (root.GetComponent<ItemPickupInput>() == null)
                {
                    Debug.LogWarning("  ! ItemPickupInput이 없습니다. Y 버튼 입력이 동작하지 않습니다.");
                }

                // ItemSensoryEffect
                if (root.GetComponent<ItemSensoryEffect>() != null)
                {
                    Debug.Log("  = ItemSensoryEffect 이미 있음 (중복 추가하지 않음)");
                }
                else if (apply)
                {
                    root.AddComponent<ItemSensoryEffect>();
                    Debug.Log("  + ItemSensoryEffect 추가");
                    dirty = true;
                    changed++;
                }
                else
                {
                    Debug.Log("  + ItemSensoryEffect 추가 예정");
                    changed++;
                }

                if (apply && dirty)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);

                    if (success)
                        Debug.Log($"  프리팹을 저장했습니다: {path}");
                    else
                    {
                        Debug.LogError($"  ✗ 프리팹 저장 실패: {path}");
                        failed = true;
                    }
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        if (!apply)
        {
            Debug.Log($"DRY RUN이므로 저장하지 않았습니다. 추가 예정 {changed}건. " +
                      "적용하려면 Apply를 실행하세요.");
            return;
        }

        Debug.Log($"셋업 완료 — {changed}건 추가" + (failed ? "  (문제 있음, 위 로그 확인)" : ""));

        Validate();
    }

    private static void Validate()
    {
        Debug.Log("===== 아이템 사용 셋업 [VALIDATE] =====");

        int problems = 0;

        foreach (string path in PrefabPaths)
        {
            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

            if (asset == null)
            {
                Debug.LogError($"  ✗ 프리팹이 없습니다: {path}");
                problems++;
                continue;
            }

            Debug.Log($"--- {asset.name}");

            problems += Expect(asset.GetComponent<NetworkPlayer>() != null, "NetworkPlayer", asset);
            problems += Expect(asset.GetComponent<PlayerItemInventory>() != null, "PlayerItemInventory", asset);
            problems += Expect(asset.GetComponent<ItemPickupInput>() != null, "ItemPickupInput", asset);
            problems += Expect(asset.GetComponent<ItemSensoryEffect>() != null, "ItemSensoryEffect", asset);

            int count = asset.GetComponents<ItemSensoryEffect>().Length;
            problems += Expect(count <= 1, $"ItemSensoryEffect 1개 이하 (현재 {count}개)", asset);

            // 효과 시간은 PlayerItemInventory의 private 직렬화 필드다.
            var inventory = asset.GetComponent<PlayerItemInventory>();

            if (inventory != null)
            {
                var so = new SerializedObject(inventory);
                SerializedProperty seconds = so.FindProperty("effectSeconds");

                if (seconds == null)
                {
                    Debug.LogError("  ✗ PlayerItemInventory에 'effectSeconds' 필드가 없습니다.", asset);
                    problems++;
                }
                else
                {
                    Debug.Log($"  · effectSeconds = {seconds.floatValue}", asset);
                }
            }
        }

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음."
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    private static int Expect(bool condition, string label, Object context)
    {
        if (condition)
        {
            Debug.Log($"  ✓ {label}", context);
            return 0;
        }

        Debug.LogError($"  ✗ {label}", context);
        return 1;
    }
}
