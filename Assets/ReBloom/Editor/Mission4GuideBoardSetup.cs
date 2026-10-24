using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage2 MISSION 4(뿌리 활성화) 미션 힌트 안내판을 만들고 세 뿌리 옆에 놓는 도구.
///
/// 미션 1~3 안내판과 같은 <see cref="MissionGuideBoard"/> 컴포넌트·같은 디자인을 쓰되,
/// 프리팹은 뿌리마다 따로 둔다. 미션 1~3 프리팹과 씬 배치는 건드리지 않는다.
///
/// <b>왜 뿌리마다 다른 안내판인가 (B안)</b> —
/// 미션 4는 뿌리마다 조작이 <b>완전히 다르다</b>.
///   · 뿌리 1 (AliveStump)  : 소리를 듣고 오른손 Grip 으로 박자 맞히기 (SoundTriggerMission)
///   · 뿌리 2 (AliveStump2) : 떨어지는 잎을 선에 맞춰 터치 (FallingNoteMission)
///   · 뿌리 3 (AliveStump3) : 1부 소리 + 2부 노트, 둘이 역할을 나눠 수행 (CombinedMission)
/// 하나로 합치면 카드가 아홉 칸을 넘어 보드가 3 m 가까이 길어지고, 지금 할 조작을 찾기 어렵다.
/// 또 <c>RootMissionNet.ActiveIndex</c> 가 0 → 1 → 2 로 <b>한 번에 한 뿌리씩</b> 진행시키므로,
/// 해당 뿌리 앞에서 그 뿌리 설명만 읽는 쪽이 진행 순서와 맞다.
/// 미션 3처럼 "정답을 가리는" 문제도 없다 — 미션 4는 탐색이 아니라 순차 수행이다.
///
/// <b>안내판이 서로 겹치지 않게 하는 법</b> —
/// 가장 가까운 두 뿌리(AliveStump ↔ AliveStump2)가 9.3 m 떨어져 있다.
/// <see cref="ShowDistance"/> 4 m / 히스테리시스 0.5 → 꺼지는 거리 4.5 m 라,
/// 중간 지점(4.64 m)에서도 두 안내판이 동시에 뜨지 않는다.
///
/// <b>배치 방향</b> —
/// 각 뿌리에서 연못 쪽(플레이어가 오는 방향)으로 향하되 55° 옆으로 비켜 3 m.
/// 뿌리 바로 앞의 <c>MissionCanvas</c>(시작 버튼·미션 패널)를 가리지 않으면서,
/// 다가오는 동안 눈에 들어오는 자리다.
///
/// 공용 에셋(둥근 패널·발광·체크 스프라이트, 깊이 프라임 머티리얼)은
/// <see cref="MissionGuideBoardSetup.Skin"/> 이 만들어 둔 것을 그대로 쓴다.
///
/// 이미 놓여 있는 안내판은 다시 만들지 않고, 손으로 맞춘 위치·회전·크기도 건드리지 않는다.
///
/// 메뉴는 하나로 끝난다 — Build and Place 한 번이면 프리팹 셋 생성·배치·참조 연결·검증까지
/// 모두 수행한다. 씬 저장만 직접 하면 된다(Ctrl+Z 로 되돌릴 수 있도록 자동 저장하지 않는다).
/// </summary>
public static class Mission4GuideBoardSetup
{
    private const string SceneName = "Stage2";
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string Mission1PrefabPath = PrefabFolder + "/MissionGuideBoard.prefab";
    private const string Mission2PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission2.prefab";
    private const string Mission3PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission3.prefab";
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    private const string LumenPortraitPath = "Assets/ReBloom/UI/ReBloom_Lumen_Portrait.png";
    private const string EchoPortraitPath = "Assets/ReBloom/UI/ReBloom_Echo_Portrait.png";

    /// <summary>플레이어가 그루터기 지대로 들어오는 쪽. 미션 2가 벌어지는 연못이다.</summary>
    private static readonly string[] ApproachFromNames =
    {
        "InteractablePlant1", "InteractablePlant2", "InteractablePlant3",
    };

    // ── 레이아웃 (미션 1~3과 동일) ────────────────────────────────────────
    private const float BoardWidth = 1000f;
    private const float BoardScale = 0.001f;

    private const float Pad = 36f;
    private const float PanelGlowWidth = 26f;

    private const float KickerTop = 36f;
    private const float KickerHeight = 32f;
    private const float KickerFontSize = 24f;
    private const float KickerSpacing = 10f;

    private const float TitleTop = 74f;
    private const float TitleHeight = 70f;
    private const float TitleFontSize = 54f;

    private const float CharactersTop = 160f;
    private const float CharactersHeight = 220f;
    private const float CharacterCardGap = 18f;
    private const float PortraitTop = 16f;
    private const float PortraitSize = 140f;
    private const float CharacterNameTop = 162f;
    private const float CharacterNameHeight = 36f;
    private const float CharacterNameFontSize = 30f;
    private const float RoleBarTop = 206f;
    private const float RoleBarHeight = 4f;
    private const float RoleBarWidth = 120f;

    private const float ParticipationTop = 392f;
    private const float ParticipationHeight = 34f;
    private const float ParticipationFontSize = 25f;

    private const float StepsTop = 442f;
    private const float StepCardHeight = 224f;
    private const float StepCardGap = 14f;
    private const float StepInnerPad = 28f;
    private const float StepLabelTop = 22f;
    private const float StepLabelHeight = 46f;
    private const float StepLabelFontSize = 36f;
    private const float StepBodyTop = 84f;
    private const float StepBodyHeight = 124f;         // 27pt 세 줄 (두 줄 문구 + 접힘 여유)
    private const float StepBodyFontSize = 27f;
    private const float StepBodyLineSpacing = 4f;

    private const float FooterGap = 24f;
    private const float FooterHeight = 50f;
    private const float FooterCheckSize = 34f;
    private const float FooterTextLeft = 50f;
    private const float FooterFontSize = 28f;
    private const float BottomPad = 34f;

    // ── 색 (미션 1~3과 동일) ──────────────────────────────────────────────
    private static readonly Color AccentDefault = new Color(0.30980393f, 0.76862746f, 0.84705883f, 1f);
    private static readonly Color PanelColor = new Color(0.02745098f, 0.05490196f, 0.08627451f, 0.96f);
    private static readonly Color CardColor = new Color(0.07450981f, 0.1254902f, 0.17254902f, 0.98f);
    private static readonly Color PortraitBackColor = new Color(0.13725491f, 0.21176471f, 0.27450982f, 1f);
    private static readonly Color TitleColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color BodyColor = new Color(0.9019608f, 0.94509804f, 0.96862745f, 1f);
    private static readonly Color FooterColor = new Color(0.8392157f, 0.8901961f, 0.92156863f, 1f);
    private static readonly Color LumenColor = new Color(0.78431374f, 0.57254905f, 1f, 1f);
    private static readonly Color EchoColor = new Color(0.42352942f, 0.7764706f, 1f, 1f);

    private const float OutlineAlpha = 0.55f;
    private const float PanelGlowAlpha = 0.40f;

    // ── 배치 ──────────────────────────────────────────────────────────────
    private const float PlacementDistance = 3f;
    private const float SideAngleDegrees = 55f;        // 연못 방향에서 옆으로 비킨 각
    private const float BoardCenterHeight = 1.25f;

    private const float MaxGroundAbove = 1f;
    private const float GroundFallbackOffset = -0.4f;
    private const float GroundProbeUp = 8f;
    private const float GroundProbeLength = 24f;

    // ── 표시 조건 ─────────────────────────────────────────────────────────
    //
    // visibleStep 근거 (TutorialMissionManager_2 전환 코드) —
    //   RootMissionManager.RootsFound        → StumpComplete → MISSION 4 뿌리 활성화  ← 여기
    //   RootMissionManager.AllRootsActivated → AllComplete   → 스테이지 종료
    // 미션 4가 끝나면 AllComplete 로 넘어가 visibleStep 조건이 깨지므로 세 안내판이 함께 꺼진다.
    private const TutorialStep_2 VisibleStep = TutorialStep_2.StumpComplete;

    private const MissionGuideBoard.DistanceMode Mode = MissionGuideBoard.DistanceMode.Horizontal;

    // 가장 가까운 두 뿌리가 9.3 m 다. show 4 / hide 4.5 면 중간 지점(4.64 m)에서도
    // 두 안내판이 겹쳐 뜨지 않는다.
    private const float ShowDistance = 4f;
    private const float HideHysteresis = 0.5f;
    private const float FadeDuration = 0.3f;

    // 힌트 중심 개편 —
    // 시작 버튼 누르기(패널이 눈앞에 있다)와 판정 세부(빨간 선·실패 시 반복·전부 맞혀야 함)는
    // 카드에서 빼고, 안내가 없으면 막히는 것만 남겼다:
    //   뿌리 1 — 소리가 에코에게만 간다 + 트리거가 아니라 Grip
    //   뿌리 2 — 손 터치가 아니라 오른손 레이 + 이번엔 루멘 차례
    //   뿌리 3 — 감각과 입력이 서로 엇갈린다(누가 어느 쪽인지는 밝히지 않는다)
    // Kicker 는 뿌리별로 다르게 둔다 — 세 보드가 같은 미션이지만 순서를 구분해 준다.
    private const string KickerRoot1 = "MISSION 04 · 첫 번째 뿌리";
    private const string KickerRoot2 = "MISSION 04 · 두 번째 뿌리";
    private const string KickerRoot3 = "MISSION 04 · 세 번째 뿌리";

    // ───────────────────────────── 뿌리별 내용 ─────────────────────────────

    /// <summary>안내판 한 장 분. 어느 뿌리를 맡고 무엇을 설명하는지.</summary>
    private sealed class RootSpec
    {
        public string AnchorName;
        public string PrefabPath;
        public string InstanceName;
        public MissionGuideBoard.CharacterDisplay Display;
        public string Kicker;
        public string Title;
        public string Participation;
        public string Footer;
        public string[][] Steps;

        // 런타임에 채운다
        public Transform Anchor;
        public MissionGuideBoard Board;
        public bool Created;

        public int StepCount => Steps.Length;

        public float BoardHeight
        {
            get
            {
                float stepsEnd = StepsTop + StepCount * (StepCardHeight + StepCardGap) - StepCardGap;
                return stepsEnd + FooterGap + FooterHeight + BottomPad;
            }
        }

        public Vector2 BoardSize => new Vector2(BoardWidth, BoardHeight);
        public float FooterTop => BoardHeight - BottomPad - FooterHeight;
    }

    // 문구는 전부 실제 코드에서 확인한 동작만 적었다.
    //
    //  뿌리 1 — SoundTriggerMission.RunMission()
    //    기억하기 → 듣기(소리 재생) → 준비 → 플레이 구간에 오른손 Grip 입력.
    //    IsGrabPressed(): XRNode.RightHand + CommonUsages.gripButton (:325-331)
    //    실패하면 (1)번으로 돌아가 전체 반복.
    //
    //  뿌리 2 — FallingNoteMission.RunMission()
    //    OnNoteClicked(): LeafNote(IPointerDownHandler) 터치. ClearRow ± hitTolerance 안이어야 인정.
    //    노트가 존을 지나치면 failed = true → 그 시도는 실패 (:192-197)
    //    touchedCount >= notes.Count 일 때만 성공 (:203)
    //
    //  뿌리 3 — CombinedMission : 1부 소리 + 2부 노트
    //    soundRole = MentalOnly → 소리는 루멘만 들림 (:163)
    //    grabRole  = EarOnly    → Grip 입력은 에코만 카운트 (:212)
    //    noteRole  = MentalOnly → 노트 터치는 루멘만 카운트 (:304)
    //    GatherNotes(): 루멘은 투명 노트(hostNote), 에코는 보이는 노트(clientNote) (:356-358)
    //
    //  시작 버튼 — RootActivation.OnStartButtonPressed(). MissionCanvas 는 World Space 이고
    //    TrackedDeviceGraphicRaycaster 가 있어 XR 손/레이로 누른다.
    private static readonly RootSpec[] Roots =
    {
        new RootSpec
        {
            AnchorName = "AliveStump",
            PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission4_Root1.prefab",
            InstanceName = "MissionGuideBoard_Mission4_Root1",
            Display = MissionGuideBoard.CharacterDisplay.EchoOnly,
            Kicker = KickerRoot1,
            Title = "소리의 리듬",
            Participation = "에코가 소리를 듣고 따라칩니다.",
            Footer = "리듬이 맞으면 다음 뿌리로 넘어갑니다.",
            Steps = new[]
            {
                new[]
                {
                    "01 · 에코의 소리에 집중하기",
                    "소리는 에코에게만 들려요.\n" +
                    "들리는 리듬을 기억해 보세요."
                },
                new[]
                {
                    "02 · 리듬 따라 하기",
                    "플레이 구간에서 에코의 오른손 Grip(쥐기) 버튼으로\n" +
                    "리듬을 따라 해보세요."
                },
            },
        },
        new RootSpec
        {
            AnchorName = "AliveStump2",
            PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission4_Root2.prefab",
            InstanceName = "MissionGuideBoard_Mission4_Root2",
            Display = MissionGuideBoard.CharacterDisplay.LumenOnly,
            Kicker = KickerRoot2,
            Title = "떨어지는 잎",
            Participation = "루멘이 레이로 잎을 선택합니다.",
            Footer = "잎을 모두 맞히면 다음 뿌리로 넘어갑니다.",
            Steps = new[]
            {
                new[]
                {
                    "01 · 루멘의 차례",
                    "이번에는 루멘이 떨어지는 잎의 타이밍을 맞춰야 해요."
                },
                new[]
                {
                    "02 · 손이 아닌 빛으로",
                    "잎을 직접 만지는 대신\n" +
                    "오른손 레이로 겨누고 트리거를 눌러보세요."
                },
            },
        },
        new RootSpec
        {
            AnchorName = "AliveStump3",
            PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission4_Root3.prefab",
            InstanceName = "MissionGuideBoard_Mission4_Root3",
            Display = MissionGuideBoard.CharacterDisplay.Both,
            Kicker = KickerRoot3,
            Title = "서로 엇갈린 감각",
            Participation = "둘이 번갈아 역할을 맡아요.",
            Footer = "두 단계를 모두 통과하면 뿌리 활성화가 끝납니다.",
            Steps = new[]
            {
                new[]
                {
                    "01 · 듣는 사람과 누르는 사람",
                    "소리를 듣는 사람과 버튼을 누를 수 있는 사람이 달라요.\n" +
                    "서로 리듬을 알려주세요."
                },
                new[]
                {
                    "02 · 보는 사람과 선택하는 사람",
                    "떨어지는 잎을 보는 사람과 선택할 수 있는 사람도 달라요.\n" +
                    "서로의 안내에 집중해 보세요."
                },
            },
        },
    };

    // ───────────────────────────── 메뉴 ─────────────────────────────

    [MenuItem("Re_Bloom/Stage2/Mission 4 Guide Board - Dry Run", priority = 390)]
    private static void DryRunMenu() => BuildAndPlace(false);

    [MenuItem("Re_Bloom/Stage2/Mission 4 Guide Board - Build and Place", priority = 391)]
    private static void BuildAndPlaceMenu() => BuildAndPlace(true);

    [MenuItem("Re_Bloom/Stage2/Mission 4 Guide Board - Validate", priority = 392)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 본체 ─────────────────────────────

    private static void BuildAndPlace(bool apply)
    {
        string mode = apply ? "BUILD + PLACE" : "DRY RUN";
        Debug.Log($"===== 미션 4 가이드 보드 [{mode}] =====");

        if (!TryGetScene(out Scene scene))
            return;

        // 매 실행마다 런타임 필드를 비워 둔다(메뉴를 두 번 눌러도 이전 결과가 남지 않게).
        foreach (RootSpec spec in Roots)
        {
            spec.Anchor = null;
            spec.Board = null;
            spec.Created = false;
        }

        foreach (RootSpec spec in Roots)
        {
            if (!TryFind(scene, spec.AnchorName, out Transform anchor))
                return;

            spec.Anchor = anchor;
        }

        if (!TryGetApproachCenter(scene, out Vector3 approachCenter))
            return;

        ReportPlan(approachCenter);

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 아무것도 만들거나 바꾸지 않았습니다.\n" +
                "적용하려면 'Mission 4 Guide Board - Build and Place' 를 실행하세요.");
            return;
        }

        foreach (RootSpec spec in Roots)
        {
            GameObject prefab = RebuildPrefab(spec);

            if (prefab == null)
                return;

            if (!TryResolveInstance(scene, prefab, spec))
                return;

            PlaceAndWire(spec, approachCenter);
        }

        EditorSceneManager.MarkSceneDirty(scene);

        if (Roots[0].Board != null)
        {
            Selection.activeGameObject = Roots[0].Board.gameObject;
            EditorGUIUtility.PingObject(Roots[0].Board.gameObject);
        }

        Debug.Log(
            "완료했습니다. 씬은 자동 저장하지 않았습니다 — 확인 후 Ctrl+S 로 저장하세요.\n" +
            "되돌리려면 Ctrl+Z 입니다.");

        Validate();
    }

    private static bool TryGetApproachCenter(Scene scene, out Vector3 center)
    {
        center = Vector3.zero;

        var found = new List<Transform>();

        foreach (string name in ApproachFromNames)
        {
            if (TryFind(scene, name, out Transform plant, false))
                found.Add(plant);
        }

        if (found.Count == 0)
        {
            Debug.LogError(
                "연못(InteractablePlant1~3)을 하나도 찾지 못했습니다. " +
                "플레이어가 들어오는 방향을 정할 수 없어 멈춥니다.");
            return false;
        }

        Vector3 sum = Vector3.zero;

        foreach (Transform transform in found)
            sum += transform.position;

        center = sum / found.Count;
        return true;
    }

    /// <summary>뿌리에서 연못 쪽으로 향하되 옆으로 비킨 방향. MissionCanvas 를 가리지 않게 한다.</summary>
    private static Vector3 SideDirection(Vector3 anchorPosition, Vector3 approachCenter)
    {
        Vector3 toApproach = approachCenter - anchorPosition;
        toApproach.y = 0f;

        if (toApproach.sqrMagnitude <= 0.000001f)
            return Vector3.forward;

        return Quaternion.AngleAxis(SideAngleDegrees, Vector3.up) * toApproach.normalized;
    }

    // ───────────────────────────── 프리팹 ─────────────────────────────

    private static GameObject RebuildPrefab(RootSpec spec)
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            Debug.LogError($"폴더가 없습니다: {PrefabFolder}");
            return null;
        }

        bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) != null;

        MissionGuideBoardSetup.Skin skin = MissionGuideBoardSetup.Skin.Load(true);

        GameObject root = BuildHierarchy(skin, spec);

        try
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, spec.PrefabPath, out bool success);

            if (!success || saved == null)
            {
                Debug.LogError($"프리팹 저장에 실패했습니다: {spec.PrefabPath}");
                return null;
            }

            AssetDatabase.ImportAsset(spec.PrefabPath, ImportAssetOptions.ForceUpdate);

            Debug.Log(existed
                ? $"  프리팹을 갱신했습니다: {spec.PrefabPath}  " +
                  "(씬 인스턴스의 위치·회전·크기 오버라이드는 유지됩니다)"
                : $"  프리팹을 만들었습니다: {spec.PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);
    }

    private sealed class Refs
    {
        public readonly List<Graphic> Accents = new List<Graphic>();
        public CanvasGroup Group;
        public TextMeshProUGUI Kicker;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Footer;
        public TextMeshProUGUI Participation;
        public Image LumenPortrait;
        public Image EchoPortrait;
        public TextMeshProUGUI LumenName;
        public TextMeshProUGUI EchoName;
        public Renderer DepthPrime;
        public readonly List<TextMeshProUGUI> StepLabels = new List<TextMeshProUGUI>();
        public readonly List<TextMeshProUGUI> StepBodies = new List<TextMeshProUGUI>();
    }

    private static GameObject BuildHierarchy(MissionGuideBoardSetup.Skin skin, RootSpec spec)
    {
        var refs = new Refs();

        TMP_FontAsset font = skin.Font != null
            ? skin.Font
            : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);

        Vector2 size = spec.BoardSize;

        var root = new GameObject(spec.InstanceName, typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = size;
        rootRect.localPosition = Vector3.zero;
        rootRect.localRotation = Quaternion.identity;
        rootRect.localScale = Vector3.one * BoardScale;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1;
        // GraphicRaycaster / CanvasScaler 를 일부러 넣지 않는다.

        refs.Group = root.AddComponent<CanvasGroup>();
        refs.Group.alpha = 0f;
        refs.Group.interactable = false;
        refs.Group.blocksRaycasts = false;

        RectTransform glow = CreateChild("PanelGlow", rootRect);
        Stretch(glow, -PanelGlowWidth, -PanelGlowWidth, -PanelGlowWidth, -PanelGlowWidth);
        refs.Accents.Add(AddSlicedImage(glow, skin.PanelGlow, WithAlpha(AccentDefault, PanelGlowAlpha)));

        RectTransform panel = CreateChild("Panel", rootRect);
        Stretch(panel, 0f, 0f, 0f, 0f);
        AddSlicedImage(panel, skin.PanelFill, PanelColor);

        RectTransform accentBar = CreateChild("AccentBar", rootRect);
        Stretch(accentBar, 0f, 0f, 0f, 0f);

        RectTransform outline = CreateChild("PanelOutline", accentBar);
        Stretch(outline, 0f, 0f, 0f, 0f);
        refs.Accents.Add(AddSlicedImage(outline, skin.PanelOutline, WithAlpha(AccentDefault, OutlineAlpha)));

        // ── Header ──────────────────────────────────────────────
        RectTransform header = CreateChild("Header", rootRect);
        Stretch(header, 0f, 0f, 0f, 0f);

        RectTransform kickerRect = TopBand("Kicker", header, KickerTop, KickerHeight, Pad, Pad);
        refs.Kicker = AddText(
            kickerRect, font, spec.Kicker, KickerFontSize, AccentDefault, TextAlignmentOptions.TopLeft);
        refs.Kicker.characterSpacing = KickerSpacing;
        refs.Accents.Add(refs.Kicker);

        RectTransform titleRect = TopBand("Title", header, TitleTop, TitleHeight, Pad, Pad);
        refs.Title = AddText(
            titleRect, font, spec.Title, TitleFontSize, TitleColor, TextAlignmentOptions.TopLeft);
        refs.Title.fontStyle = FontStyles.Bold;

        RectTransform characters = TopBand(
            "Characters", header, CharactersTop, CharactersHeight, Pad, Pad);

        BuildCharacterRow(characters, skin, spec, font, refs);

        RectTransform participationRect = TopBand(
            "Participation", header, ParticipationTop, ParticipationHeight, Pad, Pad);
        refs.Participation = AddText(
            participationRect, font, spec.Participation, ParticipationFontSize,
            BodyColor, TextAlignmentOptions.Top);

        // ── Steps ───────────────────────────────────────────────
        RectTransform stepsRoot = CreateChild("Steps", rootRect);
        Stretch(stepsRoot, 0f, 0f, 0f, 0f);

        for (int i = 0; i < spec.StepCount; i++)
        {
            float top = StepsTop + i * (StepCardHeight + StepCardGap);
            string stepName = $"Step{i + 1}";

            RectTransform card = TopBand(stepName, stepsRoot, top, StepCardHeight, Pad, Pad);
            AddSlicedImage(card, skin.CardFill, CardColor);

            RectTransform labelRect = TopBand(
                stepName + "Label", card, StepLabelTop, StepLabelHeight, StepInnerPad, StepInnerPad);
            TextMeshProUGUI label = AddText(
                labelRect, font, spec.Steps[i][0], StepLabelFontSize,
                AccentDefault, TextAlignmentOptions.TopLeft);
            label.fontStyle = FontStyles.Bold;
            refs.StepLabels.Add(label);
            refs.Accents.Add(label);

            RectTransform bodyRect = TopBand(
                stepName + "Body", card, StepBodyTop, StepBodyHeight, StepInnerPad, StepInnerPad);
            TextMeshProUGUI body = AddText(
                bodyRect, font, spec.Steps[i][1], StepBodyFontSize,
                BodyColor, TextAlignmentOptions.TopLeft);
            body.lineSpacing = StepBodyLineSpacing;
            refs.StepBodies.Add(body);
        }

        // ── Footer ──────────────────────────────────────────────
        RectTransform footer = TopBand("Footer", rootRect, spec.FooterTop, FooterHeight, Pad, Pad);

        RectTransform checkRect = CreateChild("FooterCheck", footer);
        checkRect.anchorMin = new Vector2(0f, 0.5f);
        checkRect.anchorMax = new Vector2(0f, 0.5f);
        checkRect.pivot = new Vector2(0f, 0.5f);
        checkRect.sizeDelta = new Vector2(FooterCheckSize, FooterCheckSize);
        checkRect.anchoredPosition = Vector2.zero;

        Image check = AddImage(checkRect, AccentDefault);
        check.sprite = skin.Check;
        check.preserveAspect = true;
        refs.Accents.Add(check);

        RectTransform footerLabel = CreateChild("FooterLabel", footer);
        footerLabel.anchorMin = Vector2.zero;
        footerLabel.anchorMax = Vector2.one;
        footerLabel.pivot = new Vector2(0.5f, 0.5f);
        footerLabel.offsetMin = new Vector2(FooterTextLeft, 0f);
        footerLabel.offsetMax = Vector2.zero;

        refs.Footer = AddText(
            footerLabel, font, spec.Footer, FooterFontSize, FooterColor, TextAlignmentOptions.Left);

        refs.DepthPrime = MissionGuideBoardSetup.BuildDepthPrime(rootRect, skin, size);

        // ── 스크립트 + 참조 + 문구 ──────────────────────────────
        var board = root.AddComponent<MissionGuideBoard>();
        var so = new SerializedObject(board);

        SetObject(so, "canvasGroup", refs.Group);
        SetObject(so, "kickerText", refs.Kicker);
        SetObject(so, "titleText", refs.Title);
        SetObject(so, "footerText", refs.Footer);
        SetObject(so, "lumenPortrait", refs.LumenPortrait);
        SetObject(so, "echoPortrait", refs.EchoPortrait);
        SetObject(so, "lumenNameText", refs.LumenName);
        SetObject(so, "echoNameText", refs.EchoName);
        SetObject(so, "participationText", refs.Participation);
        SetObject(so, "fogDepthPrime", refs.DepthPrime);

        SetArray(so, "accentGraphics", refs.Accents);
        SetColor(so, "accentColor", AccentDefault);

        SetString(so, "kicker", spec.Kicker);
        SetString(so, "title", spec.Title);
        SetString(so, "footer", spec.Footer);
        SetString(so, "lumenName", "루멘");
        SetString(so, "echoName", "에코");

        // 표시 모드가 무엇이든 같은 문구가 나오도록 셋 다 채운다.
        SetString(so, "participationBoth", spec.Participation);
        SetString(so, "participationLumenOnly", spec.Participation);
        SetString(so, "participationEchoOnly", spec.Participation);

        ApplyStepContent(so, spec, refs);
        ApplyDisplaySettings(so, spec);

        so.ApplyModifiedPropertiesWithoutUndo();

        board.ApplyContent();
        board.ApplyAccent();

        return root;
    }

    private static void ApplyStepContent(SerializedObject so, RootSpec spec, Refs refs)
    {
        SerializedProperty steps = so.FindProperty("steps");

        if (steps == null)
        {
            Debug.LogWarning("  ! MissionGuideBoard에 'steps' 필드가 없습니다.");
            return;
        }

        steps.arraySize = spec.StepCount;

        for (int i = 0; i < spec.StepCount; i++)
        {
            SerializedProperty element = steps.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("labelText").objectReferenceValue = refs.StepLabels[i];
            element.FindPropertyRelative("bodyText").objectReferenceValue = refs.StepBodies[i];
            element.FindPropertyRelative("label").stringValue = spec.Steps[i][0];
            element.FindPropertyRelative("body").stringValue = spec.Steps[i][1];
            element.FindPropertyRelative("icon").objectReferenceValue = null;
        }
    }

    private static void ApplyDisplaySettings(SerializedObject so, RootSpec spec)
    {
        SetEnum(so, "characterDisplay", (int)spec.Display);
        SetEnum(so, "visibleStep", (int)VisibleStep);
        SetEnum(so, "distanceMode", (int)Mode);
        SetBool(so, "hideDuringCutscene", true);
        SetFloat(so, "showDistance", ShowDistance);
        SetFloat(so, "hideHysteresis", HideHysteresis);
        SetFloat(so, "fadeDuration", FadeDuration);
        SetBool(so, "billboard", true);
        SetBool(so, "lockPitch", true);
    }

    /// <summary>
    /// 캐릭터 칸을 만든다. 담당이 한 명이면 <b>넓은 카드 하나</b>에 가운데로,
    /// 둘이면 기존처럼 반반 두 장으로 만든다.
    /// </summary>
    /// <remarks>
    /// 한 명짜리 미션(뿌리 1·2)에서 두 칸을 그대로 두면 한쪽이 빈 카드로 남는다.
    /// 그렇다고 초상을 키우면 다른 안내판과 크기가 달라 보이므로,
    /// <b>카드만 넓히고 초상·이름은 같은 크기 그대로</b> 가운데에 놓는다.
    /// 초상·이름·역할색 바는 이미 카드 가로 중앙에 정렬돼 있어
    /// (<c>anchorMin/Max.x = 0.5</c>, 이름은 카드 폭 전체 + 가운데 정렬)
    /// 카드 폭만 바꾸면 그대로 가운데로 온다.
    ///
    /// 표시하지 않는 쪽의 참조는 null로 둔다.
    /// <c>MissionGuideBoard.ApplyCharacters</c> 는 null을 모두 건너뛰므로
    /// (<c>SetGraphicActive</c> 와 텍스트 대입 양쪽에 null 가드가 있다) 그대로 동작한다.
    /// 배경색·둥근 모서리·역할색 바 등 나머지 디자인은 두 칸짜리와 완전히 같다.
    /// </remarks>
    private static void BuildCharacterRow(
        RectTransform characters, MissionGuideBoardSetup.Skin skin, RootSpec spec,
        TMP_FontAsset font, Refs refs)
    {
        Sprite lumenSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LumenPortraitPath);
        Sprite echoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(EchoPortraitPath);

        float fullWidth = BoardWidth - Pad * 2f;
        float halfWidth = (fullWidth - CharacterCardGap) * 0.5f;

        switch (spec.Display)
        {
            case MissionGuideBoard.CharacterDisplay.LumenOnly:
                refs.LumenPortrait = BuildCharacterCard(
                    characters, skin, "Lumen", 0f, fullWidth, lumenSprite,
                    "루멘", LumenColor, font, out refs.LumenName);
                break;

            case MissionGuideBoard.CharacterDisplay.EchoOnly:
                refs.EchoPortrait = BuildCharacterCard(
                    characters, skin, "Echo", 0f, fullWidth, echoSprite,
                    "에코", EchoColor, font, out refs.EchoName);
                break;

            default:
                refs.LumenPortrait = BuildCharacterCard(
                    characters, skin, "Lumen", 0f, halfWidth, lumenSprite,
                    "루멘", LumenColor, font, out refs.LumenName);

                refs.EchoPortrait = BuildCharacterCard(
                    characters, skin, "Echo", halfWidth + CharacterCardGap, halfWidth, echoSprite,
                    "에코", EchoColor, font, out refs.EchoName);
                break;
        }
    }

    private static Image BuildCharacterCard(
        RectTransform parent, MissionGuideBoardSetup.Skin skin, string name,
        float offsetX, float width, Sprite portraitSprite,
        string displayName, Color roleColor, TMP_FontAsset font, out TextMeshProUGUI nameText)
    {
        RectTransform card = CreateChild(name, parent);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0f, 0.5f);
        card.sizeDelta = new Vector2(width, 0f);
        card.anchoredPosition = new Vector2(offsetX, 0f);
        AddSlicedImage(card, skin.CardFill, CardColor);

        RectTransform portraitBack = CreateChild(name + "PortraitBack", card);
        portraitBack.anchorMin = new Vector2(0.5f, 1f);
        portraitBack.anchorMax = new Vector2(0.5f, 1f);
        portraitBack.pivot = new Vector2(0.5f, 1f);
        portraitBack.sizeDelta = new Vector2(PortraitSize + 16f, PortraitSize + 16f);
        portraitBack.anchoredPosition = new Vector2(0f, -(PortraitTop - 8f));
        AddSlicedImage(portraitBack, skin.CardFill, PortraitBackColor);

        RectTransform portraitRect = CreateChild(name + "Portrait", card);
        portraitRect.anchorMin = new Vector2(0.5f, 1f);
        portraitRect.anchorMax = new Vector2(0.5f, 1f);
        portraitRect.pivot = new Vector2(0.5f, 1f);
        portraitRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
        portraitRect.anchoredPosition = new Vector2(0f, -PortraitTop);

        Image portrait = AddImage(portraitRect, Color.white);
        portrait.preserveAspect = true;

        if (portraitSprite != null)
            portrait.sprite = portraitSprite;
        else
            Debug.LogWarning($"  ! {name} 초상 Sprite를 찾지 못했습니다. 빈 슬롯으로 둡니다.");

        RectTransform nameRect = TopBand(name + "Name", card, CharacterNameTop, CharacterNameHeight, 0f, 0f);
        nameText = AddText(
            nameRect, font, displayName, CharacterNameFontSize, roleColor, TextAlignmentOptions.Top);
        nameText.fontStyle = FontStyles.Bold;

        RectTransform roleBar = CreateChild(name + "Accent", card);
        roleBar.anchorMin = new Vector2(0.5f, 1f);
        roleBar.anchorMax = new Vector2(0.5f, 1f);
        roleBar.pivot = new Vector2(0.5f, 1f);
        roleBar.sizeDelta = new Vector2(RoleBarWidth, RoleBarHeight);
        roleBar.anchoredPosition = new Vector2(0f, -RoleBarTop);
        AddImage(roleBar, roleColor);

        return portrait;
    }

    // ───────────────────────────── 씬 인스턴스 ─────────────────────────────

    private static bool TryResolveInstance(Scene scene, GameObject prefab, RootSpec spec)
    {
        List<MissionGuideBoard> mine = FindBoards(scene, spec.PrefabPath);

        if (mine.Count > 1)
        {
            Debug.LogError(
                $"씬에 {spec.InstanceName} 이 {mine.Count}개 있습니다. " +
                "어느 것을 남길지 추측하지 않고 멈춥니다.");

            foreach (MissionGuideBoard duplicate in mine)
                Debug.LogError($"  · {GetPath(duplicate.transform)}", duplicate);

            return false;
        }

        if (mine.Count == 1)
        {
            spec.Board = mine[0];
            spec.Created = false;
            Debug.Log(
                $"  기존 인스턴스를 씁니다: {GetPath(spec.Board.transform)}  (중복 생성 없음)",
                spec.Board);
            return true;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

        if (instance == null)
        {
            Debug.LogError($"프리팹을 놓지 못했습니다: {spec.PrefabPath}");
            return false;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Mission 4 Guide Board");

        spec.Board = instance.GetComponent<MissionGuideBoard>();
        spec.Created = true;

        if (spec.Board == null)
        {
            Debug.LogError($"놓은 오브젝트에 MissionGuideBoard 가 없습니다: {instance.name}");
            return false;
        }

        Debug.Log($"  새로 놓았습니다: {spec.InstanceName} ({spec.AnchorName})", instance);
        return true;
    }

    private static void PlaceAndWire(RootSpec spec, Vector3 approachCenter)
    {
        MissionGuideBoard board = spec.Board;
        Transform boardTransform = board.transform;
        Vector3 anchorPosition = spec.Anchor.position;

        Undo.RecordObject(boardTransform, "Place Mission 4 Guide Board");

        if (spec.Created)
        {
            Vector3 direction = SideDirection(anchorPosition, approachCenter);
            Vector3 flat = anchorPosition + direction * PlacementDistance;

            float ground = ProbeGround(flat, anchorPosition.y, out string groundSource);
            var position = new Vector3(flat.x, ground + BoardCenterHeight, flat.z);

            // 빌보드와 같은 식. 뿌리 쪽에 선 사람을 향하도록 세운다.
            Vector3 forward = position - anchorPosition;
            forward.y = 0f;

            boardTransform.position = position;
            boardTransform.rotation = forward.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.identity;
            boardTransform.localScale = Vector3.one * BoardScale;

            Debug.Log(
                $"  [{spec.InstanceName}] 새 배치\n" +
                $"    기준 뿌리 : {spec.AnchorName} {Format(anchorPosition)}\n" +
                $"    방향      : {Format(direction)} (연못 쪽에서 {SideAngleDegrees}° 옆)\n" +
                $"    지면      : {ground:F3}  ({groundSource})\n" +
                $"    → 위치    : {Format(position)}  회전 Y {boardTransform.eulerAngles.y:F1}°",
                board);
        }
        else
        {
            Debug.Log(
                $"  [{spec.InstanceName}] 기존 Transform 유지 — " +
                $"위치 {Format(boardTransform.position)} / " +
                $"회전 Y {boardTransform.eulerAngles.y:F1}° / scale {boardTransform.localScale.x}",
                board);
        }

        // 프리팹 높이가 바뀌었을 때만 sizeDelta 를 맞춘다. 위치·회전·크기는 건드리지 않는다.
        if (boardTransform is RectTransform rect && rect.sizeDelta != spec.BoardSize)
        {
            Debug.Log($"    sizeDelta {rect.sizeDelta} → {spec.BoardSize}", board);
            rect.sizeDelta = spec.BoardSize;
        }

        if (board.gameObject.name != spec.InstanceName)
        {
            Undo.RecordObject(board.gameObject, "Rename Mission 4 Guide Board");
            board.gameObject.name = spec.InstanceName;
        }

        var so = new SerializedObject(board);
        SetObject(so, "anchor", spec.Anchor);
        ApplyDisplaySettings(so, spec);
        so.ApplyModifiedProperties();

        // Scene 뷰에서 결과를 볼 수 있게 알파를 올려 둔다.
        // 실행하면 Awake 가 0으로 되돌리고 조건에 맞을 때 페이드인한다.
        var group = board.GetComponent<CanvasGroup>();

        if (group != null && !Mathf.Approximately(group.alpha, 1f))
        {
            Undo.RecordObject(group, "Preview Mission 4 Guide Board");
            group.alpha = 1f;
            EditorUtility.SetDirty(group);
        }

        EditorUtility.SetDirty(board);
        EditorUtility.SetDirty(boardTransform);
    }

    /// <summary>
    /// 지면 높이. 기준보다 한참 위에 있는 적중은 "머리 위 구조물"로 보고 건너뛴다.
    /// </summary>
    private static float ProbeGround(Vector3 flat, float referenceY, out string source)
    {
        var origin = new Vector3(flat.x, referenceY + GroundProbeUp, flat.z);

        RaycastHit[] hits = Physics.RaycastAll(
            origin, Vector3.down, GroundProbeLength, ~0, QueryTriggerInteraction.Ignore);

        float ceiling = referenceY + MaxGroundAbove;

        bool found = false;
        float best = 0f;
        string bestName = null;
        int skipped = 0;

        foreach (RaycastHit hit in hits)
        {
            if (hit.point.y > ceiling)
            {
                skipped++;
                continue;
            }

            if (found && hit.point.y <= best)
                continue;

            best = hit.point.y;
            bestName = hit.collider.name;
            found = true;
        }

        string note = skipped > 0 ? $", 머리 위 {skipped}개 건너뜀" : string.Empty;

        if (found)
        {
            source = $"적중 '{bestName}' @ {best:F3}{note}";
            return best;
        }

        source = $"적중 없음{note} → 기준 높이로 대체";
        return referenceY + GroundFallbackOffset;
    }

    // ───────────────────────────── 검증 ─────────────────────────────

    private static void Validate()
    {
        Debug.Log("===== 미션 4 가이드 보드 [VALIDATE] =====");

        if (!TryGetScene(out Scene scene))
            return;

        int problems = 0;

        // ── 미션 1~3 안내판 보존 확인 (읽기만) ──────────────────
        problems += ReportOtherBoards(scene, Mission1PrefabPath, "미션 1", 1, true);
        problems += ReportOtherBoards(scene, Mission2PrefabPath, "미션 2", 3, true);
        problems += ReportOtherBoards(scene, Mission3PrefabPath, "미션 3", 1, true);

        // ── 미션 4 안내판 셋 ────────────────────────────────────
        var placed = new List<MissionGuideBoard>();

        foreach (RootSpec spec in Roots)
        {
            List<MissionGuideBoard> mine = FindBoards(scene, spec.PrefabPath);

            problems += Expect(mine.Count == 1,
                $"{spec.InstanceName} 1개 (현재 {mine.Count}개)", null);

            if (mine.Count != 1)
                continue;

            placed.Add(mine[0]);
            problems += ValidateBoard(scene, mine[0], spec);
        }

        // 안내판끼리 동시에 뜨지 않는지 — 서로의 기준 뿌리까지 거리를 본다.
        problems += CheckNoOverlap(scene);

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음. (실행 중 동작은 Play로 확인하세요)"
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    private static int ValidateBoard(Scene scene, MissionGuideBoard board, RootSpec spec)
    {
        GameObject go = board.gameObject;
        int problems = 0;

        Debug.Log($"── {GetPath(board.transform)} ──", go);

        problems += Expect(go.activeInHierarchy, "활성 상태", go);

        // Missing Script
        int missing = 0;

        foreach (Transform transform in go.GetComponentsInChildren<Transform>(true))
        {
            Component[] components = transform.GetComponents<Component>();

            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null)
                    continue;

                Debug.LogError($"  ✗ Missing Script: {GetPath(transform)} [{i}]", transform);
                missing++;
            }
        }

        problems += Expect(missing == 0, $"Missing Script 없음 ({missing}건)", go);

        var so = new SerializedObject(board);

        problems += ExpectRef(so, "anchor", spec.AnchorName);
        problems += ExpectRef(so, "canvasGroup", null);
        problems += ExpectRef(so, "kickerText", null);
        problems += ExpectRef(so, "titleText", null);
        problems += ExpectRef(so, "footerText", null);
        problems += ExpectRef(so, "participationText", null);

        // 담당 캐릭터만 칸을 만든다. 표시하지 않는 쪽은 참조가 비어 있는 것이 정상이다.
        bool showLumen = spec.Display != MissionGuideBoard.CharacterDisplay.EchoOnly;
        bool showEcho = spec.Display != MissionGuideBoard.CharacterDisplay.LumenOnly;

        problems += ExpectCharacter(so, "lumenPortrait", "lumenNameText", showLumen, "루멘");
        problems += ExpectCharacter(so, "echoPortrait", "echoNameText", showEcho, "에코");

        problems += ExpectEnum(so, "characterDisplay", (int)spec.Display, spec.Display.ToString());
        problems += ExpectEnum(so, "visibleStep", (int)VisibleStep, VisibleStep.ToString());
        problems += ExpectEnum(so, "distanceMode", (int)Mode, Mode.ToString());
        problems += ExpectBool(so, "hideDuringCutscene", true);
        problems += ExpectFloat(so, "showDistance", ShowDistance);
        problems += ExpectFloat(so, "hideHysteresis", HideHysteresis);
        problems += ExpectFloat(so, "fadeDuration", FadeDuration);
        problems += ExpectBool(so, "billboard", true);
        problems += ExpectBool(so, "lockPitch", true);

        SerializedProperty steps = so.FindProperty("steps");
        problems += Expect(steps != null && steps.arraySize == spec.StepCount,
            $"steps {spec.StepCount}칸 " +
            $"(현재 {(steps == null ? "필드 없음" : steps.arraySize.ToString())})", go);

        if (steps != null)
        {
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty element = steps.GetArrayElementAtIndex(i);
                problems += ExpectRelativeRef(element, "labelText", i);
                problems += ExpectRelativeRef(element, "bodyText", i);
            }
        }

        SerializedProperty depth = so.FindProperty("fogDepthPrime");

        if (depth == null || depth.objectReferenceValue == null)
            Debug.LogWarning("  ! fogDepthPrime 이 비어 있습니다. 화면 안개에 더 묻힙니다.", go);
        else
            Debug.Log($"  ✓ fogDepthPrime → {depth.objectReferenceValue.name}", go);

        // CanvasGroup / XR 안전
        var group = go.GetComponent<CanvasGroup>();
        problems += Expect(group != null, "CanvasGroup 있음", go);

        if (group != null)
        {
            problems += Expect(!group.interactable, "CanvasGroup.interactable = false", go);
            problems += Expect(!group.blocksRaycasts, "CanvasGroup.blocksRaycasts = false", go);
        }

        var canvas = go.GetComponent<Canvas>();
        problems += Expect(canvas != null && canvas.renderMode == RenderMode.WorldSpace,
            "Canvas = World Space", go);

        problems += Expect(go.GetComponentInChildren<GraphicRaycaster>(true) == null,
            "GraphicRaycaster 없음", go);
        problems += Expect(go.GetComponentInChildren<Collider>(true) == null,
            "Collider 없음", go);

        foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>(true))
        {
            if (!graphic.raycastTarget)
                continue;

            Debug.LogError($"  ✗ raycastTarget이 켜져 있습니다: {graphic.name}", graphic);
            problems++;
        }

        // 폰트 + 글 넘침
        foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == null)
            {
                Debug.LogError($"  ✗ {text.name}: Font Asset이 비어 있습니다.", text);
                problems++;
                continue;
            }

            var rect = text.rectTransform;

            if (rect.rect.width <= 1f || rect.rect.height <= 1f)
                continue;

            Vector2 preferred = text.GetPreferredValues(text.text, rect.rect.width, 0f);

            if (preferred.y > rect.rect.height + 1f)
            {
                Debug.LogWarning(
                    $"  ! {text.name}: 글 높이 {preferred.y:F0} > 영역 {rect.rect.height:F0} — " +
                    "잘릴 수 있습니다.", text);
                problems++;
            }
            else if (preferred.x > rect.rect.width + 1f)
            {
                Debug.Log(
                    $"  · {text.name}: 한 줄이 폭을 넘어 접힙니다 " +
                    $"({preferred.x:F0} > {rect.rect.width:F0}) — 높이는 여유가 있습니다.", text);
            }
        }

        // 위치 상식 검사
        SerializedProperty anchorProperty = so.FindProperty("anchor");
        var anchorTransform = anchorProperty != null
            ? anchorProperty.objectReferenceValue as Transform
            : null;

        if (anchorTransform != null)
        {
            Vector3 delta = board.transform.position - anchorTransform.position;
            float heightDelta = delta.y;
            delta.y = 0f;

            var rect = board.transform as RectTransform;
            Vector2 world = rect != null ? rect.sizeDelta * board.transform.localScale.x : Vector2.zero;

            Debug.Log(
                $"  · 위치 {Format(board.transform.position)} / 월드 {world.x:F2} × {world.y:F2} m\n" +
                $"  · 기준 뿌리까지 수평 {delta.magnitude:F2} m / 높이차 {heightDelta:F2} m", go);

            problems += Expect(delta.magnitude < ShowDistance,
                $"showDistance({ShowDistance} m) 안 (현재 {delta.magnitude:F2} m)", go);
            problems += Expect(Mathf.Approximately(board.transform.localScale.x, BoardScale),
                $"Scale {BoardScale} (현재 {board.transform.localScale.x})", go);
        }

        return problems;
    }

    /// <summary>
    /// 두 안내판이 동시에 뜨지 않는지. 기준 뿌리 사이 거리가 "꺼지는 거리"의 두 배를 넘어야 한다.
    /// </summary>
    private static int CheckNoOverlap(Scene scene)
    {
        float hideDistance = ShowDistance + HideHysteresis;
        float required = hideDistance * 2f;

        int problems = 0;

        for (int i = 0; i < Roots.Length; i++)
        {
            for (int j = i + 1; j < Roots.Length; j++)
            {
                if (!TryFind(scene, Roots[i].AnchorName, out Transform a, false) ||
                    !TryFind(scene, Roots[j].AnchorName, out Transform b, false))
                    continue;

                Vector3 delta = a.position - b.position;
                delta.y = 0f;

                problems += Expect(delta.magnitude > required,
                    $"{Roots[i].AnchorName} ↔ {Roots[j].AnchorName} {delta.magnitude:F1} m > " +
                    $"{required:F1} m (동시 표시 없음)", null);
            }
        }

        return problems;
    }

    private static int ReportOtherBoards(
        Scene scene, string prefabPath, string label, int expected, bool strict)
    {
        List<MissionGuideBoard> boards = FindBoards(scene, prefabPath);

        int problems = 0;

        if (boards.Count == expected)
        {
            Debug.Log($"  ✓ {label} 안내판 {expected}개 보존");
        }
        else if (strict)
        {
            Debug.LogError($"  ✗ {label} 안내판 {expected}개 보존 (현재 {boards.Count}개)");
            problems++;
        }
        else
        {
            Debug.LogWarning($"  ! {label} 안내판이 {boards.Count}개입니다(기대 {expected}개).");
        }

        foreach (MissionGuideBoard other in boards)
        {
            Transform t = other.transform;
            Debug.Log(
                $"  · {label} — {GetPath(t)} 위치 {Format(t.position)} scale {t.localScale.x} " +
                "(이 도구는 건드리지 않음)", other);
        }

        return problems;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan(Vector3 approachCenter)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"  표시 단계     : {VisibleStep} (= 미션 4 진행 중)");
        text.AppendLine($"  거리          : {Mode} / show {ShowDistance} / hyst {HideHysteresis}");
        text.AppendLine($"  연못 중심     : {Format(approachCenter)}  (플레이어가 오는 쪽)");
        text.AppendLine($"  안내판        : {Roots.Length}개 — 뿌리마다 조작이 달라 각각 배치");

        foreach (RootSpec spec in Roots)
        {
            Vector3 direction = SideDirection(spec.Anchor.position, approachCenter);
            Vector3 flat = spec.Anchor.position + direction * PlacementDistance;

            text.AppendLine();
            text.AppendLine($"  ── {spec.InstanceName} ({spec.AnchorName}) ──");
            text.AppendLine($"    기준 위치 : {Format(spec.Anchor.position)}");
            text.AppendLine($"    예상 XZ   : ({flat.x:F3}, ?, {flat.z:F3})  " +
                            "(Y는 지면 레이캐스트, 이미 놓였으면 기존 Transform 유지)");
            text.AppendLine($"    보드 크기 : {BoardWidth} × {spec.BoardHeight} @ {BoardScale} " +
                            $"→ 월드 {BoardWidth * BoardScale:F2} × {spec.BoardHeight * BoardScale:F2} m");
            text.AppendLine($"    표시 캐릭터: {spec.Display}");
            text.AppendLine($"    {spec.Title} / {spec.Participation}");

            foreach (string[] step in spec.Steps)
                text.AppendLine($"      {step[0]} : {step[1].Replace("\n", " / ")}");

            text.AppendLine($"      {spec.Footer}");
        }

        Debug.Log(text.ToString().TrimEnd());
    }

    // ───────────────────────────── 보조 ─────────────────────────────

    private static bool TryGetScene(out Scene result)
    {
        result = default;

        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded || scene.name != SceneName)
                continue;

            result = scene;
            return true;
        }

        Debug.LogError(
            $"'{SceneName}' 씬이 열려 있지 않습니다.\n" +
            "Assets/ReBloom/Scenes/Stage2.unity 를 연 뒤 다시 실행하세요.");
        return false;
    }

    private static bool TryFind(Scene scene, string name, out Transform found, bool logMissing = true)
    {
        found = null;

        var matches = new List<Transform>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                    matches.Add(transform);
            }
        }

        if (matches.Count == 0)
        {
            if (logMissing)
                Debug.LogError($"'{name}' 오브젝트를 찾지 못했습니다.");

            return false;
        }

        if (matches.Count > 1)
            Debug.LogWarning($"'{name}' 이름의 오브젝트가 {matches.Count}개입니다. 첫 번째를 씁니다.");

        found = matches[0];
        return true;
    }

    /// <summary>지정한 프리팹에서 나온 안내판 인스턴스만 모은다. 미션별로 섞지 않기 위함이다.</summary>
    private static List<MissionGuideBoard> FindBoards(Scene scene, string prefabPath)
    {
        var result = new List<MissionGuideBoard>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MissionGuideBoard board in root.GetComponentsInChildren<MissionGuideBoard>(true))
            {
                GameObject source =
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(board.gameObject);
                string path = source != null ? AssetDatabase.GetAssetPath(source) : null;

                if (path == prefabPath)
                    result.Add(board);
            }
        }

        return result;
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;

        for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            path = parent.name + "/" + path;

        return path;
    }

    private static string Format(Vector3 value)
        => $"({value.x:F3}, {value.y:F3}, {value.z:F3})";

    private static RectTransform CreateChild(string name, RectTransform parent)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        child.layer = parent.gameObject.layer;

        var rect = (RectTransform)child.transform;
        rect.localPosition = Vector3.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static RectTransform TopBand(
        string name, RectTransform parent, float top, float height, float left, float right)
    {
        RectTransform rect = CreateChild(name, parent);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -(top + height));
        rect.offsetMax = new Vector2(-right, -top);
        return rect;
    }

    private static Image AddImage(RectTransform rect, Color color)
    {
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image AddSlicedImage(RectTransform rect, Sprite sprite, Color color)
    {
        Image image = AddImage(rect, color);

        if (sprite == null)
            return image;

        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.fillCenter = true;
        return image;
    }

    private static TextMeshProUGUI AddText(
        RectTransform rect, TMP_FontAsset font, string content,
        float fontSize, Color color, TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.alignment = alignment;
        text.color = color;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        if (font != null)
            text.font = font;

        return text;
    }

    private static Color WithAlpha(Color color, float alpha)
        => new Color(color.r, color.g, color.b, alpha);

    private static void SetObject(SerializedObject so, string field, Object value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{field}' 필드가 없습니다.");
            return;
        }

        p.objectReferenceValue = value;
    }

    private static void SetString(SerializedObject so, string field, string value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{field}' 필드가 없습니다.");
            return;
        }

        p.stringValue = value;
    }

    private static void SetColor(SerializedObject so, string field, Color value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p != null)
            p.colorValue = value;
    }

    private static void SetEnum(SerializedObject so, string field, int value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p != null)
            p.enumValueIndex = value;
        else
            Debug.LogWarning($"  ! MissionGuideBoard에 '{field}' 필드가 없습니다.");
    }

    private static void SetBool(SerializedObject so, string field, bool value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p != null)
            p.boolValue = value;
    }

    private static void SetFloat(SerializedObject so, string field, float value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p != null)
            p.floatValue = value;
    }

    private static void SetArray(SerializedObject so, string field, List<Graphic> values)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
            return;

        p.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
            p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
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

    private static int ExpectRef(SerializedObject so, string field, string expectedName)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogError($"  ✗ '{field}' 필드가 없습니다.");
            return 1;
        }

        if (p.objectReferenceValue == null)
        {
            Debug.LogError($"  ✗ {field} 참조가 비어 있습니다.");
            return 1;
        }

        string actual = p.objectReferenceValue.name;

        if (expectedName != null && actual != expectedName)
        {
            Debug.LogError($"  ✗ {field} → {actual} (기대: {expectedName})");
            return 1;
        }

        Debug.Log($"  ✓ {field} → {actual}");
        return 0;
    }

    private static int ExpectRelativeRef(SerializedProperty element, string field, int index)
    {
        SerializedProperty p = element.FindPropertyRelative(field);

        if (p == null || p.objectReferenceValue == null)
        {
            Debug.LogError($"  ✗ steps[{index}].{field} 참조가 비어 있습니다.");
            return 1;
        }

        Debug.Log($"  ✓ steps[{index}].{field} → {p.objectReferenceValue.name}");
        return 0;
    }

    /// <summary>
    /// 캐릭터 한 명분 검사. 표시하는 캐릭터면 초상·이름·Sprite 가 모두 있어야 하고,
    /// 표시하지 않는 캐릭터면 셋 다 비어 있어야 한다(빈 카드가 남지 않았다는 뜻).
    /// </summary>
    private static int ExpectCharacter(
        SerializedObject so, string portraitField, string nameField, bool shown, string label)
    {
        SerializedProperty portrait = so.FindProperty(portraitField);
        SerializedProperty nameText = so.FindProperty(nameField);

        if (portrait == null || nameText == null)
        {
            Debug.LogError($"  ✗ '{portraitField}' / '{nameField}' 필드가 없습니다.");
            return 1;
        }

        if (!shown)
        {
            if (portrait.objectReferenceValue != null || nameText.objectReferenceValue != null)
            {
                Debug.LogError(
                    $"  ✗ {label} 는 이 안내판에 표시하지 않는데 참조가 남아 있습니다 " +
                    "(빈 캐릭터 칸이 생깁니다).");
                return 1;
            }

            Debug.Log($"  ✓ {label} 칸 없음 (1인용 패널)");
            return 0;
        }

        int problems = 0;
        problems += ExpectRef(so, portraitField, null);
        problems += ExpectRef(so, nameField, null);
        problems += ExpectSprite(so, portraitField);
        return problems;
    }

    private static int ExpectSprite(SerializedObject so, string field)
    {
        SerializedProperty p = so.FindProperty(field);
        var image = p != null ? p.objectReferenceValue as Image : null;

        if (image == null)
            return 0;

        if (image.sprite == null)
        {
            Debug.LogError($"  ✗ {field}.sprite 가 비어 있습니다.", image);
            return 1;
        }

        Debug.Log($"  ✓ {field}.sprite → {image.sprite.name}", image);
        return 0;
    }

    private static int ExpectEnum(SerializedObject so, string field, int expected, string label)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogError($"  ✗ '{field}' 필드가 없습니다.");
            return 1;
        }

        if (p.enumValueIndex != expected)
        {
            Debug.LogError($"  ✗ {field} = {p.enumValueIndex} (기대: {label})");
            return 1;
        }

        Debug.Log($"  ✓ {field} = {label}");
        return 0;
    }

    private static int ExpectBool(SerializedObject so, string field, bool expected)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogError($"  ✗ '{field}' 필드가 없습니다.");
            return 1;
        }

        if (p.boolValue != expected)
        {
            Debug.LogError($"  ✗ {field} = {p.boolValue} (기대: {expected})");
            return 1;
        }

        Debug.Log($"  ✓ {field} = {expected}");
        return 0;
    }

    private static int ExpectFloat(SerializedObject so, string field, float expected)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogError($"  ✗ '{field}' 필드가 없습니다.");
            return 1;
        }

        if (!Mathf.Approximately(p.floatValue, expected))
        {
            Debug.LogError($"  ✗ {field} = {p.floatValue} (기대: {expected})");
            return 1;
        }

        Debug.Log($"  ✓ {field} = {expected}");
        return 0;
    }
}
