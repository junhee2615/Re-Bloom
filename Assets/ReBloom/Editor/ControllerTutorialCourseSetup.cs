using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Unity.XR.CoreUtils;

/// <summary>
/// 현재 열린 씬에 컨트롤러 튜토리얼 코스를 만든다.
/// XR Origin이 서 있는 위치를 기준으로, 그 정면(+Z) 방향으로 6단계 스테이션을 배치한다.
/// 다시 실행하면 기존 코스를 지우고 새로 만든다.
/// </summary>
public static class ControllerTutorialCourseSetup
{
    private const string RootName = "ControllerTutorialCourse";
    // 안내판 전체 배율. 레이아웃(px)은 그대로 두고 크기만 키운다.
    private const float BoardScale = 1.5f;
    private const string MaterialFolder = "Assets/ReBloom/Materials/Tutorial";
    private const string FontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";
    private const string CratePath = "Assets/ReBloom/AssetStore/StylizedWater2/_Demo/DemoAssets/Prefabs/SWS_WoodenCrate.prefab";
    private const string SeedPath = "Assets/ReBloom/Environment_Art/Props/Stage3/Seed_low.fbx";
    // Stage1 옥상 소품(Rusty_Props)의 AirConditioner (2)와 같은 메시/머티리얼을 쓴다.
    private const string RustyPropsPath = "Assets/ReBloom/Prefabs/Environment/Stage1/Rusty_Props.prefab";
    private const string AirConditionerName = "AirConditioner (2)";
    // 원본 높이 0.38m는 CharacterController stepOffset(0.5m)보다 낮아 걸어서 올라가지므로 2배로 키워 점프가 필요하게 한다.
    private const float AirConditionerScale = 1.265f;
    private const string StepClipPath = "Assets/ReBloom/Sounds/Stage3/correct.mp3";
    private const string FinishClipPath = "Assets/ReBloom/Sounds/Basic/Clear Sound.mp3";

    private static TMP_FontAsset font;

    private struct Board
    {
        public GameObject root;
        public TMP_Text progress, title, body, status;
        public RectTransform buttonRow;
        public Image[] images;
    }

    [MenuItem("ReBloom/Tutorial/Build Controller Tutorial Course")]
    public static void Build()
    {
        var scene = EditorSceneManager.GetActiveScene();
        XROrigin origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        if (origin == null)
        {
            EditorUtility.DisplayDialog("Controller Tutorial", "씬에 XR Origin이 없습니다.", "확인");
            return;
        }

        // 이미 코스가 있으면 그 위치를 기준점으로 재사용한다.
        // (XR Origin은 빌드 때 시작 지점으로 옮겨지므로, 리그 위치를 다시 기준으로 쓰면 코스가 매번 밀려난다.)
        bool hasExisting = false;
        var keptSprites = new Sprite[6][];
        Vector3 existingPos = Vector3.zero;
        foreach (GameObject r in scene.GetRootGameObjects())
        {
            if (r.name != RootName) continue;
            hasExisting = true;
            existingPos = r.transform.position;

            // 보드에 넣어 둔 이미지는 다시 만들어도 유지한다.
            var oldMgr = r.GetComponent<ControllerTutorialManager>();
            if (oldMgr != null)
            {
                var oldSo = new SerializedObject(oldMgr);
                var oldStations = oldSo.FindProperty("stations");
                for (int i = 0; i < Mathf.Min(6, oldStations.arraySize); i++)
                {
                    var imgs = oldStations.GetArrayElementAtIndex(i).FindPropertyRelative("images");
                    if (imgs == null) continue;
                    keptSprites[i] = new Sprite[imgs.arraySize];
                    for (int k = 0; k < imgs.arraySize; k++)
                        keptSprites[i][k] = imgs.GetArrayElementAtIndex(k).objectReferenceValue as Sprite;
                }
            }
            Undo.DestroyObjectImmediate(r);
        }

        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        Material glowMat = GetOrCreateGlowMaterial();

        // 코스 기준점: XR Origin 발밑 바닥
        Vector3 basePos = hasExisting ? existingPos : origin.transform.position;
        if (!hasExisting)
            basePos.y = GroundY(basePos, origin.transform);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Controller Tutorial Course");
        root.transform.position = basePos;
        Transform R = root.transform;

        var manager = root.AddComponent<ControllerTutorialManager>();
        var sfx = root.AddComponent<AudioSource>();
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;

        // ---------------- 시작 지점 + 리그 이동
        Transform start = Empty(R, "StartPoint", new Vector3(0f, 0f, -9f));
        Undo.RecordObject(origin.transform, "Move XR Origin to tutorial start");
        origin.transform.SetPositionAndRotation(start.position, Quaternion.identity);
        PrefabUtility.RecordPrefabInstancePropertyModifications(origin.transform);

        var stations = new GameObject[6];
        var boards = new Board[6];

        // ================= 1. Grab
        Transform props = Empty(R, "Props", Vector3.zero);
        GameObject pedestal = Prefab(CratePath, props, "Pedestal_Crate", new Vector3(0f, 0.25f, -8.2f), 0.5f);
        GameObject planter = Prefab(CratePath, props, "Planter_Crate", new Vector3(0.95f, 0.25f, -8.2f), 0.5f);

        GameObject seed = Prefab(SeedPath, props, "TutorialSeed", new Vector3(0f, 0.515f, -8.2f), 1.2f);
        seed.layer = LayerMask.NameToLayer("Interactable");
        var seedCol = seed.AddComponent<BoxCollider>();
        seedCol.size = new Vector3(seedCol.size.x, Mathf.Max(seedCol.size.y, 0.03f), seedCol.size.z);
        var rb = seed.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        var grab = seed.AddComponent<XRGrabInteractable>();
        grab.interactionLayers = InteractionLayerMask.GetMask("Interactable");
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.useDynamicAttach = true;

        Transform zoneT = Empty(props, "GrabTargetZone", new Vector3(0.95f, 0.65f, -8.2f));
        var zone = zoneT.gameObject.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(0.55f, 0.4f, 0.55f);

        stations[0] = Empty(R, "Station1_Grab", Vector3.zero).gameObject;
        Ring(stations[0].transform, "TargetGlow", new Vector3(0.95f, 0.502f, -8.2f), 0.45f, glowMat);
        boards[0] = MakeBoard(stations[0].transform, "Board", new Vector3(-0.95f, 1.55f, -7.5f), new Vector3(0f, 1.6f, -9f), 1.0f, 0.62f, false);

        // ================= 2. Walk
        stations[1] = Empty(R, "Station2_Walk", Vector3.zero).gameObject;
        Transform walkTarget = Ring(stations[1].transform, "WalkTarget", new Vector3(-2f, 0.01f, -4.5f), 1.4f, glowMat);
        boards[1] = MakeBoard(stations[1].transform, "Board", new Vector3(-2f, 1.6f, -3.0f), new Vector3(-0.5f, 1.6f, -8.5f), 1.0f, 0.62f, false);

        // ================= 3. Teleport
        stations[2] = Empty(R, "Station3_Teleport", Vector3.zero).gameObject;
        Transform tpTarget = Ring(stations[2].transform, "TeleportTarget", new Vector3(-2f, 0.01f, 4.5f), 2.0f, glowMat);
        Beacon(stations[2].transform, new Vector3(-2f, 0f, 4.5f), glowMat);
        boards[2] = MakeBoard(stations[2].transform, "Board", new Vector3(-0.7f, 1.6f, -3.2f), new Vector3(-2f, 1.6f, -4.5f), 1.0f, 0.68f, false);

        // ================= 4. Jump
        GameObject jumpObj = AirConditioner(props, "Jump_AirConditioner", new Vector3(-2f, 0f, 7.2f), basePos.y);
        Bounds sb = RendererBounds(jumpObj);
        Transform jumpTop = Empty(R, "JumpTargetTop", R.InverseTransformPoint(new Vector3(sb.center.x, sb.max.y, sb.center.z)));
        stations[3] = Empty(R, "Station4_Jump", Vector3.zero).gameObject;
        Ring(stations[3].transform, "JumpGlow", R.InverseTransformPoint(new Vector3(sb.center.x, basePos.y + 0.01f, sb.center.z)), 2.6f, glowMat);
        boards[3] = MakeBoard(stations[3].transform, "Board", new Vector3(-2f, 2.1f, 9.0f), new Vector3(-2f, 1.6f, 4.5f), 1.0f, 0.62f, false);

        // ================= 5. Ray UI
        stations[4] = Empty(R, "Station5_RayUI", Vector3.zero).gameObject;
        boards[4] = MakeBoard(stations[4].transform, "Board", new Vector3(-2f, 1.75f, 9.6f), new Vector3(-2f, 1.6f, 7.2f), 1.1f, 0.78f, true, 2);
        Button nextBtn = MakeButton(boards[4].buttonRow, "NextButton", "다음", 260f);

        // ================= 6. X Button
        stations[5] = Empty(R, "Station6_XButton", Vector3.zero).gameObject;
        boards[5] = MakeBoard(stations[5].transform, "Board", new Vector3(-2f, 1.75f, 9.6f), new Vector3(-2f, 1.6f, 7.2f), 1.1f, 0.78f, true);
        Button restartBtn = MakeButton(boards[5].buttonRow, "RestartButton", "처음부터 다시", 330f);
        Button finishBtn = MakeButton(boards[5].buttonRow, "FinishButton", "완료", 260f);

        // ================= 완료
        GameObject finished = Empty(R, "Finished", Vector3.zero).gameObject;
        Board fb = MakeBoard(finished.transform, "Board", new Vector3(-2f, 1.75f, 9.6f), new Vector3(-2f, 1.6f, 7.2f), 1.1f, 0.7f, true, 0);
        fb.progress.text = "";
        fb.title.text = "튜토리얼 완료!";
        fb.body.text = "컨트롤러 사용법을 모두 익혔어요.\n다시 연습하고 싶으면 아래 버튼을 눌러주세요.";
        fb.status.text = "";
        Button restartBtn2 = MakeButton(fb.buttonRow, "RestartButton", "처음부터 다시", 330f);
        finished.SetActive(false);

        // ---------------- 매니저 연결
        string[] titles =
        {
            "물건 집기",
            "걸어서 이동하기",
            "텔레포트",
            "점프",
            "Ray로 UI 선택하기",
            "X 버튼"
        };
        string[] bodies =
        {
            "손을 씨앗 가까이 가져간 뒤, 컨트롤러 손잡이의 <b>그립 버튼</b>을 꾹 쥐어보세요.\n쥔 채로 옮겨서 옆 상자의 빛나는 곳에 내려놓으면 완료!",
            "<b>왼쪽 조이스틱</b>을 밀면 그 방향으로 천천히 걸어요.\n왼쪽 앞의 빛나는 원 안까지 걸어가 보세요.",
            "<b>오른쪽 조이스틱</b>을 앞으로 밀면 도착할 곳이 표시돼요.\n멀리 빛나는 원을 겨냥하고 조이스틱을 놓으면 순간이동!\n가까운 곳은 걷기, 먼 곳은 텔레포트가 편해요.",
            "<b>오른쪽 A 버튼</b>을 누르면 점프해요. 길게 누를수록 더 높이 뛰어요.\n앞에 있는 실외기 위로 뛰어올라 보세요.",
            "<b>오른쪽 B 버튼</b>을 누르면 손에서 광선(Ray)이 나와요.\nRay로 버튼을 가리키고 <b>검지 트리거</b>를 당기면 선택돼요.\n다 쓰면 B 버튼을 다시 눌러 꺼주세요.",
            "<b>왼쪽 X 버튼</b>은 본게임에서 미션 안내 패널을 열고 닫을 때 써요.\n지금은 위치만 기억해 두세요! (눌러봐도 괜찮아요)"
        };

        var so = new SerializedObject(manager);
        so.FindProperty("xrOrigin").objectReferenceValue = origin;
        so.FindProperty("startPoint").objectReferenceValue = start;
        var arr = so.FindProperty("stations");
        arr.arraySize = 6;
        for (int i = 0; i < 6; i++)
        {
            var e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("title").stringValue = titles[i];
            e.FindPropertyRelative("body").stringValue = bodies[i];
            e.FindPropertyRelative("root").objectReferenceValue = stations[i];
            e.FindPropertyRelative("progressText").objectReferenceValue = boards[i].progress;
            e.FindPropertyRelative("titleText").objectReferenceValue = boards[i].title;
            e.FindPropertyRelative("bodyText").objectReferenceValue = boards[i].body;
            e.FindPropertyRelative("statusText").objectReferenceValue = boards[i].status;

            var slots = e.FindPropertyRelative("imageSlots");
            var sprites = e.FindPropertyRelative("images");
            int count = boards[i].images.Length;
            slots.arraySize = count;
            sprites.arraySize = count;
            for (int k = 0; k < count; k++)
            {
                slots.GetArrayElementAtIndex(k).objectReferenceValue = boards[i].images[k];
                Sprite kept = keptSprites[i] != null && k < keptSprites[i].Length ? keptSprites[i][k] : null;
                sprites.GetArrayElementAtIndex(k).objectReferenceValue = kept;
            }

            // 에디터에서도 내용이 보이게 미리 채워둔다.
            boards[i].progress.text = $"{i + 1} / 6";
            boards[i].title.text = titles[i];
            boards[i].body.text = bodies[i];
            boards[i].status.text = "";
        }
        so.FindProperty("finishedRoot").objectReferenceValue = finished;
        so.FindProperty("grabObject").objectReferenceValue = grab;
        so.FindProperty("grabTargetZone").objectReferenceValue = zone;
        so.FindProperty("walkTarget").objectReferenceValue = walkTarget;
        so.FindProperty("teleportTarget").objectReferenceValue = tpTarget;
        so.FindProperty("jumpTargetTop").objectReferenceValue = jumpTop;
        so.FindProperty("rayNextButton").objectReferenceValue = nextBtn;
        so.FindProperty("finishButton").objectReferenceValue = finishBtn;
        var rs = so.FindProperty("restartButtons");
        rs.arraySize = 2;
        rs.GetArrayElementAtIndex(0).objectReferenceValue = restartBtn;
        rs.GetArrayElementAtIndex(1).objectReferenceValue = restartBtn2;
        so.FindProperty("sfxSource").objectReferenceValue = sfx;
        so.FindProperty("stepClearClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(StepClipPath);
        so.FindProperty("finishClip").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AudioClip>(FinishClipPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        // 첫 단계만 켜 둔다(에디터 미리보기용).
        for (int i = 1; i < 6; i++) stations[i].SetActive(false);

        EnsureXREventSystem();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log("[ControllerTutorialCourseSetup] 컨트롤러 튜토리얼 코스를 만들었습니다.", root);
    }

    // ------------------------------------------------------------------ helpers

    private static float GroundY(Vector3 pos, Transform ignore)
    {
        var hits = Physics.RaycastAll(pos + Vector3.up * 5f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = pos.y;
        float bestDist = float.MaxValue;
        foreach (var h in hits)
        {
            if (ignore != null && h.collider.transform.IsChildOf(ignore)) continue;
            if (h.distance < bestDist) { bestDist = h.distance; best = h.point.y; }
        }
        return best;
    }

    private static Transform Empty(Transform parent, string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        return go.transform;
    }

    private static GameObject Prefab(string path, Transform parent, string name, Vector3 localPos, float scale)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        go.name = name;
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one * scale;
        return go;
    }

    private static GameObject AirConditioner(Transform parent, string name, Vector3 localFloorPos, float floorY)
    {
        var props = AssetDatabase.LoadAssetAtPath<GameObject>(RustyPropsPath);
        Transform src = null;
        foreach (Transform t in props.GetComponentsInChildren<Transform>(true))
            if (t.name == AirConditionerName) { src = t; break; }
        if (src == null)
            throw new System.Exception($"{RustyPropsPath}에서 {AirConditionerName}을 찾지 못했습니다.");

        // 루트는 회전/스케일 없이 두고, 원본 회전(Z 180)·스케일은 Model 자식에만 준다.
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localFloorPos;

        var model = new GameObject("Model");
        model.transform.SetParent(go.transform, false);
        model.transform.localRotation = src.localRotation;
        model.transform.localScale = src.localScale * AirConditionerScale;
        model.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
        model.AddComponent<MeshRenderer>().sharedMaterials = src.GetComponent<MeshRenderer>().sharedMaterials;

        // 피벗이 바닥이 아니므로 보이는 모양의 바닥을 지면에 맞춘다.
        Bounds b = RendererBounds(go);
        model.transform.position += Vector3.up * (floorY - b.min.y);
        b = RendererBounds(go);

        // 원본 MeshCollider는 보이는 메시와 높이가 어긋나 있어서, 보이는 크기에 맞춘 박스 충돌체를 쓴다.
        var col = go.AddComponent<BoxCollider>();
        col.center = go.transform.InverseTransformPoint(b.center);
        col.size = b.size;
        return go;
    }

    private static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        Bounds b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    private static Transform Ring(Transform parent, string name, Vector3 localPos, float diameter, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(diameter, 0.004f, diameter);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        go.AddComponent<TutorialGlowPulse>();
        return go.transform;
    }

    private static void Beacon(Transform parent, Vector3 localFloorPos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Beacon";
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localFloorPos + Vector3.up * 1.5f;
        go.transform.localScale = new Vector3(0.08f, 1.5f, 0.08f);
        var r = go.GetComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
    }

    private static Board MakeBoard(Transform parent, string name, Vector3 localPos, Vector3 viewerLocal, float widthM, float heightM, bool withButtons, int imageCount = 1)
    {
        const float ppm = 1000f; // 1m = 1000 UI 단위
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        Vector3 dir = localPos - viewerLocal; dir.y = 0f;
        go.transform.localRotation = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.forward, Vector3.up);
        go.transform.localScale = Vector3.one * (BoardScale / ppm);

        var rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(widthM * ppm, heightM * ppm);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;
        go.AddComponent<TrackedDeviceGraphicRaycaster>();

        var bg = go.AddComponent<Image>();
        bg.color = new Color(0.06f, 0.11f, 0.09f, 0.88f);

        var accent = UIChild(rt, "Accent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -12f), new Vector2(0f, 0f));
        accent.gameObject.AddComponent<Image>().color = new Color(0.55f, 1f, 0.6f, 0.9f);

        float pad = 40f;
        Board b = new Board { root = go };
        b.progress = Text(rt, "Progress", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(pad, -80f), new Vector2(-pad, -24f), 30, TextAlignmentOptions.TopLeft, new Color(0.7f, 0.85f, 0.75f));
        b.title = Text(rt, "Title", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(pad, -160f), new Vector2(-pad, -70f), 60, TextAlignmentOptions.TopLeft, Color.white);
        b.title.fontStyle = FontStyles.Bold;

        float bottom = withButtons ? 215f : 105f;

        // 오른쪽 하단 이미지 칸. 본문/상태 문구는 이 칸만큼 오른쪽을 비운다.
        float slot = withButtons ? 200f : 230f;
        float slotGap = 20f;
        float slotBottom = withButtons ? 140f : 30f;
        float column = imageCount > 0 ? imageCount * slot + (imageCount - 1) * slotGap + 30f : 0f;
        b.images = new Image[imageCount];
        for (int k = 0; k < imageCount; k++)
        {
            float right = pad + (imageCount - 1 - k) * (slot + slotGap);
            var slotRt = UIChild(rt, $"ImageSlot{k + 1}", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-right - slot, slotBottom), new Vector2(-right, slotBottom + slot));
            var img = slotRt.gameObject.AddComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0.12f);
            img.preserveAspect = true;
            img.raycastTarget = false;
            var label = Text(slotRt, "PlaceholderLabel", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 28, TextAlignmentOptions.Center, new Color(1f, 1f, 1f, 0.45f));
            label.text = imageCount > 1 ? $"이미지 {k + 1}" : "이미지";
            b.images[k] = img;
        }

        b.body = Text(rt, "Body", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(pad, bottom), new Vector2(-pad - column, -175f), 36, TextAlignmentOptions.TopLeft, new Color(0.92f, 0.95f, 0.93f));
        b.body.lineSpacing = 12f;
        b.status = Text(rt, "Status", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(pad, bottom - 95f), new Vector2(-pad - column, bottom - 5f), 34, TextAlignmentOptions.Left, new Color(1f, 0.9f, 0.55f));
        b.status.textWrappingMode = TextWrappingModes.Normal;

        if (withButtons)
        {
            var row = UIChild(rt, "Buttons", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(pad, 24f), new Vector2(-pad, 120f));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 30f;
            h.childAlignment = TextAnchor.MiddleCenter;
            h.childControlWidth = false;
            h.childControlHeight = true;
            h.childForceExpandWidth = false;
            h.childForceExpandHeight = true;
            b.buttonRow = row;
        }
        return b;
    }

    private static RectTransform UIChild(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = offMin; rt.offsetMax = offMax;
        return rt;
    }

    private static TMP_Text Text(RectTransform parent, string name, Vector2 aMin, Vector2 aMax, Vector2 offMin, Vector2 offMax, float size, TextAlignmentOptions align, Color color)
    {
        var rt = UIChild(parent, name, aMin, aMax, offMin, offMax);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        t.richText = true;
        return t;
    }

    private static Button MakeButton(RectTransform row, string name, string label, float width)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(row, false);
        rt.sizeDelta = new Vector2(width, 96f);
        var img = go.AddComponent<Image>();
        img.color = Color.white;
        var btn = go.AddComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = new Color(0.30f, 0.62f, 0.40f, 1f);
        cb.highlightedColor = new Color(0.45f, 0.85f, 0.55f, 1f);
        cb.pressedColor = new Color(0.25f, 0.45f, 0.30f, 1f);
        cb.selectedColor = cb.highlightedColor;
        cb.disabledColor = new Color(0.3f, 0.3f, 0.3f, 0.6f);
        btn.colors = cb;
        var le = go.AddComponent<LayoutElement>();
        le.preferredWidth = width;
        var t = Text(rt, "Label", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 38, TextAlignmentOptions.Center, Color.white);
        t.text = label;
        t.fontStyle = FontStyles.Bold;
        return btn;
    }

    private static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        so.FindProperty(field).objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    private static Material GetOrCreateGlowMaterial()
    {
        string path = MaterialFolder + "/TutorialGlow.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        EnsureFolder(MaterialFolder);
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetColor("_BaseColor", new Color(0.55f, 1f, 0.6f, 0.55f));
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureXREventSystem()
    {
        var es = Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (es == null)
        {
            var go = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            es = go.AddComponent<EventSystem>();
        }

        if (es.GetComponent<XRUIInputModule>() == null)
        {
            foreach (var m in es.GetComponents<BaseInputModule>())
                Undo.DestroyObjectImmediate(m);
            Undo.AddComponent<XRUIInputModule>(es.gameObject);
        }
    }
}
