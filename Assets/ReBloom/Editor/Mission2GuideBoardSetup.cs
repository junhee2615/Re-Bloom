using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage2 MISSION 2(수생식물 되살리기) 미션 힌트 안내판을 만들고 연꽃 세 곳에 놓는 도구.
///
/// 미션 1 안내판(<see cref="MissionGuideBoardSetup"/>)과 같은 <see cref="MissionGuideBoard"/>
/// 컴포넌트·같은 디자인을 쓰되, 프리팹은 따로 둔다. 미션 1 프리팹과 씬 배치는 건드리지 않는다.
///
/// 미션 1과 다른 점 —
///  · 조작 단계가 <b>4칸</b>이다. 에코 입력 → 전달 → 루멘 입력 → 완료 조건 순서다.
///    그래서 보드 높이를 칸 수에 맞춰 계산한다(미션 1은 3칸 고정이었다).
///  · 안내판이 <b>세 개</b>다. 연꽃 세 송이가 연못에 20 m 넘게 흩어져 있어, 한 곳에만 두면
///    나머지 두 송이 앞에서는 읽을 수 없다. 각 안내판은 자기 연꽃을 기준으로 독립적으로
///    거리를 재므로 서로 간섭하지 않는다(가장 가까운 두 안내판 사이가 약 20 m,
///    showDistance 6 m 보다 훨씬 멀다).
///  · 문구를 <see cref="MissionGuideBoard"/> 의 기본값이 아니라 이 도구가 직렬화 필드에
///    직접 써 넣는다. 보드가 둘 이상이면 기본값 하나로는 감당이 안 되기 때문이다.
///  · 표시 단계가 <c>WaterComplete</c> 다. 미션 1이 끝나 "다음은 수생식물"로 넘어간 구간이
///    곧 미션 2 진행 중이다(<c>TutorialMissionManager_2</c>).
///
/// 공용 에셋(둥근 패널·발광·체크 스프라이트, 깊이 프라임 머티리얼)은
/// <see cref="MissionGuideBoardSetup.Skin"/> 이 만들어 둔 것을 그대로 쓴다.
///
/// 이미 놓여 있는 안내판은 <b>다시 만들지 않고 그대로 쓴다</b>. 손으로 맞춘 위치·회전·크기도
/// 건드리지 않는다. 프리팹 높이가 바뀌었을 때 필요한 <c>sizeDelta</c> 만 맞춰 준다.
///
/// 메뉴는 하나로 끝난다 — Build and Place 한 번이면 프리팹 갱신·세 곳 배치·참조 연결·검증까지
/// 모두 수행한다. 씬 저장만 직접 하면 된다(Ctrl+Z 로 되돌릴 수 있도록 자동 저장하지 않는다).
/// </summary>
public static class Mission2GuideBoardSetup
{
    private const string SceneName = "Stage2";
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/MissionGuideBoard_Mission2.prefab";
    private const string Mission1PrefabPath = PrefabFolder + "/MissionGuideBoard.prefab";
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    private const string LumenPortraitPath = "Assets/ReBloom/UI/ReBloom_Lumen_Portrait.png";
    private const string EchoPortraitPath = "Assets/ReBloom/UI/ReBloom_Echo_Portrait.png";

    private const string InstancePrefix = "MissionGuideBoard_Mission2_";

    /// <summary>안내판을 하나씩 세울 연꽃들. 순서가 곧 안내판 번호다.</summary>
    private static readonly string[] AnchorNames =
    {
        "InteractablePlant1",
        "InteractablePlant2",
        "InteractablePlant3",
    };

    // ── 레이아웃 (캔버스 단위) ─────────────────────────────────────────────
    // 가로와 머리말·제목·캐릭터 구역은 미션 1과 같은 값이다. 두 안내판이 같아 보여야 한다.
    private const float BoardWidth = 1000f;

    // 프리팹 기본값(0.00168)이 아니라 사용자가 미션 1 인스턴스에서 직접 맞춘 값을 따른다.
    // Stage2.unity 의 미션 1 안내판은 m_LocalScale 0.001 로 조정돼 있다.
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

    // STEP 카드.
    //
    // 본문 영역을 세 줄 높이로 잡는다. 문구는 두 줄로 썼지만 가장 긴 줄
    // ("루멘이 오른손 컨트롤러로 꽃잎을 리듬에 맞춰 톡톡 터치하세요.")이 카드 안쪽 폭
    // 872px 에 거의 꽉 차서, 폰트 메트릭이 조금만 달라져도 한 줄이 더 접힌다.
    // 그때 글이 잘리지 않도록 미리 한 줄 분을 비워 둔다. 네 칸 모두 같은 높이라
    // 두 줄로 들어가도 보기에 어색하지 않다.
    private const float StepsTop = 442f;
    private const float StepCardHeight = 224f;
    private const float StepCardGap = 14f;
    private const float StepInnerPad = 28f;
    private const float StepLabelTop = 22f;
    private const float StepLabelHeight = 46f;
    private const float StepLabelFontSize = 36f;
    private const float StepBodyTop = 84f;             // 제목 아래 16px
    private const float StepBodyHeight = 124f;         // 27pt 세 줄
    private const float StepBodyFontSize = 27f;
    private const float StepBodyLineSpacing = 4f;

    private const float FooterGap = 24f;
    private const float FooterHeight = 50f;
    private const float FooterCheckSize = 34f;
    private const float FooterTextLeft = 50f;
    private const float FooterFontSize = 28f;
    private const float BottomPad = 34f;

    // ── 색 (미션 1과 동일) ────────────────────────────────────────────────
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
    // 각 연꽃에서 "연못 바깥쪽"으로 3 m.
    // 바깥쪽 = 연꽃 세 송이의 중심에서 멀어지는 방향이다. 연꽃들은 연못 안에 있고
    // 플레이어는 물가에서 다가오므로, 이 방향이 둑 쪽이면서 동시에 플레이어가 오는 쪽이다.
    // (Plant1 에 적용하면 이미 놓여 있는 안내판 자리와 거의 같은 지점이 나온다)
    private const float PlacementDistance = 3f;

    // 지면에서 판 중심까지.
    // 미션 1 안내판의 최종 높이(월드 1.31, WaterwayZone 기준 +0.81)에 맞춘 값이다.
    private const float BoardCenterHeight = 1.25f;

    // 기준점보다 이보다 높은 레이캐스트 적중은 지면으로 치지 않는다.
    // 미션 1에서 수로 위 구조물을 지면으로 잡아 판이 4.5 m 떠올랐던 적이 있다.
    private const float MaxGroundAbove = 1f;
    private const float GroundFallbackOffset = -0.4f;
    private const float GroundProbeUp = 8f;
    private const float GroundProbeLength = 24f;

    // ── 표시 조건 ─────────────────────────────────────────────────────────
    private const MissionGuideBoard.DistanceMode Mode = MissionGuideBoard.DistanceMode.Horizontal;
    private const float ShowDistance = 6f;
    private const float HideHysteresis = 0.5f;
    private const float FadeDuration = 0.3f;

    // ── 문구 ──────────────────────────────────────────────────────────────
    //
    // 전부 실제 코드에서 확인한 동작만 적었다.
    //  · 에코 진동       : InteractablePlantVibration.OnTriggerEnter — "Right Controller" 태그
    //                      접촉 + RoleManager.LocalIsEar 일 때만 SendHapticImpulse
    //  · 루멘 꽃잎 터치  : PetalTouchForwarder.OnTriggerEnter — 같은 태그 접촉 →
    //                      PetalRhythmMission.OnPetalTouched, mentalOnly(기본 true)라
    //                      RoleManager.LocalIsMental 일 때만 유효
    //  · 완료            : 연꽃 3송이 IsCleared → PlantClearSequence.CheckCompletion
    // 두 입력 모두 접촉 판정이고 버튼을 쓰지 않는다. 그래서 문구에도 그렇게 적었다.
    //
    // 힌트 중심 개편 —
    // 손을 대 보면 알 수 있는 조작(접촉 위치·말로 전달하기·연꽃 3송이 찾기)은 빼고,
    // 안내가 없으면 막히는 두 가지만 남겼다:
    //   ① 역할이 갈려 있다(에코가 두드려도 반응이 없다)
    //   ② 리듬이 반복되므로 서두를 필요가 없다 (loopWhileTouching, loopGap 2초)
    // 판정 수치(beatTolerance 0.35 / requiredMatchRatio 0.6 / betweenInputTimeout 2.5)는
    // 정답에 해당하므로 적지 않는다.
    private const string Kicker = "MISSION 02 · 미션 힌트";
    private const string Title = "수생식물을 되살려라";
    private const string Participation = "에코와 루멘이 함께 해야 해요.";
    private const string Footer = "세 송이를 모두 되살리면 연못이 깨어납니다.";

    private static readonly string[,] Steps =
    {
        {
            "01 · 서로 다른 역할",
            "에코는 줄기에서 리듬을 느낄 수 있고,\n" +
            "루멘은 꽃잎을 깨울 수 있어요."
        },
        {
            "02 · 리듬에 집중하기",
            "리듬은 반복되니 서두르지 않아도 괜찮아요.\n" +
            "서로 박자를 맞춰보세요."
        },
        {
            "03 · 가볍게 두드리기",
            "꽃잎은 버튼을 누르지 않고 살짝 터치해도 반응해요."
        },
    };

    private static int StepCount => Steps.GetLength(0);

    /// <summary>칸 수에 맞춰 늘어나는 보드 높이.</summary>
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

    [MenuItem("Re_Bloom/Stage2/Mission 2 Guide Board - Dry Run", priority = 370)]
    private static void DryRunMenu() => BuildAndPlace(false);

    [MenuItem("Re_Bloom/Stage2/Mission 2 Guide Board - Build and Place", priority = 371)]
    private static void BuildAndPlaceMenu() => BuildAndPlace(true);

    [MenuItem("Re_Bloom/Stage2/Mission 2 Guide Board - Validate", priority = 372)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 슬롯 ─────────────────────────────

    /// <summary>안내판 한 개 분. 어느 연꽃을 맡고, 어떤 인스턴스가 그 자리에 있는지.</summary>
    private sealed class Slot
    {
        public int Index;
        public string AnchorName;
        public string InstanceName;
        public Transform Anchor;
        public MissionGuideBoard Board;
        public bool Created;
    }

    // ───────────────────────────── 본체 ─────────────────────────────

    private static void BuildAndPlace(bool apply)
    {
        string mode = apply ? "BUILD + PLACE" : "DRY RUN";
        Debug.Log($"===== 미션 2 가이드 보드 [{mode}] =====");

        if (!TryGetScene(out Scene scene))
            return;

        List<Slot> slots = BuildSlots(scene);

        if (slots == null)
            return;

        Vector3 center = AnchorCenter(slots);

        ReportPlan(slots, center);

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 아무것도 만들거나 바꾸지 않았습니다.\n" +
                "적용하려면 'Mission 2 Guide Board - Build and Place' 를 실행하세요.");
            return;
        }

        // ── ① 프리팹 (문구·높이가 바뀌므로 다시 만든다) ────────
        GameObject prefab = RebuildPrefab();

        if (prefab == null)
            return;

        // ── ② 씬 인스턴스 짝짓기 ───────────────────────────────
        if (!MatchExistingBoards(scene, slots))
            return;

        // ── ③ 배치 + 설정 ──────────────────────────────────────
        foreach (Slot slot in slots)
        {
            if (slot.Board == null && !CreateInstance(scene, prefab, slot))
                return;

            PlaceAndWire(slot, center);
        }

        EditorSceneManager.MarkSceneDirty(scene);

        if (slots[0].Board != null)
        {
            Selection.activeGameObject = slots[0].Board.gameObject;
            EditorGUIUtility.PingObject(slots[0].Board.gameObject);
        }

        Debug.Log(
            "완료했습니다. 씬은 자동 저장하지 않았습니다 — 확인 후 Ctrl+S 로 저장하세요.\n" +
            "되돌리려면 Ctrl+Z 입니다.");

        Validate();
    }

    /// <summary>연꽃 세 개를 찾아 슬롯을 만든다. 하나라도 없으면 멈춘다.</summary>
    private static List<Slot> BuildSlots(Scene scene)
    {
        var slots = new List<Slot>();

        for (int i = 0; i < AnchorNames.Length; i++)
        {
            if (!TryFind(scene, AnchorNames[i], out Transform anchor))
                return null;

            slots.Add(new Slot
            {
                Index = i,
                AnchorName = AnchorNames[i],
                InstanceName = InstancePrefix + "Plant" + (i + 1),
                Anchor = anchor,
            });
        }

        return slots;
    }

    /// <summary>연꽃 세 송이의 중심. 바깥 방향을 정하는 기준이다.</summary>
    private static Vector3 AnchorCenter(List<Slot> slots)
    {
        Vector3 sum = Vector3.zero;

        foreach (Slot slot in slots)
            sum += slot.Anchor.position;

        return sum / slots.Count;
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

        // 공용 스킨(둥근 패널·발광·체크 스프라이트 + 깊이 머티리얼).
        // 없으면 여기서 만들어진다. 미션 1 안내판과 같은 에셋을 공유한다.
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
                  "(문구·높이 변경. 씬 인스턴스의 위치·회전·크기 오버라이드는 유지됩니다)"
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

        // ── 루트 ────────────────────────────────────────────────
        var root = new GameObject(InstancePrefix + "Plant1", typeof(RectTransform));
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

        // ── 배경 ────────────────────────────────────────────────
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

        // ── 안개 대응 ───────────────────────────────────────────
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

        // 문구는 스크립트 기본값(미션 1용)을 쓰지 않고 직접 써 넣는다.
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

    /// <summary>표시 조건. anchor 는 씬 배치 단계에서 따로 잇는다(프리팹은 씬을 참조할 수 없다).</summary>
    private static void ApplyDisplaySettings(SerializedObject so)
    {
        SetEnum(so, "characterDisplay", (int)MissionGuideBoard.CharacterDisplay.Both);
        SetEnum(so, "visibleStep", (int)TutorialStep_2.WaterComplete);
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

    // ───────────────────────────── 씬 짝짓기 ─────────────────────────────

    /// <summary>
    /// 씬에 이미 있는 미션 2 안내판을 슬롯에 나눠 준다.
    /// </summary>
    /// <remarks>
    /// 짝은 <c>anchor</c> 로 맞춘다. 이름이 바뀌었거나 손으로 옮겼더라도 기준 연꽃만 같으면
    /// 그 자리의 안내판으로 인정한다. anchor 가 비어 있거나 모르는 것을 가리키는 안내판은
    /// 남는 슬롯에 순서대로 넣는다(그래야 예전에 만든 것을 버리고 새로 만들지 않는다).
    /// 슬롯보다 안내판이 많으면 어느 것을 버릴지 추측하지 않고 멈춘다.
    /// </remarks>
    private static bool MatchExistingBoards(Scene scene, List<Slot> slots)
    {
        List<MissionGuideBoard> found = FindBoards(scene, PrefabPath);

        if (found.Count > slots.Count)
        {
            Debug.LogError(
                $"씬에 미션 2 안내판이 {found.Count}개 있습니다(자리는 {slots.Count}개). " +
                "어느 것을 버릴지 추측하지 않고 멈춥니다. 여분을 지우고 다시 실행하세요.");

            foreach (MissionGuideBoard duplicate in found)
                Debug.LogError($"  · {GetPath(duplicate.transform)}", duplicate);

            return false;
        }

        var leftovers = new List<MissionGuideBoard>();

        // ① anchor 가 맞는 것부터 제자리에 넣는다.
        foreach (MissionGuideBoard board in found)
        {
            var so = new SerializedObject(board);
            SerializedProperty anchorProperty = so.FindProperty("anchor");
            var anchor = anchorProperty != null
                ? anchorProperty.objectReferenceValue as Transform
                : null;

            Slot match = anchor != null
                ? slots.Find(s => s.Anchor == anchor && s.Board == null)
                : null;

            if (match != null)
            {
                match.Board = board;
                Debug.Log(
                    $"  기존 인스턴스를 씁니다: {GetPath(board.transform)} → {match.AnchorName}",
                    board);
            }
            else
            {
                leftovers.Add(board);
            }
        }

        // ② 남은 것은 빈 자리에 순서대로.
        foreach (MissionGuideBoard board in leftovers)
        {
            Slot free = slots.Find(s => s.Board == null);

            if (free == null)
            {
                Debug.LogError($"  ✗ 빈 자리가 없습니다: {GetPath(board.transform)}", board);
                return false;
            }

            free.Board = board;
            Debug.LogWarning(
                $"  ! anchor 로 짝을 찾지 못해 남는 자리에 넣습니다: " +
                $"{GetPath(board.transform)} → {free.AnchorName}", board);
        }

        return true;
    }

    private static bool CreateInstance(Scene scene, GameObject prefab, Slot slot)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

        if (instance == null)
        {
            Debug.LogError($"프리팹을 놓지 못했습니다: {PrefabPath}");
            return false;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Mission 2 Guide Board");

        slot.Board = instance.GetComponent<MissionGuideBoard>();
        slot.Created = true;

        if (slot.Board == null)
        {
            Debug.LogError($"놓은 오브젝트에 MissionGuideBoard 가 없습니다: {instance.name}");
            return false;
        }

        Debug.Log($"  새로 놓았습니다: {slot.InstanceName} ({slot.AnchorName})", instance);
        return true;
    }

    // ───────────────────────────── 배치 ─────────────────────────────

    private static void PlaceAndWire(Slot slot, Vector3 center)
    {
        MissionGuideBoard board = slot.Board;
        Transform boardTransform = board.transform;
        Vector3 anchorPosition = slot.Anchor.position;

        Undo.RecordObject(boardTransform, "Place Mission 2 Guide Board");

        if (slot.Created)
        {
            // 연못 바깥쪽 = 연꽃 세 송이의 중심에서 멀어지는 방향.
            Vector3 direction = anchorPosition - center;
            direction.y = 0f;

            direction = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector3.forward;

            Vector3 flat = anchorPosition + direction * PlacementDistance;

            float ground = ProbeGround(flat, anchorPosition.y, out string groundSource);
            var position = new Vector3(flat.x, ground + BoardCenterHeight, flat.z);

            // 빌보드와 같은 식으로 초기 회전을 잡는다. 연꽃 쪽에 선 사람을 향한다.
            Vector3 fromAnchor = position - anchorPosition;
            fromAnchor.y = 0f;

            boardTransform.position = position;
            boardTransform.rotation = fromAnchor.sqrMagnitude > 0.000001f
                ? Quaternion.LookRotation(fromAnchor, Vector3.up)
                : Quaternion.identity;
            boardTransform.localScale = Vector3.one * BoardScale;

            Debug.Log(
                $"  [{slot.InstanceName}] 새 배치\n" +
                $"    기준      : {slot.AnchorName} {Format(anchorPosition)}\n" +
                $"    바깥 방향 : {Format(direction)}  거리 {PlacementDistance} m\n" +
                $"    지면      : {ground:F3}  ({groundSource})\n" +
                $"    → 위치    : {Format(position)}  회전 Y {boardTransform.eulerAngles.y:F1}°",
                board);
        }
        else
        {
            Debug.Log(
                $"  [{slot.InstanceName}] 기존 Transform 유지 — " +
                $"위치 {Format(boardTransform.position)} / " +
                $"회전 Y {boardTransform.eulerAngles.y:F1}° / scale {boardTransform.localScale.x}",
                board);
        }

        // 프리팹 높이가 바뀌었으므로 sizeDelta 는 인스턴스에도 맞춰 준다.
        // 위치·회전·크기는 건드리지 않는다.
        if (boardTransform is RectTransform rect && rect.sizeDelta != BoardSize)
        {
            Debug.Log($"    sizeDelta {rect.sizeDelta} → {BoardSize}", board);
            rect.sizeDelta = BoardSize;
        }

        if (board.gameObject.name != slot.InstanceName)
        {
            Undo.RecordObject(board.gameObject, "Rename Mission 2 Guide Board");
            Debug.Log($"    이름 {board.gameObject.name} → {slot.InstanceName}", board);
            board.gameObject.name = slot.InstanceName;
        }

        var so = new SerializedObject(board);
        SetObject(so, "anchor", slot.Anchor);
        ApplyDisplaySettings(so);
        so.ApplyModifiedProperties();

        // Scene 뷰에서 결과를 볼 수 있게 알파를 올려 둔다.
        // 실행하면 Awake 가 0으로 되돌리고 조건에 맞을 때 페이드인한다.
        var group = board.GetComponent<CanvasGroup>();

        if (group != null && !Mathf.Approximately(group.alpha, 1f))
        {
            Undo.RecordObject(group, "Preview Mission 2 Guide Board");
            group.alpha = 1f;
            EditorUtility.SetDirty(group);
        }

        EditorUtility.SetDirty(board);
        EditorUtility.SetDirty(boardTransform);
    }

    /// <summary>
    /// 지면 높이. 기준점보다 한참 위에 있는 적중은 "머리 위 구조물"로 보고 건너뛴다.
    /// </summary>
    private static float ProbeGround(Vector3 flat, float anchorY, out string source)
    {
        var origin = new Vector3(flat.x, anchorY + GroundProbeUp, flat.z);

        RaycastHit[] hits = Physics.RaycastAll(
            origin, Vector3.down, GroundProbeLength, ~0, QueryTriggerInteraction.Ignore);

        float ceiling = anchorY + MaxGroundAbove;

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
        return anchorY + GroundFallbackOffset;
    }

    // ───────────────────────────── 검증 ─────────────────────────────

    private static void Validate()
    {
        Debug.Log("===== 미션 2 가이드 보드 [VALIDATE] =====");

        if (!TryGetScene(out Scene scene))
            return;

        int problems = 0;

        // ── 미션 1 안내판 보존 확인 (읽기만) ────────────────────
        List<MissionGuideBoard> mission1 = FindBoards(scene, Mission1PrefabPath);

        problems += Expect(mission1.Count == 1,
            $"미션 1 안내판 1개 보존 (현재 {mission1.Count}개)", null);

        if (mission1.Count == 1)
        {
            Transform t = mission1[0].transform;
            Debug.Log(
                $"  · 미션 1 안내판 그대로 — {GetPath(t)} " +
                $"위치 {Format(t.position)} scale {t.localScale.x}  (이 도구는 건드리지 않음)",
                mission1[0]);
        }

        // ── 미션 2 안내판 세 개 ─────────────────────────────────
        List<MissionGuideBoard> mine = FindBoards(scene, PrefabPath);

        problems += Expect(mine.Count == AnchorNames.Length,
            $"미션 2 안내판 {AnchorNames.Length}개 (현재 {mine.Count}개)", null);

        var usedAnchors = new List<Transform>();

        foreach (MissionGuideBoard board in mine)
            problems += ValidateBoard(board, usedAnchors);

        // 기준 연꽃이 겹치면 두 안내판이 같은 자리에서 함께 뜬다.
        problems += Expect(usedAnchors.Count == mine.Count,
            $"기준 연꽃이 서로 다름 ({usedAnchors.Count}/{mine.Count})", null);

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음. (실행 중 동작은 Play로 확인하세요)"
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    private static int ValidateBoard(MissionGuideBoard board, List<Transform> usedAnchors)
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

        // 기준 연꽃
        SerializedProperty anchorProperty = so.FindProperty("anchor");
        var anchorTransform = anchorProperty != null
            ? anchorProperty.objectReferenceValue as Transform
            : null;

        if (anchorTransform == null)
        {
            Debug.LogError("  ✗ anchor 참조가 비어 있습니다.", go);
            problems++;
        }
        else
        {
            bool known = System.Array.IndexOf(AnchorNames, anchorTransform.name) >= 0;
            problems += Expect(known, $"anchor → {anchorTransform.name}", go);

            if (!usedAnchors.Contains(anchorTransform))
                usedAnchors.Add(anchorTransform);
            else
                Debug.LogError($"  ✗ anchor 가 다른 안내판과 겹칩니다: {anchorTransform.name}", go);
        }

        problems += ExpectRef(so, "canvasGroup");
        problems += ExpectRef(so, "kickerText");
        problems += ExpectRef(so, "titleText");
        problems += ExpectRef(so, "footerText");
        problems += ExpectRef(so, "participationText");
        problems += ExpectRef(so, "lumenPortrait");
        problems += ExpectRef(so, "echoPortrait");
        problems += ExpectRef(so, "lumenNameText");
        problems += ExpectRef(so, "echoNameText");

        problems += ExpectEnum(so, "characterDisplay",
            (int)MissionGuideBoard.CharacterDisplay.Both, "Both");
        problems += ExpectEnum(so, "visibleStep",
            (int)TutorialStep_2.WaterComplete, "WaterComplete");
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

        // 깊이 프라임
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
        if (anchorTransform != null)
        {
            Vector3 delta = board.transform.position - anchorTransform.position;
            float heightDelta = delta.y;
            delta.y = 0f;

            var rect = board.transform as RectTransform;
            Vector2 world = rect != null ? rect.sizeDelta * board.transform.localScale.x : Vector2.zero;

            Debug.Log(
                $"  · 위치 {Format(board.transform.position)} / 월드 {world.x:F2} × {world.y:F2} m\n" +
                $"  · 기준까지 수평 {delta.magnitude:F2} m / 높이차 {heightDelta:F2} m", go);

            problems += Expect(delta.magnitude < ShowDistance,
                $"showDistance({ShowDistance} m) 안 (현재 {delta.magnitude:F2} m)", go);
            problems += Expect(heightDelta > 0.3f && heightDelta < 2.8f,
                $"기준점 대비 높이 0.3~2.8 m (현재 {heightDelta:F2} m)", go);
            problems += Expect(Mathf.Approximately(board.transform.localScale.x, BoardScale),
                $"Scale {BoardScale} (현재 {board.transform.localScale.x})", go);
        }

        return problems;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan(List<Slot> slots, Vector3 center)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"  프리팹        : {PrefabPath}  (문구·높이 갱신)");
        text.AppendLine($"  보드 크기     : {BoardWidth} × {BoardHeight} @ {BoardScale} " +
                        $"→ 월드 {BoardWidth * BoardScale:F2} × {BoardHeight * BoardScale:F2} m");
        text.AppendLine($"  STEP 칸 수    : {StepCount}  (본문 영역 {StepBodyHeight}px = 27pt 세 줄)");
        text.AppendLine("  표시 단계     : WaterComplete (= 미션 2 진행 중)");
        text.AppendLine($"  거리          : {Mode} / show {ShowDistance} / hyst {HideHysteresis}");
        text.AppendLine($"  연꽃 중심     : {Format(center)}");
        text.AppendLine($"  안내판        : {slots.Count}개");

        foreach (Slot slot in slots)
        {
            Vector3 direction = slot.Anchor.position - center;
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.forward;

            Vector3 flat = slot.Anchor.position + direction * PlacementDistance;

            text.AppendLine(
                $"    {slot.InstanceName}  기준 {slot.AnchorName} {Format(slot.Anchor.position)}");
            text.AppendLine(
                $"      예상 XZ ({flat.x:F3}, ?, {flat.z:F3})  (Y는 지면 레이캐스트, " +
                "이미 놓인 안내판은 기존 Transform 유지)");
        }

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

    private static bool TryFind(Scene scene, string name, out Transform found)
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
            Debug.LogError($"'{name}' 오브젝트를 찾지 못했습니다. 이름이 바뀌었는지 확인하세요.");
            return false;
        }

        if (matches.Count > 1)
            Debug.LogWarning($"'{name}' 이름의 오브젝트가 {matches.Count}개입니다. 첫 번째를 씁니다.");

        found = matches[0];
        return true;
    }

    /// <summary>지정한 프리팹에서 나온 안내판 인스턴스만 모은다. 미션 1/2를 섞지 않기 위함이다.</summary>
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
