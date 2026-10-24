using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage2 MISSION 3(살아 있는 뿌리 찾기) 미션 힌트 안내판을 만들고 씬에 놓는 도구.
///
/// 미션 1·2 안내판과 같은 <see cref="MissionGuideBoard"/> 컴포넌트·같은 디자인을 쓰되,
/// 프리팹은 따로 둔다. 미션 1·2 프리팹과 씬 배치는 건드리지 않는다.
///
/// <b>안내판을 그루터기마다 두지 않는 이유</b> —
/// 미션 3은 "여섯 그루터기 중 살아 있는 셋을 가려내는" 탐색 미션이다.
/// <c>MissionOutlineHighlighter_2</c> 의 <c>plantCompleteOutlines</c> 에 그루터기 <b>여섯 개</b>가
/// 모두 들어 있어(미션 4의 <c>stumpCompleteOutlines</c> 는 살아 있는 셋뿐이다) 이 단계에서는
/// 여섯 개가 똑같이 빛난다. 살아 있는 그루터기 옆에만 안내판을 세우면 답을 그대로 알려 주는 셈이라
/// 미션이 사라진다. 그래서 <b>그루터기 지대 입구에 하나만</b ></b> 세우고 "찾는 방법"만 설명한다.
/// (미션 2는 연꽃 세 송이가 모두 목표였으므로 세 곳에 두는 것이 맞았다)
///
/// <b>거리 기준점을 비워 두는 이유</b> —
/// 이 안내판은 특정 목표물이 아니라 "지대 입구 표지판"이다. 그래서 <c>anchor</c> 를 비워 두고
/// <see cref="MissionGuideBoard"/> 가 자기 위치를 기준으로 거리를 재게 한다
/// (<c>ShouldShow</c>: <c>anchor != null ? anchor.position : transform.position</c>).
/// 표지판을 옮기면 감지 범위도 함께 따라와 손으로 자리를 조정하기 쉽다.
///
/// 공용 에셋(둥근 패널·발광·체크 스프라이트, 깊이 프라임 머티리얼)은
/// <see cref="MissionGuideBoardSetup.Skin"/> 이 만들어 둔 것을 그대로 쓴다.
///
/// 이미 놓여 있는 안내판은 다시 만들지 않고, 손으로 맞춘 위치·회전·크기도 건드리지 않는다.
/// 프리팹 높이가 바뀌었을 때 필요한 <c>sizeDelta</c> 만 맞춰 준다.
///
/// 메뉴는 하나로 끝난다 — Build and Place 한 번이면 프리팹 생성·배치·참조 연결·검증까지
/// 모두 수행한다. 씬 저장만 직접 하면 된다(Ctrl+Z 로 되돌릴 수 있도록 자동 저장하지 않는다).
/// </summary>
public static class Mission3GuideBoardSetup
{
    private const string SceneName = "Stage2";
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission3.prefab";
    private const string Mission1PrefabPath = PrefabFolder + "/MissionGuideBoard.prefab";
    private const string Mission2PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission2.prefab";
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    private const string LumenPortraitPath = "Assets/ReBloom/UI/ReBloom_Lumen_Portrait.png";
    private const string EchoPortraitPath = "Assets/ReBloom/UI/ReBloom_Echo_Portrait.png";

    private const string InstanceName = "MissionGuideBoard_Mission3";

    /// <summary>
    /// 그루터기 지대를 이루는 오브젝트들. 살아 있는 것과 죽은 것을 모두 넣는다.
    /// 이 단계에서는 여섯 개가 똑같이 빛나므로, 지대의 범위를 재는 데 전부 필요하다.
    /// </summary>
    private static readonly string[] StumpNames =
    {
        "AliveStump", "AliveStump2", "AliveStump3",
        "DeadStump", "DeadStump2", "DeadStump3",
    };

    /// <summary>적어도 이 이름들은 있어야 한다. 없으면 지대를 잡을 수 없다.</summary>
    private static readonly string[] RequiredStumpNames =
    {
        "AliveStump", "AliveStump2", "AliveStump3",
    };

    /// <summary>플레이어가 그루터기 지대로 들어오는 쪽. 미션 2가 벌어지는 연못이다.</summary>
    private static readonly string[] ApproachFromNames =
    {
        "InteractablePlant1", "InteractablePlant2", "InteractablePlant3",
    };

    // ── 레이아웃 (미션 2와 동일) ──────────────────────────────────────────
    private const float BoardWidth = 1000f;
    private const float BoardScale = 0.001f;           // 미션 1·2 인스턴스와 같은 배율

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

    // ── 색 (미션 1·2와 동일) ──────────────────────────────────────────────
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
    //
    // 그루터기 지대에서 연못 쪽으로 가장 많이 나와 있는 그루터기보다 이만큼 더 앞(연못 쪽).
    // 지대에 들어서기 직전에 읽히되, 어떤 그루터기도 가리지 않는 거리다.
    private const float EntranceMargin = 4f;

    // 지면에서 판 중심까지. 미션 1·2와 같은 값이다.
    private const float BoardCenterHeight = 1.25f;

    // 기준 높이보다 이보다 높은 레이캐스트 적중은 지면으로 치지 않는다.
    // 미션 1에서 머리 위 구조물을 지면으로 잡아 판이 4.5 m 떠올랐던 적이 있다.
    private const float MaxGroundAbove = 1f;
    private const float GroundFallbackOffset = -0.4f;
    private const float GroundProbeUp = 8f;
    private const float GroundProbeLength = 24f;

    // ── 표시 조건 ─────────────────────────────────────────────────────────
    //
    // visibleStep 근거 (TutorialMissionManager_2 의 전환 코드) —
    //   PlantClearSequence.MissionCleared → PlantComplete  → MISSION 3 뿌리 찾기   ← 여기
    //   RootMissionManager.RootsFound     → StumpComplete  → MISSION 4 뿌리 활성화
    // 즉 PlantComplete 구간이 곧 미션 3 진행 중이다. 경계가 모호하지 않다.
    private const TutorialStep_2 VisibleStep = TutorialStep_2.PlantComplete;

    private const MissionGuideBoard.DistanceMode Mode = MissionGuideBoard.DistanceMode.Horizontal;

    // 미션 1·2(6 m)보다 넓게 잡는다. 목표물 옆에 붙은 안내판이 아니라
    // 폭이 30 m 넘는 지대의 입구 표지판이라, 지나가며 읽히려면 반경이 더 필요하다.
    private const float ShowDistance = 8f;
    private const float HideHysteresis = 0.5f;
    private const float FadeDuration = 0.3f;

    // ── 문구 ──────────────────────────────────────────────────────────────
    //
    // 전부 실제 코드에서 확인한 동작만 적었다.
    //  · 발견 판정 : LivingRoot.OnTriggerStay — enabled + "Right Controller" 태그 접촉 +
    //                RoleManager.LocalIsEar. 셋을 모두 만족해야 OnRootFound 가 불린다.
    //  · 진동      : TriggerRightHandHaptic — XRNode.RightHand, amplitude 0.25 /
    //                duration 0.15 / interval 0.7초 주기. 에코에게만 간다.
    //  · 발견 표시 : LivingRoot.MarkFound → SetFoundEffect(true) → SetFoundMarker.
    //                바닥 파티클(foundEffect)은 showFoundCircle 이 모든 인스턴스에서 0 이라
    //                재생되지 않는다(LivingRoot.cs 의 `if (visible && !showFoundCircle) return;`).
    //                실제로 켜지는 것은 그루터기 위 마커 스프라이트이고, 양쪽 플레이어 모두 본다.
    //  · 완료      : FoundCount >= 3 → NetFindComplete → RootsFound (미션 4로 전환)
    // 루멘에게는 발견 조작이 없다. 없는 조작을 만들어 적지 않는다.
    //
    // 힌트 중심 개편 —
    // 이 미션에서 안내가 없으면 막히는 핵심은 "겉모습으로 구별할 수 없다"는 사실 하나다.
    // (살아있는 그루터기 3개와 죽은 그루터기 3개의 Outline 설정이 전부 동일하다)
    // 그래서 그 사실과 "에코가 손을 대면 반응을 볼 수 있다"만 남기고,
    // 진동이라는 판별 수단·바닥 효과·찾아야 하는 개수는 카드에서 빼거나 하단 목표로 넘겼다.
    private const string Kicker = "MISSION 03 · 미션 힌트";
    private const string Title = "살아 있는 뿌리를 찾아라";
    private const string Participation = "뿌리는 에코만 찾을 수 있어요.";
    private const string Footer = "세 개를 모두 찾으면 뿌리 활성화가 시작됩니다.";

    private static readonly string[,] Steps =
    {
        {
            "01 · 겉모습만으로는 알 수 없어요",
            "살아 있는 뿌리와 죽은 그루터기는 겉모습이 비슷해요."
        },
        {
            "02 · 뿌리의 반응을 살펴보세요",
            "에코가 그루터기에 손을 대면\n" +
            "뿌리 주변에 나타나는 변화를 확인할 수 있어요."
        },
    };

    private static int StepCount => Steps.GetLength(0);

    private static float BoardHeight
    {
        get
        {
            float stepsEnd = StepsTop + StepCount * (StepCardHeight + StepCardGap) - StepCardGap;
            return stepsEnd + FooterGap + FooterHeight + BottomPad;
        }
    }

    private static float FooterTop => BoardHeight - BottomPad - FooterHeight;

    private static Vector2 BoardSize => new Vector2(BoardWidth, BoardHeight);

    // ───────────────────────────── 메뉴 ─────────────────────────────

    [MenuItem("Re_Bloom/Stage2/Mission 3 Guide Board - Dry Run", priority = 380)]
    private static void DryRunMenu() => BuildAndPlace(false);

    [MenuItem("Re_Bloom/Stage2/Mission 3 Guide Board - Build and Place", priority = 381)]
    private static void BuildAndPlaceMenu() => BuildAndPlace(true);

    [MenuItem("Re_Bloom/Stage2/Mission 3 Guide Board - Validate", priority = 382)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 본체 ─────────────────────────────

    private static void BuildAndPlace(bool apply)
    {
        string mode = apply ? "BUILD + PLACE" : "DRY RUN";
        Debug.Log($"===== 미션 3 가이드 보드 [{mode}] =====");

        if (!TryGetScene(out Scene scene))
            return;

        if (!TryComputeEntrance(scene, out Entrance entrance))
            return;

        ReportPlan(entrance);

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 아무것도 만들거나 바꾸지 않았습니다.\n" +
                "적용하려면 'Mission 3 Guide Board - Build and Place' 를 실행하세요.");
            return;
        }

        GameObject prefab = RebuildPrefab();

        if (prefab == null)
            return;

        if (!TryResolveInstance(scene, prefab, out MissionGuideBoard board, out bool created))
            return;

        PlaceAndWire(board, entrance, created);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = board.gameObject;
        EditorGUIUtility.PingObject(board.gameObject);

        Debug.Log(
            "완료했습니다. 씬은 자동 저장하지 않았습니다 — 확인 후 Ctrl+S 로 저장하세요.\n" +
            "되돌리려면 Ctrl+Z 입니다.");

        Validate();
    }

    // ───────────────────────────── 입구 계산 ─────────────────────────────

    /// <summary>그루터기 지대의 입구. 안내판을 세울 자리와 바라볼 방향.</summary>
    private struct Entrance
    {
        public Vector3 Center;        // 그루터기 지대 중심
        public Vector3 Direction;     // 지대 → 연못(플레이어가 오는 쪽) 수평 방향
        public Vector3 Flat;          // 안내판 XZ (Y는 레이캐스트로 결정)
        public float ReferenceY;      // 지면 탐색 기준 높이
        public string FrontStump;     // 가장 연못 쪽으로 나와 있던 그루터기
        public List<Transform> Stumps;
        public float NearestAliveDistance;
        public string NearestAliveName;
    }

    private static bool TryComputeEntrance(Scene scene, out Entrance entrance)
    {
        entrance = default;

        // ── 그루터기 모으기 ─────────────────────────────────────
        var stumps = new List<Transform>();
        var missing = new List<string>();

        foreach (string name in StumpNames)
        {
            if (TryFind(scene, name, out Transform stump, false))
                stumps.Add(stump);
            else
                missing.Add(name);
        }

        foreach (string required in RequiredStumpNames)
        {
            if (stumps.Exists(s => s.name == required))
                continue;

            Debug.LogError(
                $"'{required}' 를 찾지 못했습니다. 그루터기 지대를 잡을 수 없어 멈춥니다.");
            return false;
        }

        if (missing.Count > 0)
        {
            Debug.LogWarning(
                $"  ! 찾지 못한 그루터기: {string.Join(", ", missing)} — " +
                "남은 것만으로 지대를 계산합니다.");
        }

        // ── 접근 방향 (연못 쪽) ────────────────────────────────
        var approach = new List<Transform>();

        foreach (string name in ApproachFromNames)
        {
            if (TryFind(scene, name, out Transform plant, false))
                approach.Add(plant);
        }

        if (approach.Count == 0)
        {
            Debug.LogError(
                "연못(InteractablePlant1~3)을 하나도 찾지 못했습니다. " +
                "플레이어가 들어오는 방향을 정할 수 없어 멈춥니다.");
            return false;
        }

        Vector3 stumpCenter = Average(stumps);
        Vector3 approachCenter = Average(approach);

        Vector3 direction = approachCenter - stumpCenter;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.000001f)
        {
            Debug.LogError("그루터기 지대와 연못이 같은 자리입니다. 방향을 정할 수 없습니다.");
            return false;
        }

        direction = direction.normalized;

        // ── 가장 연못 쪽으로 나와 있는 그루터기 ─────────────────
        float frontReach = float.NegativeInfinity;
        string frontName = null;

        foreach (Transform stump in stumps)
        {
            Vector3 offset = stump.position - stumpCenter;
            offset.y = 0f;

            float reach = Vector3.Dot(offset, direction);

            if (reach <= frontReach)
                continue;

            frontReach = reach;
            frontName = stump.name;
        }

        Vector3 flat = stumpCenter + direction * (frontReach + EntranceMargin);
        flat.y = stumpCenter.y;

        // ── 살아 있는 그루터기와의 거리 (스포일러 점검) ─────────
        float nearestAlive = float.PositiveInfinity;
        string nearestAliveName = null;

        foreach (Transform stump in stumps)
        {
            if (System.Array.IndexOf(RequiredStumpNames, stump.name) < 0)
                continue;

            Vector3 delta = stump.position - flat;
            delta.y = 0f;

            if (delta.magnitude >= nearestAlive)
                continue;

            nearestAlive = delta.magnitude;
            nearestAliveName = stump.name;
        }

        entrance = new Entrance
        {
            Center = stumpCenter,
            Direction = direction,
            Flat = flat,
            ReferenceY = stumpCenter.y,
            FrontStump = frontName,
            Stumps = stumps,
            NearestAliveDistance = nearestAlive,
            NearestAliveName = nearestAliveName,
        };

        return true;
    }

    private static Vector3 Average(List<Transform> transforms)
    {
        Vector3 sum = Vector3.zero;

        foreach (Transform transform in transforms)
            sum += transform.position;

        return sum / transforms.Count;
    }

    // ───────────────────────────── 프리팹 ─────────────────────────────

    private static GameObject RebuildPrefab()
    {
        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            Debug.LogError($"폴더가 없습니다: {PrefabFolder}");
            return null;
        }

        bool existed = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;

        MissionGuideBoardSetup.Skin skin = MissionGuideBoardSetup.Skin.Load(true);

        GameObject root = BuildHierarchy(skin);

        try
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

            if (!success || saved == null)
            {
                Debug.LogError($"프리팹 저장에 실패했습니다: {PrefabPath}");
                return null;
            }

            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);

            Debug.Log(existed
                ? $"  프리팹을 갱신했습니다: {PrefabPath}  " +
                  "(씬 인스턴스의 위치·회전·크기 오버라이드는 유지됩니다)"
                : $"  프리팹을 만들었습니다: {PrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
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

    private static GameObject BuildHierarchy(MissionGuideBoardSetup.Skin skin)
    {
        var refs = new Refs();

        TMP_FontAsset font = skin.Font != null
            ? skin.Font
            : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath);

        Vector2 size = BoardSize;

        var root = new GameObject(InstanceName, typeof(RectTransform));
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
            kickerRect, font, Kicker, KickerFontSize, AccentDefault, TextAlignmentOptions.TopLeft);
        refs.Kicker.characterSpacing = KickerSpacing;
        refs.Accents.Add(refs.Kicker);

        RectTransform titleRect = TopBand("Title", header, TitleTop, TitleHeight, Pad, Pad);
        refs.Title = AddText(
            titleRect, font, Title, TitleFontSize, TitleColor, TextAlignmentOptions.TopLeft);
        refs.Title.fontStyle = FontStyles.Bold;

        RectTransform characters = TopBand(
            "Characters", header, CharactersTop, CharactersHeight, Pad, Pad);

        float cardWidth = (BoardWidth - Pad * 2f - CharacterCardGap) * 0.5f;

        refs.LumenPortrait = BuildCharacterCard(
            characters, skin, "Lumen", 0f, cardWidth,
            AssetDatabase.LoadAssetAtPath<Sprite>(LumenPortraitPath),
            "루멘", LumenColor, font, out refs.LumenName);

        refs.EchoPortrait = BuildCharacterCard(
            characters, skin, "Echo", cardWidth + CharacterCardGap, cardWidth,
            AssetDatabase.LoadAssetAtPath<Sprite>(EchoPortraitPath),
            "에코", EchoColor, font, out refs.EchoName);

        RectTransform participationRect = TopBand(
            "Participation", header, ParticipationTop, ParticipationHeight, Pad, Pad);
        refs.Participation = AddText(
            participationRect, font, Participation, ParticipationFontSize,
            BodyColor, TextAlignmentOptions.Top);

        // ── Steps ───────────────────────────────────────────────
        RectTransform stepsRoot = CreateChild("Steps", rootRect);
        Stretch(stepsRoot, 0f, 0f, 0f, 0f);

        for (int i = 0; i < StepCount; i++)
        {
            float top = StepsTop + i * (StepCardHeight + StepCardGap);
            string stepName = $"Step{i + 1}";

            RectTransform card = TopBand(stepName, stepsRoot, top, StepCardHeight, Pad, Pad);
            AddSlicedImage(card, skin.CardFill, CardColor);

            RectTransform labelRect = TopBand(
                stepName + "Label", card, StepLabelTop, StepLabelHeight, StepInnerPad, StepInnerPad);
            TextMeshProUGUI label = AddText(
                labelRect, font, Steps[i, 0], StepLabelFontSize,
                AccentDefault, TextAlignmentOptions.TopLeft);
            label.fontStyle = FontStyles.Bold;
            refs.StepLabels.Add(label);
            refs.Accents.Add(label);

            RectTransform bodyRect = TopBand(
                stepName + "Body", card, StepBodyTop, StepBodyHeight, StepInnerPad, StepInnerPad);
            TextMeshProUGUI body = AddText(
                bodyRect, font, Steps[i, 1], StepBodyFontSize, BodyColor, TextAlignmentOptions.TopLeft);
            body.lineSpacing = StepBodyLineSpacing;
            refs.StepBodies.Add(body);
        }

        // ── Footer ──────────────────────────────────────────────
        RectTransform footer = TopBand("Footer", rootRect, FooterTop, FooterHeight, Pad, Pad);

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
            footerLabel, font, Footer, FooterFontSize, FooterColor, TextAlignmentOptions.Left);

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

        SetString(so, "kicker", Kicker);
        SetString(so, "title", Title);
        SetString(so, "footer", Footer);
        SetString(so, "participationBoth", Participation);
        SetString(so, "lumenName", "루멘");
        SetString(so, "echoName", "에코");

        ApplyStepContent(so, refs);
        ApplyDisplaySettings(so);

        so.ApplyModifiedPropertiesWithoutUndo();

        board.ApplyContent();
        board.ApplyAccent();

        return root;
    }

    private static void ApplyStepContent(SerializedObject so, Refs refs)
    {
        SerializedProperty steps = so.FindProperty("steps");

        if (steps == null)
        {
            Debug.LogWarning("  ! MissionGuideBoard에 'steps' 필드가 없습니다.");
            return;
        }

        steps.arraySize = StepCount;

        for (int i = 0; i < StepCount; i++)
        {
            SerializedProperty element = steps.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("labelText").objectReferenceValue = refs.StepLabels[i];
            element.FindPropertyRelative("bodyText").objectReferenceValue = refs.StepBodies[i];
            element.FindPropertyRelative("label").stringValue = Steps[i, 0];
            element.FindPropertyRelative("body").stringValue = Steps[i, 1];
            element.FindPropertyRelative("icon").objectReferenceValue = null;
        }
    }

    /// <summary>
    /// 표시 조건.
    /// </summary>
    /// <remarks>
    /// <c>anchor</c> 는 일부러 비워 둔다. 이 안내판은 특정 목표물이 아니라 지대 입구 표지판이라,
    /// 자기 위치를 기준으로 거리를 재는 쪽이 맞다.
    /// </remarks>
    private static void ApplyDisplaySettings(SerializedObject so)
    {
        SetObject(so, "anchor", null);
        SetEnum(so, "characterDisplay", (int)MissionGuideBoard.CharacterDisplay.Both);
        SetEnum(so, "visibleStep", (int)VisibleStep);
        SetEnum(so, "distanceMode", (int)Mode);
        SetBool(so, "hideDuringCutscene", true);
        SetFloat(so, "showDistance", ShowDistance);
        SetFloat(so, "hideHysteresis", HideHysteresis);
        SetFloat(so, "fadeDuration", FadeDuration);
        SetBool(so, "billboard", true);
        SetBool(so, "lockPitch", true);
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

    private static bool TryResolveInstance(
        Scene scene, GameObject prefab, out MissionGuideBoard board, out bool created)
    {
        board = null;
        created = false;

        List<MissionGuideBoard> mine = FindBoards(scene, PrefabPath);

        if (mine.Count > 1)
        {
            Debug.LogError(
                $"씬에 미션 3 안내판이 {mine.Count}개 있습니다. 어느 것을 남길지 추측하지 않고 멈춥니다.");

            foreach (MissionGuideBoard duplicate in mine)
                Debug.LogError($"  · {GetPath(duplicate.transform)}", duplicate);

            return false;
        }

        if (mine.Count == 1)
        {
            board = mine[0];
            Debug.Log($"  기존 인스턴스를 씁니다: {GetPath(board.transform)}  (중복 생성 없음)", board);
            return true;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

        if (instance == null)
        {
            Debug.LogError($"프리팹을 놓지 못했습니다: {PrefabPath}");
            return false;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Mission 3 Guide Board");

        board = instance.GetComponent<MissionGuideBoard>();
        created = true;

        if (board == null)
        {
            Debug.LogError($"놓은 오브젝트에 MissionGuideBoard 가 없습니다: {instance.name}");
            return false;
        }

        Debug.Log($"  프리팹을 새로 놓았습니다: {instance.name}", instance);
        return true;
    }

    private static void PlaceAndWire(MissionGuideBoard board, Entrance entrance, bool created)
    {
        Transform boardTransform = board.transform;

        Undo.RecordObject(boardTransform, "Place Mission 3 Guide Board");

        if (created)
        {
            float ground = ProbeGround(entrance.Flat, entrance.ReferenceY, out string groundSource);
            var position = new Vector3(entrance.Flat.x, ground + BoardCenterHeight, entrance.Flat.z);

            // 빌보드와 같은 식. 플레이어는 연못 쪽(= -Direction)에 있으므로
            // 판의 앞면이 그쪽을 향하도록 지대 안쪽을 바라보게 세운다.
            Vector3 forward = entrance.Center - position;
            forward.y = 0f;

            boardTransform.position = position;
            boardTransform.rotation = forward.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(forward, Vector3.up)
                : Quaternion.identity;
            boardTransform.localScale = Vector3.one * BoardScale;

            Debug.Log(
                "  새 배치\n" +
                $"    그루터기 중심 : {Format(entrance.Center)}\n" +
                $"    연못 방향     : {Format(entrance.Direction)}\n" +
                $"    최전방 그루터기: {entrance.FrontStump} (여기서 {EntranceMargin} m 더 앞)\n" +
                $"    지면          : {ground:F3}  ({groundSource})\n" +
                $"    → 위치        : {Format(position)}  회전 Y {boardTransform.eulerAngles.y:F1}°",
                board);
        }
        else
        {
            Debug.Log(
                "  기존 Transform 유지 — " +
                $"위치 {Format(boardTransform.position)} / " +
                $"회전 Y {boardTransform.eulerAngles.y:F1}° / scale {boardTransform.localScale.x}",
                board);
        }

        if (boardTransform is RectTransform rect && rect.sizeDelta != BoardSize)
        {
            Debug.Log($"    sizeDelta {rect.sizeDelta} → {BoardSize}", board);
            rect.sizeDelta = BoardSize;
        }

        if (board.gameObject.name != InstanceName)
        {
            Undo.RecordObject(board.gameObject, "Rename Mission 3 Guide Board");
            board.gameObject.name = InstanceName;
        }

        var so = new SerializedObject(board);
        ApplyDisplaySettings(so);
        so.ApplyModifiedProperties();

        // Scene 뷰에서 결과를 볼 수 있게 알파를 올려 둔다.
        // 실행하면 Awake 가 0으로 되돌리고 조건에 맞을 때 페이드인한다.
        var group = board.GetComponent<CanvasGroup>();

        if (group != null && !Mathf.Approximately(group.alpha, 1f))
        {
            Undo.RecordObject(group, "Preview Mission 3 Guide Board");
            group.alpha = 1f;
            EditorUtility.SetDirty(group);
        }

        EditorUtility.SetDirty(board);
        EditorUtility.SetDirty(boardTransform);
    }

    /// <summary>
    /// 지면 높이. 기준 높이보다 한참 위에 있는 적중은 "머리 위 구조물"로 보고 건너뛴다.
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
        Debug.Log("===== 미션 3 가이드 보드 [VALIDATE] =====");

        if (!TryGetScene(out Scene scene))
            return;

        int problems = 0;

        // ── 미션 1·2 안내판 보존 확인 (읽기만) ──────────────────
        //
        // 미션 1은 배치가 확정돼 있어 개수가 어긋나면 오류로 본다.
        // 미션 2는 세 곳 배치 도구를 아직 돌리지 않았을 수 있으므로 알림만 남긴다.
        problems += ReportOtherBoards(scene, Mission1PrefabPath, "미션 1", 1, true);
        ReportOtherBoards(scene, Mission2PrefabPath, "미션 2", 3, false);

        // ── 미션 3 안내판 ───────────────────────────────────────
        List<MissionGuideBoard> mine = FindBoards(scene, PrefabPath);

        problems += Expect(mine.Count == 1, $"미션 3 안내판 1개 (현재 {mine.Count}개)", null);

        if (mine.Count != 1)
        {
            Debug.Log($"검증 실패 — 문제 {problems}건.");
            return;
        }

        MissionGuideBoard board = mine[0];
        GameObject go = board.gameObject;

        problems += Expect(go.activeInHierarchy, $"활성 상태 ({GetPath(board.transform)})", go);

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

        problems += ExpectRef(so, "canvasGroup");
        problems += ExpectRef(so, "kickerText");
        problems += ExpectRef(so, "titleText");
        problems += ExpectRef(so, "footerText");
        problems += ExpectRef(so, "participationText");
        problems += ExpectRef(so, "lumenPortrait");
        problems += ExpectRef(so, "echoPortrait");
        problems += ExpectRef(so, "lumenNameText");
        problems += ExpectRef(so, "echoNameText");

        // anchor 는 비어 있는 것이 정상이다 (자기 위치 기준 표지판).
        SerializedProperty anchorProperty = so.FindProperty("anchor");

        if (anchorProperty != null && anchorProperty.objectReferenceValue != null)
        {
            Debug.LogWarning(
                $"  ! anchor 가 {anchorProperty.objectReferenceValue.name} 로 연결돼 있습니다. " +
                "이 안내판은 자기 위치를 기준으로 재도록 비워 두는 것이 설계입니다.");
        }
        else
        {
            Debug.Log("  ✓ anchor 비어 있음 — 자기 위치 기준 (설계대로)");
        }

        problems += ExpectEnum(so, "characterDisplay",
            (int)MissionGuideBoard.CharacterDisplay.Both, "Both");
        problems += ExpectEnum(so, "visibleStep", (int)VisibleStep, VisibleStep.ToString());
        problems += ExpectEnum(so, "distanceMode", (int)Mode, Mode.ToString());
        problems += ExpectBool(so, "hideDuringCutscene", true);
        problems += ExpectFloat(so, "showDistance", ShowDistance);
        problems += ExpectFloat(so, "hideHysteresis", HideHysteresis);
        problems += ExpectFloat(so, "fadeDuration", FadeDuration);
        problems += ExpectBool(so, "billboard", true);
        problems += ExpectBool(so, "lockPitch", true);

        SerializedProperty steps = so.FindProperty("steps");
        problems += Expect(steps != null && steps.arraySize == StepCount,
            $"steps {StepCount}칸 (현재 {(steps == null ? "필드 없음" : steps.arraySize.ToString())})", go);

        if (steps != null)
        {
            for (int i = 0; i < steps.arraySize; i++)
            {
                SerializedProperty element = steps.GetArrayElementAtIndex(i);
                problems += ExpectRelativeRef(element, "labelText", i);
                problems += ExpectRelativeRef(element, "bodyText", i);
            }
        }

        problems += ExpectSprite(so, "lumenPortrait");
        problems += ExpectSprite(so, "echoPortrait");

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

        // ── 스포일러 점검 ───────────────────────────────────────
        //
        // 살아 있는 그루터기가 감지 반경 안에 있으면, 안내판이 "여기가 정답"이라고
        // 알려 주는 꼴이 된다. 미션 3은 여섯 중 셋을 가려내는 탐색이므로 그러면 안 된다.
        problems += CheckSpoiler(scene, board);

        // ── 위치 상식 검사 ──────────────────────────────────────
        var boardRect = board.transform as RectTransform;
        Vector2 world = boardRect != null
            ? boardRect.sizeDelta * board.transform.localScale.x
            : Vector2.zero;

        Debug.Log(
            $"  · 위치 {Format(board.transform.position)} / 월드 {world.x:F2} × {world.y:F2} m / " +
            $"scale {board.transform.localScale.x}", go);

        problems += Expect(Mathf.Approximately(board.transform.localScale.x, BoardScale),
            $"Scale {BoardScale} (현재 {board.transform.localScale.x})", go);

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음. (실행 중 동작은 Play로 확인하세요)"
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    private static int CheckSpoiler(Scene scene, MissionGuideBoard board)
    {
        float nearest = float.PositiveInfinity;
        string nearestName = null;

        foreach (string name in RequiredStumpNames)
        {
            if (!TryFind(scene, name, out Transform stump, false))
                continue;

            Vector3 delta = stump.position - board.transform.position;
            delta.y = 0f;

            if (delta.magnitude >= nearest)
                continue;

            nearest = delta.magnitude;
            nearestName = name;
        }

        if (nearestName == null)
        {
            Debug.LogWarning("  ! 살아 있는 그루터기를 찾지 못해 스포일러 점검을 건너뜁니다.");
            return 0;
        }

        return Expect(nearest > ShowDistance,
            $"살아 있는 그루터기가 감지 반경 밖 — 가장 가까운 {nearestName} {nearest:F1} m " +
            $"(showDistance {ShowDistance} m)", board.gameObject);
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
            Debug.LogWarning(
                $"  ! {label} 안내판이 {boards.Count}개입니다(기대 {expected}개). " +
                "해당 미션의 배치 도구를 아직 돌리지 않았다면 정상입니다.");
        }

        foreach (MissionGuideBoard other in boards)
        {
            Transform t = other.transform;
            Debug.Log(
                $"  · {label} 안내판 그대로 — {GetPath(t)} " +
                $"위치 {Format(t.position)} scale {t.localScale.x}  (이 도구는 건드리지 않음)",
                other);
        }

        return problems;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan(Entrance entrance)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"  프리팹        : {PrefabPath}");
        text.AppendLine($"  보드 크기     : {BoardWidth} × {BoardHeight} @ {BoardScale} " +
                        $"→ 월드 {BoardWidth * BoardScale:F2} × {BoardHeight * BoardScale:F2} m");
        text.AppendLine($"  STEP 칸 수    : {StepCount}");
        text.AppendLine($"  표시 단계     : {VisibleStep} (= 미션 3 진행 중)");
        text.AppendLine($"  거리          : {Mode} / show {ShowDistance} / hyst {HideHysteresis} " +
                        "/ anchor 비움(자기 위치 기준)");
        text.AppendLine($"  안내판        : 1개 (그루터기마다 두면 정답이 드러나므로 입구에만)");
        text.AppendLine($"  그루터기      : {entrance.Stumps.Count}개");

        foreach (Transform stump in entrance.Stumps)
            text.AppendLine($"    {stump.name,-14}{Format(stump.position)}");

        text.AppendLine($"  지대 중심     : {Format(entrance.Center)}");
        text.AppendLine($"  연못 방향     : {Format(entrance.Direction)}");
        text.AppendLine($"  최전방        : {entrance.FrontStump} (+{EntranceMargin} m 앞에 배치)");
        text.AppendLine($"  예상 XZ       : ({entrance.Flat.x:F3}, ?, {entrance.Flat.z:F3})  " +
                        "(Y는 지면 레이캐스트, 이미 놓였으면 기존 Transform 유지)");
        text.AppendLine($"  가장 가까운 살아 있는 그루터기 : {entrance.NearestAliveName} " +
                        $"{entrance.NearestAliveDistance:F1} m " +
                        $"({(entrance.NearestAliveDistance > ShowDistance ? "감지 반경 밖 — 정답 노출 없음" : "⚠ 반경 안 — 정답이 드러남")})");
        text.AppendLine("  문구 —");
        text.AppendLine($"    {Kicker} / {Title}");
        text.AppendLine($"    {Participation}");

        for (int i = 0; i < StepCount; i++)
            text.AppendLine($"    {Steps[i, 0]} : {Steps[i, 1].Replace("\n", " / ")}");

        text.Append($"    {Footer}");

        Debug.Log(text.ToString());
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

    private static int ExpectRef(SerializedObject so, string field)
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

        Debug.Log($"  ✓ {field} → {p.objectReferenceValue.name}");
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
