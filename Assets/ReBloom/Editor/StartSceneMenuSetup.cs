#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ReBloom.EditorTools
{
    /// <summary>
    /// StartScene 타이틀 화면 1~2단계(데모 UI 정리 + MenuCanvas 이동)를 수행하는 일회성 도구.
    ///
    /// 왜 씬 파일을 직접 고치지 않고 에디터 스크립트인가:
    /// MultiBtn은 UI Sample.prefab → ModalSingleButton.prefab → TextButton.prefab 으로
    /// 3단계 중첩된 프리팹 내부 오브젝트다. Unity는 프리팹 인스턴스의 구조 변경(부모 바꾸기)을
    /// 허용하지 않으므로 씬 YAML에 부모 변경을 적어 넣을 방법이 없다. Unpack이 선행되어야 하고
    /// Unpack은 Unity API로만 안전하게 수행할 수 있다.
    ///
    /// 로그 규칙:
    /// 한 번의 Debug.Log에 여러 줄을 모아 찍으면 Unity Console 목록에는 앞의 두 줄만 보인다.
    /// 그래서 확인해야 할 사실은 반드시 한 줄에 하나씩 따로 찍는다.
    ///
    /// Undo 규칙:
    /// PrefabUtility.UnpackPrefabInstance는 InteractionMode.AutomatedAction으로 호출하면
    /// Undo에 기록되지 않는다. 그 상태로 Ctrl+Z를 누르면 Unpack은 남고 그 뒤 작업만 되돌아가
    /// 씬이 어중간하게 망가진다. 그래서 반드시 InteractionMode.UserAction을 쓰고,
    /// 그래도 Undo를 신뢰하지 말라고 Apply 확인 창에서 경고한다.
    ///
    /// 작업이 끝나면 이 파일은 지워도 된다.
    /// </summary>
    public static class StartSceneMenuSetup
    {
        private const string Tag = "[StartSceneMenuSetup]";

        private const string SceneName = "StartScene";
        private const string MenuCanvasName = "MenuCanvas";
        private const string UiSampleName = "UI Sample";
        private const string SingleBtnName = "SingleBtn";
        private const string MultiBtnName = "MultiBtn";

        private const string SingleMethod = "EnterSingle";
        private const string MultiMethod = "EnterMulti";

        // 조사에서 정한 배치값. Lobby의 검증된 World Space UI(거리 3.13m, scale 0.01)와 같은 체계다.
        private static readonly Vector3 MenuCanvasPosition = new Vector3(0f, 1.35f, 2.8f);
        private static readonly Vector3 MenuCanvasScale = new Vector3(0.01f, 0.01f, 0.01f);
        private static readonly Vector2 MenuCanvasSize = new Vector2(260f, 140f);

        private static readonly Vector2 ButtonSize = new Vector2(110f, 28f);
        private static readonly Vector2 SinglePosition = new Vector2(0f, 20f);
        private static readonly Vector2 MultiPosition = new Vector2(0f, -20f);

        private const string SingleLabel = "혼자 시작";
        private const string MultiLabel = "함께 시작";

        // 끌 데모 오브젝트. 버튼을 품고 있는 계층은 아래에서 따로 걸러낸다.
        private static readonly string[] DemoObjectNames =
        {
            "Interactive Controls",
            "Scroll UI Sample",
            "Header Text",
            "Modal Text",
        };

        /// <summary>버튼 안에서 찾아낸 텍스트 컴포넌트. TMP이거나 레거시 Text 둘 중 하나다.</summary>
        private sealed class LabelTarget
        {
            public GameObject Button;
            public TMP_Text Tmp;
            public Text Legacy;

            public bool Found => Tmp != null || Legacy != null;

            public string Kind => Tmp != null ? "TextMeshProUGUI" : (Legacy != null ? "UnityEngine.UI.Text" : "없음");

            public GameObject Host => Tmp != null ? Tmp.gameObject : (Legacy != null ? Legacy.gameObject : null);
        }

        /// <summary>버튼 하나의 사전 검증 결과.</summary>
        private sealed class ButtonCheck
        {
            public string Name;
            public string ExpectedMethod;
            public GameObject Target;
            public Button Button;
            public bool Passed;
        }

        [MenuItem("Re_Bloom/StartScene/Menu UI Setup - Dry Run", false, 10)]
        private static void DryRun()
        {
            Execute(true);
        }

        [MenuItem("Re_Bloom/StartScene/Menu UI Setup - Apply", false, 11)]
        private static void Apply()
        {
            Execute(false);
        }

        /// <summary>
        /// 이미 MenuCanvas로 옮겨진 버튼의 **라벨만** 다시 넣는 복구용 메뉴.
        ///
        /// 이 메뉴가 하지 않는 일:
        ///   - Unpack
        ///   - 부모 변경 (SetTransformParent 호출 없음)
        ///   - MenuCanvas 생성/변경
        ///   - RectTransform 변경
        ///   - Button.onClick 변경 (읽어서 로그로 보여주기만 한다)
        ///   - 폰트 / Font Size 변경
        ///
        /// 두 버튼과 두 텍스트 컴포넌트가 모두 확인되기 전에는 한 글자도 쓰지 않는다.
        /// </summary>
        [MenuItem("Re_Bloom/StartScene/Fix Menu Button Labels", false, 30)]
        private static void FixMenuButtonLabels()
        {
            Debug.Log($"{Tag} ===== Fix Menu Button Labels 시작 =====");

            Scene scene = SceneManager.GetActiveScene();

            if (scene.name != SceneName)
            {
                Debug.LogError($"{Tag} 활성 씬이 '{scene.name}' 입니다. '{SceneName}'을 열고 다시 실행하세요.");
                return;
            }

            // ── 1단계: 검사만 한다. 여기서는 아무것도 쓰지 않는다. ──
            GameObject single = ResolveUnique(scene, SingleBtnName);
            GameObject multi = ResolveUnique(scene, MultiBtnName);

            if (single == null || multi == null)
            {
                Debug.LogError($"{Tag} ===== 버튼을 확정하지 못해 아무것도 수정하지 않았습니다. =====");
                return;
            }

            LabelTarget singleLabel = FindLabel(single);
            LabelTarget multiLabel = FindLabel(multi);

            ReportLabel(SingleBtnName, singleLabel);
            ReportLabel(MultiBtnName, multiLabel);

            if (!singleLabel.Found || !multiLabel.Found)
            {
                Debug.LogError($"{Tag} ===== 텍스트 컴포넌트를 찾지 못해 아무것도 수정하지 않았습니다. =====");
                return;
            }

            // onClick은 읽기만 한다. 이 메뉴는 onClick에 절대 쓰지 않는다.
            Debug.Log($"{Tag} {SingleBtnName} onClick (읽기 전용) = {DescribeOnClick(single)}");
            Debug.Log($"{Tag} {MultiBtnName} onClick (읽기 전용) = {DescribeOnClick(multi)}");

            // ── 2단계: 라벨만 쓴다. ────────────────────────────────
            Undo.SetCurrentGroupName("Fix Menu Button Labels");
            int undoGroup = Undo.GetCurrentGroup();

            SetLabel(singleLabel, SingleLabel);
            SetLabel(multiLabel, MultiLabel);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            Debug.Log($"{Tag} ===== 라벨 2개만 변경했습니다. Ctrl+Z 1회로 되돌릴 수 있습니다. =====");
        }

        private static void ReportLabel(string buttonName, LabelTarget target)
        {
            if (!target.Found)
            {
                Debug.LogError($"{Tag} {buttonName}: 하위에서 TMP도 UnityEngine.UI.Text도 찾지 못했습니다.");
                return;
            }

            string current = target.Tmp != null ? target.Tmp.text : target.Legacy.text;
            float size = target.Tmp != null ? target.Tmp.fontSize : target.Legacy.fontSize;

            Debug.Log(
                $"{Tag} {buttonName}: 텍스트 컴포넌트 = {target.Kind} on {Path(target.Host)}, " +
                $"현재 text=\"{current}\", size={size}");
        }

        private static void Execute(bool dryRun)
        {
            Debug.Log($"{Tag} ===== {(dryRun ? "DRY RUN" : "APPLY")} 시작 =====");

            Scene scene = SceneManager.GetActiveScene();

            if (scene.name != SceneName)
            {
                Debug.LogError($"{Tag} 활성 씬이 '{scene.name}' 입니다. '{SceneName}'을 열고 다시 실행하세요.");
                return;
            }

            Debug.Log($"{Tag} 활성 씬: {scene.name}");

            // ── 사전 검증: Dry Run과 Apply가 똑같이 수행한다 ─────────
            ButtonCheck single = CheckButton(scene, SingleBtnName, SingleMethod);
            ButtonCheck multi = CheckButton(scene, MultiBtnName, MultiMethod);

            ReportContext(scene);

            bool allPassed = single.Passed && multi.Passed;

            if (!allPassed)
            {
                Debug.LogError(
                    $"{Tag} ===== 검증 실패 — {SingleBtnName}={(single.Passed ? "PASS" : "FAIL")}, " +
                    $"{MultiBtnName}={(multi.Passed ? "PASS" : "FAIL")}. Apply를 실행하지 않습니다. =====");
                return;
            }

            Debug.Log($"{Tag} ===== 검증 통과 — 두 버튼 모두 정상입니다. =====");

            if (dryRun)
            {
                Debug.Log($"{Tag} DRY RUN이므로 아무것도 바꾸지 않았습니다.");
                return;
            }

            // ── Apply 전 마지막 확인 ────────────────────────────────
            bool confirmed = EditorUtility.DisplayDialog(
                "StartScene 메뉴 UI 정리",
                "UI Sample / ModalSingleButton 프리팹 인스턴스를 Unpack하고\n" +
                "SingleBtn / MultiBtn을 새 MenuCanvas로 옮깁니다.\n\n" +
                "Unpack은 Undo가 불안정합니다.\n" +
                "실행 전에 StartScene.unity를 커밋하거나 백업해 두세요.\n\n" +
                "계속할까요?",
                "실행", "취소");

            if (!confirmed)
            {
                Debug.Log($"{Tag} 사용자가 취소했습니다. 아무것도 바꾸지 않았습니다.");
                return;
            }

            RunApply(scene, single, multi);
        }

        /// <summary>
        /// 버튼 하나를 이름으로 찾고, Button / PersistentCall / target / method 를 모두 확인한다.
        /// 확인 결과는 한 줄에 하나씩 따로 찍어 Console 목록에서 접히지 않게 한다.
        /// </summary>
        private static ButtonCheck CheckButton(Scene scene, string name, string expectedMethod)
        {
            var result = new ButtonCheck { Name = name, ExpectedMethod = expectedMethod, Passed = false };

            GameObject go = ResolveUnique(scene, name);

            if (go == null)
                return result;

            result.Target = go;

            LabelTarget label = FindLabel(go);
            Debug.Log($"{Tag} {name}: 텍스트 컴포넌트 = {label.Kind}");

            Button button = go.GetComponent<Button>();

            if (button == null)
            {
                Debug.LogError($"{Tag} {name}: Button 컴포넌트가 없습니다.");
                return result;
            }

            result.Button = button;

            int calls = button.onClick.GetPersistentEventCount();

            if (calls != 1)
            {
                Debug.LogError($"{Tag} {name}: Persistent Call이 {calls}개입니다. 1개여야 합니다.");

                for (int i = 0; i < calls; i++)
                {
                    Object t = button.onClick.GetPersistentTarget(i);
                    Debug.LogError(
                        $"{Tag} {name}: call[{i}] target={(t != null ? t.name : "null")} " +
                        $"method={button.onClick.GetPersistentMethodName(i)}");
                }

                return result;
            }

            Object target = button.onClick.GetPersistentTarget(0);
            string method = button.onClick.GetPersistentMethodName(0);

            if (target == null)
            {
                Debug.LogError($"{Tag} {name}: onClick target이 비어 있습니다(Missing).");
                return result;
            }

            bool targetIsConnectionManager =
                target is ConnectionManager ||
                (target is GameObject targetGo && targetGo.GetComponent<ConnectionManager>() != null);

            if (!targetIsConnectionManager)
            {
                Debug.LogError(
                    $"{Tag} {name}: onClick target이 ConnectionManager가 아닙니다. " +
                    $"실제 = {target.name} ({target.GetType().Name})");
                return result;
            }

            if (method != expectedMethod)
            {
                Debug.LogError($"{Tag} {name}: method가 '{method}' 입니다. '{expectedMethod}' 여야 합니다.");
                return result;
            }

            Debug.Log(
                $"{Tag} {name}: PASS — calls=1, target={target.name}({target.GetType().Name}), method={method}");

            result.Passed = true;
            return result;
        }

        /// <summary>UI Sample / MenuCanvas / 데모 오브젝트의 현재 상태를 한 줄씩 보고한다.</summary>
        private static void ReportContext(Scene scene)
        {
            List<GameObject> uiSample = FindAllInScene(scene, UiSampleName);
            Debug.Log($"{Tag} {UiSampleName}: {(uiSample.Count > 0 ? Path(uiSample[0]) : "없음")}");

            List<GameObject> menuCanvas = FindAllInScene(scene, MenuCanvasName);
            Debug.Log($"{Tag} {MenuCanvasName}: {(menuCanvas.Count > 0 ? Path(menuCanvas[0]) : "없음 (Apply 시 생성)")}");

            foreach (string name in DemoObjectNames)
            {
                List<GameObject> targets = FindAllInScene(scene, name);

                Debug.Log(targets.Count > 0
                    ? $"{Tag} 끌 대상 '{name}': {Path(targets[0])} (현재 active={targets[0].activeSelf})"
                    : $"{Tag} 끌 대상 '{name}': 씬에 없음 — 건너뜁니다.");
            }
        }

        private static void RunApply(Scene scene, ButtonCheck single, ButtonCheck multi)
        {
            Undo.SetCurrentGroupName("StartScene 메뉴 UI 정리");
            int undoGroup = Undo.GetCurrentGroup();

            string singleBefore = DescribeOnClick(single.Target);
            string multiBefore = DescribeOnClick(multi.Target);

            // ── 1. Unpack ────────────────────────────────────────────
            // 프리팹 "에셋 파일"은 바뀌지 않는다. 씬 인스턴스의 연결만 끊는다.
            // UserAction으로 호출해야 Undo에 기록된다.
            int unpacked = UnpackUntilRoot(multi.Target) + UnpackUntilRoot(single.Target);
            Debug.Log($"{Tag} Unpack 수행: {unpacked}회");

            // ── 2. 데모 UI 비활성화 ──────────────────────────────────
            foreach (string name in DemoObjectNames)
            {
                List<GameObject> targets = FindAllInScene(scene, name);

                if (targets.Count == 0)
                {
                    Debug.Log($"{Tag} [건너뜀] '{name}' 을 찾지 못했습니다.");
                    continue;
                }

                GameObject target = targets[0];

                // 버튼을 품고 있는 계층은 절대 끄지 않는다.
                if (Contains(target.transform, single.Target.transform) ||
                    Contains(target.transform, multi.Target.transform))
                {
                    Debug.Log($"{Tag} [보호] '{name}' 아래에 버튼이 있어 끄지 않았습니다.");
                    continue;
                }

                Undo.RecordObject(target, "Disable demo UI");
                target.SetActive(false);
                EditorUtility.SetDirty(target);

                Debug.Log($"{Tag} [끔] {Path(target)}");
            }

            // ── 3. MenuCanvas ────────────────────────────────────────
            List<GameObject> existing = FindAllInScene(scene, MenuCanvasName);
            GameObject menuCanvas = existing.Count > 0 ? existing[0] : CreateMenuCanvas();

            var menuRect = menuCanvas.transform as RectTransform;

            if (menuRect == null)
            {
                Debug.LogError($"{Tag} MenuCanvas에 RectTransform이 없습니다. 중단합니다.");
                return;
            }

            // ── 4~5. 버튼 이동 + 정리 ────────────────────────────────
            MoveButton(single.Target, menuRect, SinglePosition, SingleLabel);
            MoveButton(multi.Target, menuRect, MultiPosition, MultiLabel);

            // ── onClick 보존 확인 ────────────────────────────────────
            string singleAfter = DescribeOnClick(single.Target);
            string multiAfter = DescribeOnClick(multi.Target);

            Debug.Log($"{Tag} {SingleBtnName} onClick before = {singleBefore}");
            Debug.Log($"{Tag} {SingleBtnName} onClick after  = {singleAfter}");
            Debug.Log($"{Tag} {MultiBtnName} onClick before = {multiBefore}");
            Debug.Log($"{Tag} {MultiBtnName} onClick after  = {multiAfter}");

            bool intact = singleBefore == singleAfter && multiBefore == multiAfter;

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);

            if (intact)
            {
                Debug.Log($"{Tag} ===== 완료 — onClick 2개 모두 실행 전과 동일합니다. 확인 후 Ctrl+S =====");
            }
            else
            {
                Debug.LogError(
                    $"{Tag} ===== onClick이 달라졌습니다. 저장하지 말고 씬을 다시 여세요(File > Reopen Scene). =====");
            }
        }

        /// <summary>Lobby의 CharacterCanvas와 같은 구성으로 World Space Canvas를 만든다.</summary>
        private static GameObject CreateMenuCanvas()
        {
            var go = new GameObject(MenuCanvasName, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Create MenuCanvas");

            var rect = (RectTransform)go.transform;
            rect.SetParent(null, false);
            rect.localPosition = MenuCanvasPosition;
            rect.localRotation = Quaternion.identity;
            rect.localScale = MenuCanvasScale;
            rect.sizeDelta = MenuCanvasSize;

            Canvas canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();

            // VR 레이로 누르려면 이것이 반드시 있어야 한다. Lobby의 캔버스들과 동일 구성.
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            Debug.Log(
                $"{Tag} [생성] {MenuCanvasName} - WorldSpace, pos {MenuCanvasPosition}, " +
                $"scale {MenuCanvasScale.x}, size {MenuCanvasSize.x}x{MenuCanvasSize.y}, " +
                "GraphicRaycaster + TrackedDeviceGraphicRaycaster");

            return go;
        }

        private static void MoveButton(GameObject button, RectTransform parent, Vector2 anchoredPosition, string label)
        {
            Undo.SetTransformParent(button.transform, parent, "Move button to MenuCanvas");

            var rect = button.transform as RectTransform;

            if (rect == null)
            {
                Debug.LogError($"{Tag} {button.name} 에 RectTransform이 없습니다.");
                return;
            }

            Undo.RecordObject(rect, "Place button");

            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;      // MenuCanvas 기준 1
            rect.sizeDelta = ButtonSize;
            rect.anchoredPosition = anchoredPosition;
            rect.localPosition = new Vector3(anchoredPosition.x, anchoredPosition.y, 0f);

            EditorUtility.SetDirty(rect);

            Debug.Log(
                $"{Tag} [이동] {button.name} -> {MenuCanvasName}, " +
                $"anchoredPosition {anchoredPosition}, sizeDelta {ButtonSize}, scale 1");

            LabelTarget target = FindLabel(button);

            if (!target.Found)
            {
                Debug.LogError($"{Tag} {button.name} 안에서 텍스트 컴포넌트를 찾지 못해 라벨을 넣지 못했습니다.");
                return;
            }

            SetLabel(target, label);
        }

        /// <summary>
        /// 버튼 안의 텍스트 컴포넌트를 찾는다.
        ///
        /// XRI Starter Assets의 TextButton.prefab은 TMP가 아니라 **레거시
        /// UnityEngine.UI.Text**를 쓴다 (Text.cs guid 5f7201a1…, TMP는 f4688fdb…).
        /// 그래서 TMP만 찾으면 영원히 못 찾는다. TMP를 먼저 보고, 없으면 레거시 Text로 내려간다.
        /// </summary>
        private static LabelTarget FindLabel(GameObject button)
        {
            var target = new LabelTarget { Button = button };

            target.Tmp = button.GetComponentInChildren<TMP_Text>(true);

            if (target.Tmp == null)
                target.Legacy = button.GetComponentInChildren<Text>(true);

            return target;
        }

        /// <summary>
        /// 찾아 둔 텍스트 컴포넌트에 라벨만 넣는다.
        ///
        /// 폰트는 건드리지 않는다. 레거시 Text에는 TMP Font Asset을 넣을 수 없고,
        /// 지금 쓰는 LegacyRuntime 폰트로 한글이 이미 정상 표시되고 있다.
        /// 폰트 교체는 나중 UI 디자인 단계에서 따로 한다.
        ///
        /// Font Size도 그대로 둔다. 지금 값이 실기기에서 검증된 크기다.
        /// </summary>
        private static void SetLabel(LabelTarget target, string label)
        {
            if (target.Tmp != null)
            {
                Undo.RecordObject(target.Tmp, "Set button label");

                target.Tmp.text = label;
                target.Tmp.alignment = TextAlignmentOptions.Center;   // 가로 Center + 세로 Middle

                EditorUtility.SetDirty(target.Tmp);

                Debug.Log(
                    $"{Tag} [라벨] {target.Button.name} -> \"{label}\" " +
                    $"(TMP, font {(target.Tmp.font != null ? target.Tmp.font.name : "없음")} 유지, " +
                    $"size {target.Tmp.fontSize} 유지)");

                return;
            }

            Undo.RecordObject(target.Legacy, "Set button label");

            target.Legacy.text = label;
            target.Legacy.alignment = TextAnchor.MiddleCenter;

            EditorUtility.SetDirty(target.Legacy);

            Debug.Log(
                $"{Tag} [라벨] {target.Button.name} -> \"{label}\" " +
                $"(UnityEngine.UI.Text, font {(target.Legacy.font != null ? target.Legacy.font.name : "없음")} 유지, " +
                $"size {target.Legacy.fontSize} 유지)");
        }

        /// <summary>
        /// 이 오브젝트가 스스로 프리팹 인스턴스 루트가 될 때까지 바깥 인스턴스를 하나씩 Unpack한다.
        /// Unpack은 씬 인스턴스의 프리팹 연결만 끊으며 프리팹 에셋 파일은 바뀌지 않는다.
        ///
        /// InteractionMode.UserAction을 쓰는 이유:
        /// AutomatedAction은 Undo에 기록되지 않아, Ctrl+Z를 누르면 Unpack만 남고
        /// 그 뒤 작업이 되돌아가 씬이 어중간하게 망가진다.
        /// </summary>
        private static int UnpackUntilRoot(GameObject target)
        {
            int count = 0;

            while (count <= 8)
            {
                GameObject root = PrefabUtility.GetOutermostPrefabInstanceRoot(target);

                if (root == null || root == target)
                    break;

                Debug.Log($"{Tag} [Unpack] {root.name} ({target.name} 때문에)");

                PrefabUtility.UnpackPrefabInstance(
                    root, PrefabUnpackMode.OutermostRoot, InteractionMode.UserAction);

                count++;
            }

            return count;
        }

        private static string DescribeOnClick(GameObject button)
        {
            Button b = button.GetComponent<Button>();

            if (b == null)
                return "(Button 컴포넌트 없음)";

            int n = b.onClick.GetPersistentEventCount();
            var sb = new StringBuilder($"calls={n}");

            for (int i = 0; i < n; i++)
            {
                Object target = b.onClick.GetPersistentTarget(i);

                sb.Append($" [{i}] target={(target != null ? target.name : "null")}");
                sb.Append($"({(target != null ? target.GetType().Name : "-")})");
                sb.Append($" method={b.onClick.GetPersistentMethodName(i)}");
            }

            return sb.ToString();
        }

        private static bool Contains(Transform ancestor, Transform candidate)
        {
            for (Transform t = candidate; t != null; t = t.parent)
            {
                if (t == ancestor)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 이름이 정확히 일치하는 오브젝트가 딱 하나일 때만 돌려준다.
        /// 0개이거나 2개 이상이면 Error를 찍고 null을 돌려줘 호출한 쪽이 중단하게 한다.
        /// </summary>
        private static GameObject ResolveUnique(Scene scene, string name)
        {
            List<GameObject> found = FindAllInScene(scene, name);

            if (found.Count == 0)
            {
                Debug.LogError($"{Tag} {name}: 씬에서 찾지 못했습니다. (이름이 정확히 '{name}' 인지 확인하세요)");
                return null;
            }

            if (found.Count > 1)
            {
                Debug.LogError($"{Tag} {name}: 이름이 같은 오브젝트가 {found.Count}개입니다. 어느 것인지 알 수 없어 중단합니다.");

                foreach (GameObject g in found)
                    Debug.LogError($"{Tag} {name}: 후보 - {Path(g)}");

                return null;
            }

            Debug.Log($"{Tag} {name}: 발견 - {Path(found[0])}");

            return found[0];
        }

        /// <summary>
        /// 이름이 정확히 일치하는 오브젝트를 모두 모은다.
        /// 하나만 찾고 끝내지 않는 이유는 동명 오브젝트가 있으면 조용히 엉뚱한 것을 집기 때문이다.
        /// </summary>
        private static List<GameObject> FindAllInScene(Scene scene, string name)
        {
            var hits = new List<GameObject>();

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == name)
                        hits.Add(t.gameObject);
                }
            }

            return hits;
        }

        private static string Path(GameObject go)
        {
            var parts = new List<string>();

            for (Transform t = go.transform; t != null; t = t.parent)
                parts.Insert(0, t.name);

            return string.Join("/", parts);
        }
    }
}
#endif
