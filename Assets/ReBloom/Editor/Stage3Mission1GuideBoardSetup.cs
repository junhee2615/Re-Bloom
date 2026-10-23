using System.Collections.Generic;
using ReBloom.Solar;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage3 MISSION 1(태양광 패널 수리) 조작 가이드 안내판을 만들고 패널 구역 입구에 놓는 도구.
///
/// Stage2 미션 1~4 안내판과 <b>같은</b> <see cref="MissionGuideBoard"/> 컴포넌트·같은 디자인·
/// 같은 공용 스프라이트를 쓴다. 다른 점은 단계 판정뿐이다 —
/// 보드 본체는 Stage2의 <c>TutorialMissionManager_2</c> 를 직접 읽으므로 Stage3에서는 쓸 수 없고,
/// 그래서 <see cref="Stage3MissionStepSource"/> 를 같은 GameObject에 붙여
/// 보드의 <c>stepSource</c> 에 연결한다. 그 하나로 거리 감지·페이드·빌보드·컷씬 숨김은
/// 전부 보드가 그대로 맡는다. Stage2 프리팹과 Stage2 씬은 읽지도 쓰지도 않는다.
///
/// <b>왜 안내판 하나인가</b> —
/// 미션 1은 고장 난 패널 <b>4개</b>를 수리하면 끝나고(<c>SolarMissionManager.requiredPanels</c>),
/// 네 패널의 조작이 모두 같다(올라서서 몸을 앞뒤로 움직여 각도를 맞춘다).
/// 패널마다 같은 설명을 세워 둘 이유가 없어 구역 입구에 한 장만 둔다.
///
/// <b>배치 근거 (씬에서 읽은 값만 씀)</b> —
///   · 기준점 : <c>SolarMissionManager.requiredPanels</c> 네 장의 평균 위치.
///              Stage3_Environment 프리팹 안 Mission1/SolarPanel 아래에 3×3 격자로 서 있고
///              그중 4장이 고장 난 패널이다.
///   · 방향   : 그 기준점에서 플레이어 스폰(<c>HostSpawnPoint</c>)을 향하는 수평 방향.
///              플레이어는 패널 구역 서쪽에서 걸어 들어온다.
///   · 거리   : 기준점에서 그 방향으로 <see cref="PlacementDistance"/> m.
///              격자 바깥이라 패널에 올라서는 동작과 열 사이 통로를 막지 않는다.
///   · 높이   : 그 자리에서 아래로 쏜 지면 레이캐스트 + <see cref="BoardCenterHeight"/>.
/// 좌표를 직접 적어 넣지 않는다 — 전부 씬 오브젝트에서 계산하므로 패널을 옮기면 따라온다.
/// <see cref="Validate"/> 가 "패널 경계와 얼마나 떨어졌는지"를 실제 Collider/Renderer
/// 바운드로 재서 보고하므로, 동선을 막는지는 추측이 아니라 수치로 확인할 수 있다.
///
/// <b>거리 기준점</b> —
/// <c>anchor</c> 를 <b>비워 둔다</b>. 보드는 <c>anchor</c> 가 비면 자기 위치에서 거리를 재는데,
/// 입구 안내판은 "보드 앞에 섰을 때" 떠야 하기 때문이다. 패널을 anchor 로 두면
/// 이미 격자 안으로 들어간 뒤에야 떠서 입구 안내 구실을 못 한다.
///
/// 이미 놓여 있는 안내판은 다시 만들지 않고, 손으로 맞춘 위치·회전·크기도 건드리지 않는다.
/// 씬은 자동 저장하지 않는다(Ctrl+Z 로 되돌릴 수 있도록).
/// </summary>
public static class Stage3Mission1GuideBoardSetup
{
    private const string SceneName = "Stage3";
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/MissionGuideBoard_Stage3_Mission1.prefab";
    private const string InstanceName = "MissionGuideBoard_Stage3_Mission1";
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    private const string LumenPortraitPath = "Assets/ReBloom/UI/ReBloom_Lumen_Portrait.png";
    private const string EchoPortraitPath = "Assets/ReBloom/UI/ReBloom_Echo_Portrait.png";

    /// <summary>플레이어가 패널 구역으로 들어오는 쪽. 앞에 있는 것을 먼저 쓴다.</summary>
    private static readonly string[] ApproachFromNames =
    {
        "HostSpawnPoint", "ClientSpawnPoint", "SpawnPoint",
    };

    // ── 레이아웃 (Stage2 미션 1~4와 동일) ─────────────────────────────────
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

    private const float StepsTop = 442f;
    private const float StepCardGap = 14f;
    private const float StepInnerPad = 28f;
    private const float StepLabelTop = 22f;
    private const float StepLabelHeight = 46f;
    private const float StepLabelFontSize = 36f;
    private const float StepBodyTop = 84f;
    private const float StepBodyFontSize = 27f;
    private const float StepBodyLineSpacing = 4f;

    // 카드 높이는 본문 줄 수로 정한다. Stage2 보드의 두 줄 카드와 똑같은 치수가
    // 그대로 나오도록 상수를 맞춰 뒀다 —
    //   두 줄 + 여유 한 줄 = 3 × 41.3333 = 124 (= Stage2의 StepBodyHeight)
    //   카드 높이 84 + 124 + 16          = 224 (= Stage2의 StepCardHeight)
    // 그래서 01·02·04 카드는 Stage2와 픽셀 단위로 같고, 본문이 긴 03 카드만 늘어난다.
    // 글자 크기·여백·색·모서리는 전부 그대로다.
    private const float StepBodyLineHeight = 124f / 3f;   // 27pt 한 줄 + lineSpacing 4
    private const float StepBodySlackLines = 1f;          // 한 줄이 접혔을 때의 여유
    private const float StepBodyMinHeight = 124f;         // 짧은 카드도 이 높이는 유지
    private const float StepCardBottomPad = 16f;          // 본문 아래 안쪽 여백

    private const float FooterGap = 24f;
    private const float FooterHeight = 50f;
    private const float FooterCheckSize = 34f;
    private const float FooterTextLeft = 50f;
    private const float FooterFontSize = 28f;
    private const float BottomPad = 34f;

    // ── 색 (Stage2 미션 1~4와 동일) ───────────────────────────────────────
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
    // 고장 난 패널 네 장의 평균 위치에서 스폰 쪽으로 이 거리만큼 물러난 자리.
    // 격자 열 간격이 4 m 라 패널 한 장의 반폭은 2 m 를 넘지 않는다.
    // 7 m 면 격자 바깥으로 충분히 나가면서, 서쪽 두 패널은 showDistance 안에 남는다.
    private const float PlacementDistance = 7f;
    private const float BoardCenterHeight = 1.25f;

    private const float MaxGroundAbove = 1f;
    private const float GroundFallbackOffset = -0.4f;
    private const float GroundProbeUp = 8f;
    private const float GroundProbeLength = 24f;

    /// <summary>패널 바운드에서 이만큼은 떨어져 있어야 동선을 막지 않는다고 본다.</summary>
    private const float RequiredClearance = 0.6f;

    // ── 표시 조건 ─────────────────────────────────────────────────────────
    //
    // TutorialMissionManager_3 는 단계를 순서대로만 넘기고 한 번에 하나만 들고 있다.
    //   Initial → (SolarMissionManager.IsClear) → PanelComplete → …
    // 그래서 "Initial 부터 PanelComplete 직전까지" 는 곧 "CurrentTutorial == Initial" 이고,
    // 미션 1이 끝나 PanelComplete 로 넘어가면 조건이 깨져 안내판이 스스로 페이드아웃한다.
    private const TutorialStep_3 VisibleStep = TutorialStep_3.Initial;

    // 보드 본체의 Stage2 전용 필드. stepSource 가 연결되면 읽지 않는다(MissionGuideBoard
    // 의 IsOnTargetStep 이 stepSource 를 먼저 본다). 그래도 기본값으로 둬서 혼동을 줄인다.
    private const TutorialStep_2 UnusedStage2Step = TutorialStep_2.Initial;

    private const MissionGuideBoard.DistanceMode Mode = MissionGuideBoard.DistanceMode.Horizontal;

    private const float ShowDistance = 6f;
    private const float HideHysteresis = 0.5f;
    private const float FadeDuration = 0.3f;

    private const MissionGuideBoard.CharacterDisplay Display =
        MissionGuideBoard.CharacterDisplay.Both;

    private const string Kicker = "MISSION 01 · 조작 가이드";
    private const string Title = "고장 난 태양광 패널을 고쳐라";

    // 미션 목표. 다른 안내판의 Footer 와 같은 자리다 — 체크 표시 + "무엇으로 끝나는가" 한 줄.
    private const string Footer = "고장 난 태양광 패널 4개를 모두 수리하세요.";

    // 문구는 전부 실제 코드에서 확인한 동작이다.
    //
    //  01 — SolarPanelTilt : 발밑으로 레이를 쏴 deck 콜라이더를 맞히면 "올라섰다"로 본다
    //       (:243-244). 버튼 입력은 쓰지 않는다. occupantRole 기본값은 Role.none 이라
    //       루멘·에코 둘 다 올라설 수 있다 (:214).
    //  02 — 발 위치가 판 중심에서 앞뒤로 벗어난 거리를 fullDeflectionDistance(0.4 m)로
    //       정규화해 maxPitch 까지 기울인다 (:127-129, :172). 좌우 위치는 보지 않는다.
    //  03 — SolarBeam.visibleToRole = Role.mental → 빛줄기는 루멘에게만 보인다 (:57, :265-267).
    //       그래서 패널에 올라선 쪽은 자기 각도를 눈으로 확인할 수 없고 루멘이 알려 줘야 한다.
    //       다만 누가 올라서는지는 코드가 제한하지 않는다 — SolarPanelTilt.occupantRole 기본값이
    //       Role.none 이라 둘 다 올라설 수 있다 (:50, :214). 문구가 에코를 지정한 것은
    //       기획상의 역할 분담이고, 에코에게만 오는 진동(SolarBeamHaptics.onlyForRole = Role.ear,
    //       :37, :136-139)이 그 조절을 돕는다는 점에서 코드와 어긋나지 않는다.
    //  04 — SolarPanelRepair.litSecondsToRepair = 1.5 초 (:29). 그리고 빔은
    //       SolarReflector.IsFrontFace 를 통과한 면만 "닿았다"로 센다
    //       (SolarBeam.cs:222, SolarReflector.cs:80-82) — 뒷면·옆면은 벽으로 취급된다.
    private static readonly string[][] Steps =
    {
        new[]
        {
            "01 · 패널 위에 올라서기",
            "고장 난 패널을 수리하기 위해 태양광 패널 위에 올라서세요.\n" +
            "별도의 버튼 조작은 필요하지 않아요."
        },
        new[]
        {
            "02 · 몸을 움직여 각도 조절",
            "패널 위에서 앞뒤로 이동하면 패널의 기울기가 달라집니다."
        },
        new[]
        {
            "03 · 루멘과 에코의 협력",
            "루멘은 자신에게만 보이는 빛줄기를 관찰하고,\n" +
            "에코에게 이동 방향을 알려주세요.\n" +
            "\n" +
            "에코는 패널 위에서 루멘의 안내에 따라\n" +
            "앞뒤로 움직여 빛의 각도를 조절하세요."
        },
        new[]
        {
            "04 · 빛을 유지해 수리 완료",
            "반사된 빛이 고장 난 패널 앞면에 1.5초 동안 닿으면 수리됩니다.\n" +
            "주의! 빛이 패널 뒷면에 닿으면 수리되지 않아요."
        },
    };

    private static int StepCount => Steps.Length;

    /// <summary>본문이 차지하는 줄 수. 문구에 넣은 줄바꿈을 그대로 센다(빈 줄도 한 줄이다).</summary>
    private static int BodyLineCount(int index) => Steps[index][1].Split('\n').Length;

    /// <summary>카드 i 의 본문 영역 높이.</summary>
    private static float StepBodyHeightOf(int index) => Mathf.Max(
        StepBodyMinHeight,
        (BodyLineCount(index) + StepBodySlackLines) * StepBodyLineHeight);

    /// <summary>카드 i 의 전체 높이.</summary>
    private static float StepCardHeightOf(int index)
        => StepBodyTop + StepBodyHeightOf(index) + StepCardBottomPad;

    /// <summary>카드 i 의 윗변 위치(보드 위에서부터).</summary>
    private static float StepTopOf(int index)
    {
        float top = StepsTop;

        for (int i = 0; i < index; i++)
            top += StepCardHeightOf(i) + StepCardGap;

        return top;
    }

    /// <summary>카드 묶음 전체가 차지하는 높이. 카드 사이 간격을 포함하고 끝 간격은 뺀다.</summary>
    private static float StepsBlockHeight
    {
        get
        {
            float total = 0f;

            for (int i = 0; i < StepCount; i++)
                total += StepCardHeightOf(i) + StepCardGap;

            return total - StepCardGap;
        }
    }

    private static float BoardHeight
        => StepsTop + StepsBlockHeight + FooterGap + FooterHeight + BottomPad;

    private static Vector2 BoardSize => new Vector2(BoardWidth, BoardHeight);
    private static float FooterTop => BoardHeight - BottomPad - FooterHeight;

    // ───────────────────────────── 메뉴 ─────────────────────────────

    // 400~401 은 Stage3EndingSetup 이 쓰고 있어 410 블록을 쓴다.
    [MenuItem("Re_Bloom/Stage3/Mission 1 Guide Board - Dry Run", priority = 410)]
    private static void DryRunMenu() => BuildAndPlace(false);

    [MenuItem("Re_Bloom/Stage3/Mission 1 Guide Board - Build and Place", priority = 411)]
    private static void BuildAndPlaceMenu() => BuildAndPlace(true);

    [MenuItem("Re_Bloom/Stage3/Mission 1 Guide Board - Validate", priority = 412)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 본체 ─────────────────────────────

    private static void BuildAndPlace(bool apply)
    {
        string mode = apply ? "BUILD + PLACE" : "DRY RUN";
        Debug.Log($"===== Stage3 미션 1 가이드 보드 [{mode}] =====");

        if (!TryGetScene(out Scene scene))
            return;

        if (!TryGetPanelCenter(scene, out Vector3 panelCenter, out List<Transform> panels))
            return;

        if (!TryGetApproachPoint(scene, out Vector3 approach, out string approachName))
            return;

        Vector3 direction = ApproachDirection(panelCenter, approach);
        Vector3 flat = panelCenter + direction * PlacementDistance;

        ReportPlan(panelCenter, panels, approach, approachName, direction, flat);

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 아무것도 만들거나 바꾸지 않았습니다.\n" +
                "적용하려면 'Mission 1 Guide Board - Build and Place' 를 실행하세요.");
            return;
        }

        GameObject prefab = RebuildPrefab();

        if (prefab == null)
            return;

        if (!TryResolveInstance(scene, prefab, out MissionGuideBoard board, out bool created))
            return;

        PlaceAndWire(board, created, panelCenter, flat, direction);

        EditorSceneManager.MarkSceneDirty(scene);

        Selection.activeGameObject = board.gameObject;
        EditorGUIUtility.PingObject(board.gameObject);

        Debug.Log(
            "완료했습니다. 씬은 자동 저장하지 않았습니다 — 확인 후 Ctrl+S 로 저장하세요.\n" +
            "되돌리려면 Ctrl+Z 입니다.");

        Validate();
    }

    /// <summary>
    /// 고장 난 패널 네 장의 평균 위치. <c>SolarMissionManager.requiredPanels</c> 를 그대로 읽는다.
    /// </summary>
    /// <remarks>
    /// 이름으로 찾지 않는 이유 — 패널은 Stage3_Environment 프리팹 안에 있고
    /// 이름이 <c>BrokenSolarPanel_low (1)</c> 처럼 복제 번호로 갈린다.
    /// 클리어 판정이 쓰는 목록을 그대로 쓰는 쪽이 미션과 어긋나지 않는다.
    /// 매니저의 목록이 비어 있으면(런타임에 자동 수집하는 경로) 씬의
    /// <c>SolarReflector</c> 중 <c>broken</c> 인 것으로 대체한다.
    /// </remarks>
    private static bool TryGetPanelCenter(Scene scene, out Vector3 center, out List<Transform> panels)
    {
        center = Vector3.zero;
        panels = new List<Transform>();

        SolarMissionManager manager = null;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            manager = root.GetComponentInChildren<SolarMissionManager>(true);

            if (manager != null)
                break;
        }

        if (manager != null && manager.requiredPanels != null)
        {
            foreach (SolarReflector panel in manager.requiredPanels)
            {
                if (panel != null)
                    panels.Add(panel.transform);
            }
        }

        if (panels.Count == 0)
        {
            Debug.LogWarning(
                manager == null
                    ? "  ! SolarMissionManager 를 찾지 못했습니다. 고장 난 SolarReflector 로 대체합니다."
                    : "  ! SolarMissionManager.requiredPanels 가 비어 있습니다. " +
                      "고장 난 SolarReflector 로 대체합니다.");

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (SolarReflector panel in root.GetComponentsInChildren<SolarReflector>(true))
                {
                    if (panel.broken)
                        panels.Add(panel.transform);
                }
            }
        }

        if (panels.Count == 0)
        {
            Debug.LogError(
                "고장 난 태양광 패널을 하나도 찾지 못했습니다.\n" +
                "배치 기준을 정할 수 없어 멈춥니다 — 임의 좌표를 쓰지 않습니다.");
            return false;
        }

        Vector3 sum = Vector3.zero;

        foreach (Transform panel in panels)
            sum += panel.position;

        center = sum / panels.Count;
        return true;
    }

    private static bool TryGetApproachPoint(Scene scene, out Vector3 point, out string usedName)
    {
        foreach (string name in ApproachFromNames)
        {
            if (!TryFind(scene, name, out Transform found, false))
                continue;

            point = found.position;
            usedName = name;
            return true;
        }

        point = Vector3.zero;
        usedName = null;

        Debug.LogError(
            $"플레이어 스폰({string.Join(" / ", ApproachFromNames)})을 찾지 못했습니다.\n" +
            "들어오는 방향을 정할 수 없어 멈춥니다 — 임의 좌표를 쓰지 않습니다.");
        return false;
    }

    /// <summary>패널 구역에서 플레이어가 오는 쪽으로 향하는 수평 단위 방향.</summary>
    private static Vector3 ApproachDirection(Vector3 panelCenter, Vector3 approach)
    {
        Vector3 toApproach = approach - panelCenter;
        toApproach.y = 0f;

        return toApproach.sqrMagnitude > 0.000001f ? toApproach.normalized : Vector3.back;
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

        // 공용 스킨. Stage2 미션 1~4 안내판이 쓰는 그 스프라이트·폰트·머티리얼 그대로다.
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
        // GraphicRaycaster / CanvasScaler 를 일부러 넣지 않는다 — XR 레이에 걸리면 안 된다.

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

        BuildCharacterRow(characters, skin, font, refs);

        // Participation 띠(캐릭터 칸 아래 한 줄)는 만들지 않는다 —
        // 확정된 문구에 그 자리에 넣을 줄이 없고, 없는 문구를 지어내지 않는다.
        // 역할 분담은 카드 03이 설명한다. 칸을 비워 두면 MissionGuideBoard 가
        // participationText 를 건너뛴다(ApplyCharacters 의 null 가드).

        // ── Steps ───────────────────────────────────────────────
        RectTransform stepsRoot = CreateChild("Steps", rootRect);
        Stretch(stepsRoot, 0f, 0f, 0f, 0f);

        for (int i = 0; i < StepCount; i++)
        {
            float top = StepTopOf(i);
            string stepName = $"Step{i + 1}";

            RectTransform card = TopBand(stepName, stepsRoot, top, StepCardHeightOf(i), Pad, Pad);
            AddSlicedImage(card, skin.CardFill, CardColor);

            RectTransform labelRect = TopBand(
                stepName + "Label", card, StepLabelTop, StepLabelHeight, StepInnerPad, StepInnerPad);
            TextMeshProUGUI label = AddText(
                labelRect, font, Steps[i][0], StepLabelFontSize,
                AccentDefault, TextAlignmentOptions.TopLeft);
            label.fontStyle = FontStyles.Bold;
            refs.StepLabels.Add(label);
            refs.Accents.Add(label);

            RectTransform bodyRect = TopBand(
                stepName + "Body", card, StepBodyTop, StepBodyHeightOf(i), StepInnerPad, StepInnerPad);
            TextMeshProUGUI body = AddText(
                bodyRect, font, Steps[i][1], StepBodyFontSize,
                BodyColor, TextAlignmentOptions.TopLeft);
            body.lineSpacing = StepBodyLineSpacing;
            refs.StepBodies.Add(body);
        }

        // ── Footer (미션 목표) ──────────────────────────────────
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
        //
        // 단계 판정만 Stage3 쪽으로 넘긴다. 보드 본체는 Stage2용 그대로다.
        var stepSource = root.AddComponent<Stage3MissionStepSource>();
        var sourceObject = new SerializedObject(stepSource);
        SetEnum(sourceObject, "visibleStep", (int)VisibleStep, "Stage3MissionStepSource");
        sourceObject.ApplyModifiedPropertiesWithoutUndo();

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
        SetObject(so, "fogDepthPrime", refs.DepthPrime);
        SetObject(so, "stepSource", stepSource);

        // Participation 칸을 만들지 않았으므로 참조도 비운다.
        SetObject(so, "participationText", null);

        // anchor 는 비워 둔다 — 보드 자기 위치에서 거리를 재야 입구 안내판 구실을 한다.
        SetObject(so, "anchor", null);

        SetArray(so, "accentGraphics", refs.Accents);
        SetColor(so, "accentColor", AccentDefault);

        SetString(so, "kicker", Kicker);
        SetString(so, "title", Title);
        SetString(so, "footer", Footer);
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
            element.FindPropertyRelative("label").stringValue = Steps[i][0];
            element.FindPropertyRelative("body").stringValue = Steps[i][1];
            element.FindPropertyRelative("icon").objectReferenceValue = null;
        }
    }

    private static void ApplyDisplaySettings(SerializedObject so)
    {
        SetEnum(so, "characterDisplay", (int)Display, "MissionGuideBoard");
        SetEnum(so, "visibleStep", (int)UnusedStage2Step, "MissionGuideBoard");
        SetEnum(so, "distanceMode", (int)Mode, "MissionGuideBoard");
        SetBool(so, "hideDuringCutscene", true);
        SetFloat(so, "showDistance", ShowDistance);
        SetFloat(so, "hideHysteresis", HideHysteresis);
        SetFloat(so, "fadeDuration", FadeDuration);
        SetBool(so, "billboard", true);
        SetBool(so, "lockPitch", true);
    }

    /// <summary>루멘·에코 두 칸. 미션 1은 둘이 함께 하므로 Stage2의 두 칸짜리와 같다.</summary>
    private static void BuildCharacterRow(
        RectTransform characters, MissionGuideBoardSetup.Skin skin, TMP_FontAsset font, Refs refs)
    {
        Sprite lumenSprite = AssetDatabase.LoadAssetAtPath<Sprite>(LumenPortraitPath);
        Sprite echoSprite = AssetDatabase.LoadAssetAtPath<Sprite>(EchoPortraitPath);

        float fullWidth = BoardWidth - Pad * 2f;
        float halfWidth = (fullWidth - CharacterCardGap) * 0.5f;

        refs.LumenPortrait = BuildCharacterCard(
            characters, skin, "Lumen", 0f, halfWidth, lumenSprite,
            "루멘", LumenColor, font, out refs.LumenName);

        refs.EchoPortrait = BuildCharacterCard(
            characters, skin, "Echo", halfWidth + CharacterCardGap, halfWidth, echoSprite,
            "에코", EchoColor, font, out refs.EchoName);
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
                $"씬에 {InstanceName} 이 {mine.Count}개 있습니다. " +
                "어느 것을 남길지 추측하지 않고 멈춥니다.");

            foreach (MissionGuideBoard duplicate in mine)
                Debug.LogError($"  · {GetPath(duplicate.transform)}", duplicate);

            return false;
        }

        if (mine.Count == 1)
        {
            board = mine[0];
            Debug.Log(
                $"  기존 인스턴스를 씁니다: {GetPath(board.transform)}  (중복 생성 없음)", board);
            return true;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

        if (instance == null)
        {
            Debug.LogError($"프리팹을 놓지 못했습니다: {PrefabPath}");
            return false;
        }

        Undo.RegisterCreatedObjectUndo(instance, "Create Stage3 Mission 1 Guide Board");

        board = instance.GetComponent<MissionGuideBoard>();
        created = true;

        if (board == null)
        {
            Debug.LogError($"놓은 오브젝트에 MissionGuideBoard 가 없습니다: {instance.name}");
            return false;
        }

        Debug.Log($"  새로 놓았습니다: {InstanceName}", instance);
        return true;
    }

    private static void PlaceAndWire(
        MissionGuideBoard board, bool created,
        Vector3 panelCenter, Vector3 flat, Vector3 direction)
    {
        Transform boardTransform = board.transform;

        Undo.RecordObject(boardTransform, "Place Stage3 Mission 1 Guide Board");

        if (created)
        {
            float ground = ProbeGround(flat, panelCenter.y, out string groundSource);
            var position = new Vector3(flat.x, ground + BoardCenterHeight, flat.z);

            boardTransform.position = position;

            // 다가오는 플레이어를 향해 세운다. 빌보드가 켜져 있으니 실행 중에는
            // 카메라를 따라 돌고, 이 회전은 Scene 뷰에서 보이는 기본 방향이다.
            boardTransform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            boardTransform.localScale = Vector3.one * BoardScale;

            Debug.Log(
                $"  [{InstanceName}] 새 배치\n" +
                $"    패널 중심 : {Format(panelCenter)}\n" +
                $"    방향      : {Format(direction)} (플레이어가 오는 쪽)\n" +
                $"    거리      : {PlacementDistance} m\n" +
                $"    지면      : {ground:F3}  ({groundSource})\n" +
                $"    → 위치    : {Format(position)}  회전 Y {boardTransform.eulerAngles.y:F1}°",
                board);
        }
        else
        {
            Debug.Log(
                $"  [{InstanceName}] 기존 Transform 유지 — " +
                $"위치 {Format(boardTransform.position)} / " +
                $"회전 Y {boardTransform.eulerAngles.y:F1}° / scale {boardTransform.localScale.x}",
                board);
        }

        // 프리팹 높이가 바뀌었을 때만 sizeDelta 를 맞춘다. 위치·회전·크기는 건드리지 않는다.
        if (boardTransform is RectTransform rect && rect.sizeDelta != BoardSize)
        {
            Debug.Log($"    sizeDelta {rect.sizeDelta} → {BoardSize}", board);
            rect.sizeDelta = BoardSize;
        }

        if (board.gameObject.name != InstanceName)
        {
            Undo.RecordObject(board.gameObject, "Rename Stage3 Mission 1 Guide Board");
            board.gameObject.name = InstanceName;
        }

        // 기존 인스턴스에도 단계 소스가 붙어 있도록 보장한다(프리팹을 갱신해도
        // 씬 인스턴스에 컴포넌트가 없을 수 있다 — 예전 버전으로 놓였을 때).
        var stepSource = board.GetComponent<Stage3MissionStepSource>();

        if (stepSource == null)
        {
            stepSource = Undo.AddComponent<Stage3MissionStepSource>(board.gameObject);
            Debug.Log("    Stage3MissionStepSource 를 새로 붙였습니다.", board);
        }

        var sourceObject = new SerializedObject(stepSource);
        SetEnum(sourceObject, "visibleStep", (int)VisibleStep, "Stage3MissionStepSource");
        sourceObject.ApplyModifiedProperties();

        var so = new SerializedObject(board);
        SetObject(so, "stepSource", stepSource);
        SetObject(so, "anchor", null);
        ApplyDisplaySettings(so);
        so.ApplyModifiedProperties();

        // Scene 뷰에서 결과를 볼 수 있게 알파를 올려 둔다.
        // 실행하면 Awake 가 0으로 되돌리고 조건에 맞을 때 페이드인한다.
        var group = board.GetComponent<CanvasGroup>();

        if (group != null && !Mathf.Approximately(group.alpha, 1f))
        {
            Undo.RecordObject(group, "Preview Stage3 Mission 1 Guide Board");
            group.alpha = 1f;
            EditorUtility.SetDirty(group);
        }

        EditorUtility.SetDirty(board);
        EditorUtility.SetDirty(stepSource);
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
        Debug.Log("===== Stage3 미션 1 가이드 보드 [VALIDATE] =====");

        if (!TryGetScene(out Scene scene))
            return;

        int problems = 0;

        List<MissionGuideBoard> mine = FindBoards(scene, PrefabPath);

        problems += Expect(mine.Count == 1, $"{InstanceName} 1개 (현재 {mine.Count}개)", null);

        if (mine.Count != 1)
        {
            Debug.LogError("보드를 찾지 못해 나머지 검사를 건너뜁니다.");
            return;
        }

        problems += ValidateBoard(scene, mine[0]);
        problems += ValidateClearance(scene, mine[0]);
        problems += ReportOtherBoards(scene);

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음. (실행 중 동작은 Play로 확인하세요)"
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    private static int ValidateBoard(Scene scene, MissionGuideBoard board)
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

        problems += ExpectRef(so, "canvasGroup", null);
        problems += ExpectRef(so, "kickerText", null);
        problems += ExpectRef(so, "titleText", null);
        problems += ExpectRef(so, "footerText", null);
        problems += ExpectRef(so, "lumenPortrait", null);
        problems += ExpectRef(so, "echoPortrait", null);
        problems += ExpectRef(so, "lumenNameText", null);
        problems += ExpectRef(so, "echoNameText", null);
        problems += ExpectSprite(so, "lumenPortrait");
        problems += ExpectSprite(so, "echoPortrait");

        // anchor 는 비어 있어야 정상이다 — 보드 자기 위치에서 거리를 재게 한다.
        problems += ExpectNull(so, "anchor", "보드 자기 위치에서 거리를 재야 입구 안내판이 된다");

        // Participation 칸을 만들지 않았으므로 비어 있어야 정상이다.
        problems += ExpectNull(so, "participationText", "확정 문구에 그 자리에 넣을 줄이 없다");

        // ── 단계 판정 ────────────────────────────────────────────
        problems += ValidateStepSource(board, so);

        problems += ExpectEnum(so, "characterDisplay", (int)Display, Display.ToString());
        problems += ExpectEnum(so, "distanceMode", (int)Mode, Mode.ToString());
        problems += ExpectBool(so, "hideDuringCutscene", true);
        problems += ExpectFloat(so, "showDistance", ShowDistance);
        problems += ExpectFloat(so, "hideHysteresis", HideHysteresis);
        problems += ExpectFloat(so, "fadeDuration", FadeDuration);
        problems += ExpectBool(so, "billboard", true);
        problems += ExpectBool(so, "lockPitch", true);

        SerializedProperty steps = so.FindProperty("steps");
        problems += Expect(steps != null && steps.arraySize == StepCount,
            $"steps {StepCount}칸 " +
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

        var boardRect = board.transform as RectTransform;
        Vector2 world = boardRect != null
            ? boardRect.sizeDelta * board.transform.localScale.x
            : Vector2.zero;

        Debug.Log(
            $"  · 위치 {Format(board.transform.position)} / 월드 {world.x:F2} × {world.y:F2} m", go);

        problems += Expect(Mathf.Approximately(board.transform.localScale.x, BoardScale),
            $"Scale {BoardScale} (현재 {board.transform.localScale.x})", go);

        return problems;
    }

    /// <summary>
    /// 단계 판정이 Stage3 쪽으로 넘어갔는지. 이것이 비면 보드는 Stage2 매니저를
    /// 찾다가 못 찾아 영원히 숨은 채로 남는다.
    /// </summary>
    private static int ValidateStepSource(MissionGuideBoard board, SerializedObject so)
    {
        int problems = 0;

        SerializedProperty property = so.FindProperty("stepSource");

        if (property == null)
        {
            Debug.LogError("  ✗ MissionGuideBoard에 'stepSource' 필드가 없습니다.", board);
            return 1;
        }

        var source = property.objectReferenceValue as Stage3MissionStepSource;

        if (source == null)
        {
            Debug.LogError(
                "  ✗ stepSource 가 Stage3MissionStepSource 가 아닙니다. " +
                "비어 있으면 보드가 Stage2 매니저를 찾다가 못 찾아 뜨지 않습니다.", board);
            return 1;
        }

        problems += Expect(source.gameObject == board.gameObject,
            "stepSource 가 같은 GameObject 에 있음", board);

        var sourceObject = new SerializedObject(source);
        SerializedProperty step = sourceObject.FindProperty("visibleStep");

        if (step == null)
        {
            Debug.LogError("  ✗ Stage3MissionStepSource에 'visibleStep' 필드가 없습니다.", source);
            return problems + 1;
        }

        if (step.enumValueIndex != (int)VisibleStep)
        {
            Debug.LogError(
                $"  ✗ Stage3MissionStepSource.visibleStep = {step.enumValueIndex} " +
                $"(기대: {VisibleStep} = {(int)VisibleStep})", source);
            return problems + 1;
        }

        Debug.Log($"  ✓ stepSource → Stage3MissionStepSource / visibleStep = {VisibleStep}", source);

        // Inspector 를 열면 보드 쪽에도 Visible Step(TutorialStep_2) 이 보인다.
        // stepSource 가 연결된 동안에는 읽지 않는 값이라 혼동하지 않도록 적어 둔다.
        SerializedProperty unused = so.FindProperty("visibleStep");

        if (unused != null)
        {
            Debug.Log(
                $"  · 참고: MissionGuideBoard.visibleStep({UnusedStage2Step}) 은 Stage2 전용입니다. " +
                "stepSource 가 연결돼 있으면 읽지 않습니다 — 단계 판정은 위의 " +
                $"Stage3MissionStepSource.visibleStep({VisibleStep}) 이 합니다.", board);
        }

        return problems;
    }

    /// <summary>
    /// 패널 동선을 막지 않는지와, 어느 패널에서 안내판이 보이는지.
    /// 패널 바운드는 실제 Collider(없으면 Renderer)에서 읽는다 — 추측하지 않는다.
    /// </summary>
    private static int ValidateClearance(Scene scene, MissionGuideBoard board)
    {
        int problems = 0;

        Vector3 boardPosition = board.transform.position;
        var flatBoard = new Vector2(boardPosition.x, boardPosition.z);

        var reflectors = new List<SolarReflector>();

        foreach (GameObject root in scene.GetRootGameObjects())
            reflectors.AddRange(root.GetComponentsInChildren<SolarReflector>(true));

        if (reflectors.Count == 0)
        {
            Debug.LogWarning("  ! 씬에서 SolarReflector 를 찾지 못해 여유 거리를 재지 못했습니다.");
            return 0;
        }

        float minClearance = float.MaxValue;
        string closest = null;
        int visibleFrom = 0;
        int brokenCount = 0;

        foreach (SolarReflector reflector in reflectors)
        {
            if (!TryGetFootprint(reflector.gameObject, out Bounds bounds))
                continue;

            // 바운드 바깥까지의 수평 거리. 안에 있으면 0 이다.
            float dx = Mathf.Max(0f, Mathf.Max(bounds.min.x - flatBoard.x, flatBoard.x - bounds.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(bounds.min.z - flatBoard.y, flatBoard.y - bounds.max.z));
            float clearance = Mathf.Sqrt(dx * dx + dz * dz);

            if (clearance < minClearance)
            {
                minClearance = clearance;
                closest = reflector.name;
            }

            if (!reflector.broken)
                continue;

            brokenCount++;

            Vector3 delta = reflector.transform.position - boardPosition;
            delta.y = 0f;
            bool visible = delta.magnitude <= ShowDistance;

            if (visible)
                visibleFrom++;

            Debug.Log(
                $"  · 고장 패널 '{reflector.name}' — 보드까지 수평 {delta.magnitude:F2} m " +
                (visible ? "(showDistance 안: 패널 위에서도 읽을 수 있음)" : "(showDistance 밖)"),
                reflector);
        }

        if (minClearance < float.MaxValue)
        {
            problems += Expect(minClearance >= RequiredClearance,
                $"패널 바운드와 여유 {minClearance:F2} m ≥ {RequiredClearance} m " +
                $"(가장 가까운 패널 '{closest}') — 올라서기·통로를 막지 않음", board);
        }
        else
        {
            Debug.LogWarning("  ! 패널에서 Collider/Renderer 바운드를 읽지 못했습니다.");
        }

        Debug.Log(
            $"  · 고장 패널 {brokenCount}개 중 {visibleFrom}개가 showDistance({ShowDistance} m) 안입니다.\n" +
            "    입구 안내판이므로 다가오는 길에서 읽는 것이 주 용도고, " +
            "격자 안쪽 패널에서는 꺼지는 것이 정상입니다.", board);

        return problems;
    }

    /// <summary>패널이 땅에서 차지하는 범위. 서는 면을 기준으로 하므로 Collider 를 먼저 본다.</summary>
    private static bool TryGetFootprint(GameObject panel, out Bounds bounds)
    {
        bounds = default;
        bool any = false;

        foreach (Collider collider in panel.GetComponentsInChildren<Collider>(true))
        {
            if (collider.isTrigger)
                continue;

            if (!any)
            {
                bounds = collider.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (any)
            return true;

        foreach (Renderer renderer in panel.GetComponentsInChildren<Renderer>(true))
        {
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return any;
    }

    /// <summary>이 도구가 만든 것 말고 다른 MissionGuideBoard 가 Stage3에 있는지.</summary>
    private static int ReportOtherBoards(Scene scene)
    {
        var others = new List<MissionGuideBoard>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MissionGuideBoard found in root.GetComponentsInChildren<MissionGuideBoard>(true))
            {
                GameObject source =
                    PrefabUtility.GetCorrespondingObjectFromOriginalSource(found.gameObject);
                string path = source != null ? AssetDatabase.GetAssetPath(source) : null;

                if (path != PrefabPath)
                    others.Add(found);
            }
        }

        if (others.Count == 0)
        {
            Debug.Log("  ✓ Stage3에 다른 가이드 보드가 섞여 있지 않습니다.");
            return 0;
        }

        foreach (MissionGuideBoard other in others)
        {
            GameObject source =
                PrefabUtility.GetCorrespondingObjectFromOriginalSource(other.gameObject);
            string path = source != null ? AssetDatabase.GetAssetPath(source) : "(프리팹 아님)";

            Debug.LogWarning(
                $"  ! 다른 가이드 보드: {GetPath(other.transform)} ← {path} " +
                "(이 도구는 건드리지 않음)", other);
        }

        return 0;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan(
        Vector3 panelCenter, List<Transform> panels,
        Vector3 approach, string approachName, Vector3 direction, Vector3 flat)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"  표시 단계     : Stage3MissionStepSource / {VisibleStep} (= 미션 1 진행 중)");
        text.AppendLine($"  거리          : {Mode} / show {ShowDistance} / hyst {HideHysteresis}");
        text.AppendLine($"  보드 크기     : {BoardWidth} × {BoardHeight} @ {BoardScale} " +
                        $"→ 월드 {BoardWidth * BoardScale:F2} × {BoardHeight * BoardScale:F2} m");
        text.AppendLine($"  표시 캐릭터   : {Display}");
        text.AppendLine();
        text.AppendLine($"  고장 패널     : {panels.Count}개 — 중심 {Format(panelCenter)}");

        foreach (Transform panel in panels)
            text.AppendLine($"    · {panel.name} {Format(panel.position)}");

        text.AppendLine($"  들어오는 쪽   : {approachName} {Format(approach)}");
        text.AppendLine($"  방향          : {Format(direction)}");
        text.AppendLine($"  예상 XZ       : ({flat.x:F3}, ?, {flat.z:F3})  " +
                        "(Y는 지면 레이캐스트, 이미 놓였으면 기존 Transform 유지)");
        text.AppendLine();
        text.AppendLine($"  {Title}");

        for (int i = 0; i < StepCount; i++)
        {
            text.AppendLine(
                $"    {Steps[i][0]}  [카드 {StepCardHeightOf(i):F0} / 본문 {StepBodyHeightOf(i):F0} " +
                $"= {BodyLineCount(i)}줄 + 여유]");
            text.AppendLine($"      {Steps[i][1].Replace("\n", " / ")}");
        }

        text.AppendLine($"    ✓ {Footer}");

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
            "Assets/ReBloom/Scenes/Stage3.unity 를 연 뒤 다시 실행하세요.");
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

    /// <summary>지정한 프리팹에서 나온 안내판 인스턴스만 모은다.</summary>
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
            Debug.LogWarning($"  ! '{field}' 필드가 없습니다.");
            return;
        }

        p.objectReferenceValue = value;
    }

    private static void SetString(SerializedObject so, string field, string value)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogWarning($"  ! '{field}' 필드가 없습니다.");
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

    private static void SetEnum(SerializedObject so, string field, int value, string owner)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p != null)
            p.enumValueIndex = value;
        else
            Debug.LogWarning($"  ! {owner}에 '{field}' 필드가 없습니다.");
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

    /// <summary>비어 있어야 정상인 참조. 왜 비는지 함께 남긴다.</summary>
    private static int ExpectNull(SerializedObject so, string field, string why)
    {
        SerializedProperty p = so.FindProperty(field);

        if (p == null)
        {
            Debug.LogError($"  ✗ '{field}' 필드가 없습니다.");
            return 1;
        }

        if (p.objectReferenceValue != null)
        {
            Debug.LogError(
                $"  ✗ {field} 가 {p.objectReferenceValue.name} 로 채워져 있습니다 " +
                $"(비어 있어야 합니다 — {why}).");
            return 1;
        }

        Debug.Log($"  ✓ {field} 비어 있음 ({why})");
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
