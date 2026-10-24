using System.Collections.Generic;
using ReBloom.Water;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Stage3 MISSION 2(물결 타이밍) 미션 힌트 안내판을 만들고 발판 구역 입구에 놓는 도구.
///
/// Stage3 미션 1 안내판(<see cref="Stage3Mission1GuideBoardSetup"/>)과 같은
/// <see cref="MissionGuideBoard"/> 컴포넌트·같은 치수·같은 공용 스프라이트를 쓴다.
/// 단계 판정만 <see cref="Stage3MissionStepSource"/> 로 넘기고, 그 값만
/// <see cref="TutorialStep_3.PanelComplete"/> 로 다르다. 미션 1 프리팹과 그 씬 배치,
/// Stage2 쪽 파일은 읽지도 쓰지도 않는다.
///
/// <b>왜 안내판 둘인가</b> —
/// 미션 2는 강을 사이에 두고 두 사람이 <b>서로 멀리</b> 떨어져 수행한다. 두 발판이
/// 18.5 m 떨어져 있어, 한 장만 두면 건너편 사람은 <see cref="ShowDistance"/>(6 m)
/// 밖이라 읽을 수 없다. 그래서 발판마다 한 장씩, 같은 프리팹으로 둔다.
/// 두 발판에는 역할이 고정되어 있지 않으므로(<c>IsNearAnyStand</c> 는 "아무 발판이나"를
/// 본다) <b>두 보드의 문구는 완전히 같다</b> — 누가 어느 쪽에 서도 같은 안내를 읽는다.
///
/// <b>배치 근거 (씬에서 읽은 값만 씀)</b> —
///   · 발판   : <c>RiverbedMissionNet.standPoints</c> 두 개. 시작 판정이 쓰는 그 목록이다.
///              (비어 있으면 <c>RiverbedFlowController.requiredStations</c> 로 대체)
///   · 들어오는 쪽 : <c>WaterfrontSpawnPoint</c>. <c>ZoneTeleporter</c> 가 두 사람을
///              이 지점으로 보내므로, Waterfront 구역의 실제 진입점이다.
///              여기서 더 가까운 발판이 Near, 건너편이 Far 다.
///   · 방향   : 각 발판에서 <b>강 반대쪽(육지)</b> 으로. 발판이 둘뿐이므로
///              "상대 발판에서 멀어지는 방향"이 곧 육지 쪽이다
///              (<see cref="LandDirection"/>). 강 쪽으로 밀려날 일이 없다.
///   · 위치   : 그 방향으로 <see cref="PlacementDistance"/> m. 발판 위도,
///              두 발판 사이(강 안)도 아니다.
///   · 회전   : 발판을 향하게 세운다 — 발판에 선 사람이 읽을 방향이다.
///              (빌보드가 켜져 있어 실행 중에는 카메라를 따라 돈다)
///   · 높이   : 지면 레이캐스트 + <see cref="BoardCenterHeight"/>.
/// 좌표를 적어 넣지 않는다 — 전부 씬 오브젝트에서 계산하므로 발판을 옮기면 따라온다.
/// <see cref="Validate"/> 가 두 보드 각각에 대해 StoolMissionPanel 두 개와의 거리와
/// "발판 → 패널 시선을 가로막는지"를 실제 Transform 으로 재서 보고한다.
///
/// <b>두 보드를 가려내는 법</b> —
/// 둘은 <b>같은 프리팹</b>에서 나오므로 이름이나 프리팹 경로로는 구분할 수 없다.
/// 그래서 각 인스턴스가 <b>어느 발판에 가장 가까운가</b>로 짝을 정한다.
/// 이름(<c>_Near</c> / <c>_Far</c>)은 사람이 보기 위한 표시일 뿐이라,
/// 이름을 바꿔 두어도 재실행 시 짝이 어긋나지 않는다.
///
/// <b>거리 기준점</b> —
/// <c>anchor</c> 를 비워 둔다. 보드는 <c>anchor</c> 가 비면 자기 위치에서 거리를 재는데,
/// 입구 안내판은 "보드 앞에 섰을 때" 떠야 하기 때문이다. (미션 1 안내판과 같은 방식)
///
/// 이미 놓여 있는 안내판은 다시 만들지 않고, 손으로 맞춘 위치·회전·크기도 건드리지 않는다.
/// 씬은 자동 저장하지 않는다(Ctrl+Z 로 되돌릴 수 있도록).
/// </summary>
public static class Stage3Mission2GuideBoardSetup
{
    private const string SceneName = "Stage3";
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/MissionGuideBoard_Stage3_Mission2.prefab";
    private const string Mission1PrefabPath = PrefabFolder + "/MissionGuideBoard_Stage3_Mission1.prefab";
    private const string InstanceName = "MissionGuideBoard_Stage3_Mission2";

    // 안내판 둘을 Hierarchy 에서 구분하기 위한 이름.
    // Near = Waterfront 진입점에서 더 가까운 발판 쪽, Far = 건너편 발판 쪽.
    // 이름은 사람이 보기 위한 것이고, 도구가 어느 보드인지 가려낼 때는
    // "어느 발판에 가장 가까운가"로 판정한다(이름을 바꿔도 안전하게).
    private const string NearSuffix = "_Near";
    private const string FarSuffix = "_Far";

    /// <summary>발판은 정확히 둘이어야 한다. 다르면 추측하지 않고 멈춘다.</summary>
    private const int RequiredStoolCount = 2;

    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    private const string LumenPortraitPath = "Assets/ReBloom/UI/ReBloom_Lumen_Portrait.png";
    private const string EchoPortraitPath = "Assets/ReBloom/UI/ReBloom_Echo_Portrait.png";

    /// <summary>플레이어가 Waterfront 로 들어오는 지점. 앞에 있는 것을 먼저 쓴다.</summary>
    private static readonly string[] ApproachFromNames =
    {
        "WaterfrontSpawnPoint", "SpawnPoint",
    };

    /// <summary>진행 상황 패널. 이름으로 찾는 건 대체 경로고, 먼저 컴포넌트에서 읽는다.</summary>
    private const string PanelNamePrefix = "StoolMissionPanel";

    // ── 레이아웃 (Stage3 미션 1 / Stage2 미션 1~4와 동일) ─────────────────
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

    // 카드 높이는 본문 줄 수로 정한다. 미션 1 안내판과 똑같은 식이다 —
    //   두 줄 + 여유 한 줄 = 3 × 41.3333 = 124,  카드 84 + 124 + 16 = 224
    // 그래서 짧은 카드는 Stage2·Stage3 미션 1 카드와 픽셀 단위로 같고,
    // 세 줄짜리 카드(02·03·04)만 한 칸 늘어난다.
    private const float StepBodyLineHeight = 124f / 3f;   // 27pt 한 줄 + lineSpacing 4
    private const float StepBodySlackLines = 1f;          // 한 줄이 접혔을 때의 여유
    private const float StepBodyMinHeight = 124f;         // 짧은 카드도 이 높이는 유지
    private const float StepCardBottomPad = 16f;          // 본문 아래 안쪽 여백

    private const float BottomPad = 34f;

    // ── 색 (미션 1과 동일) ────────────────────────────────────────────────
    private static readonly Color AccentDefault = new Color(0.30980393f, 0.76862746f, 0.84705883f, 1f);
    private static readonly Color PanelColor = new Color(0.02745098f, 0.05490196f, 0.08627451f, 0.96f);
    private static readonly Color CardColor = new Color(0.07450981f, 0.1254902f, 0.17254902f, 0.98f);
    private static readonly Color PortraitBackColor = new Color(0.13725491f, 0.21176471f, 0.27450982f, 1f);
    private static readonly Color TitleColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color BodyColor = new Color(0.9019608f, 0.94509804f, 0.96862745f, 1f);
    private static readonly Color LumenColor = new Color(0.78431374f, 0.57254905f, 1f, 1f);
    private static readonly Color EchoColor = new Color(0.42352942f, 0.7764706f, 1f, 1f);

    private const float OutlineAlpha = 0.55f;
    private const float PanelGlowAlpha = 0.40f;

    // ── 배치 ──────────────────────────────────────────────────────────────
    //
    // 발판에서 육지 쪽으로 이만큼 물러난다. 발판 위(올라서는 면)와
    // 발판 옆 StoolMissionPanel(발판에서 약 1.6 m)을 둘 다 비켜 가면서,
    // 발판에 선 사람이 showDistance 안에 머무르는 거리다.
    private const float PlacementDistance = 4f;

    // 미션 1 안내판은 1.25 m 였다. 카드가 한 장 더 많아 보드가 0.16 m 더 높으므로
    // 그대로 두면 마지막 카드가 무릎 높이로 내려간다. 중심을 올려 위아래를
    // 시선 범위 안에 둔다. Inspector 에서 조정할 수 있다.
    private const float BoardCenterHeight = 1.45f;

    private const float MaxGroundAbove = 1f;
    private const float GroundFallbackOffset = -0.4f;
    private const float GroundProbeUp = 8f;
    private const float GroundProbeLength = 24f;

    /// <summary>StoolMissionPanel 과 이만큼은 떨어져 있어야 서로 안 가린다고 본다.</summary>
    private const float RequiredPanelClearance = 1.5f;

    /// <summary>발판 → 패널 시선에서 보드가 이만큼 비켜 있어야 한다 (보드 반폭 0.5 m + 여유).</summary>
    private const float RequiredSightlineClearance = 1.2f;

    /// <summary>발판 올라서는 면과 이만큼은 떨어져 있어야 동선을 막지 않는다고 본다.</summary>
    private const float RequiredStoolClearance = 1.5f;

    // ── 표시 조건 ─────────────────────────────────────────────────────────
    //
    // TutorialMissionManager_3 는 단계를 순서대로만 넘기고 한 번에 하나만 들고 있다.
    //   Initial → PanelComplete → (RiverbedMissionNet 클리어) → RiverbedComplete → …
    // 그래서 "PanelComplete 부터 RiverbedComplete 직전까지" 는 곧
    // "CurrentTutorial == PanelComplete" 이고, 미션 2가 끝나면 조건이 깨져
    // 안내판이 스스로 페이드아웃한다.
    private const TutorialStep_3 VisibleStep = TutorialStep_3.PanelComplete;

    // 보드 본체의 Stage2 전용 필드. stepSource 가 연결되면 읽지 않는다.
    private const TutorialStep_2 UnusedStage2Step = TutorialStep_2.Initial;

    private const MissionGuideBoard.DistanceMode Mode = MissionGuideBoard.DistanceMode.Horizontal;

    private const float ShowDistance = 6f;
    private const float HideHysteresis = 0.5f;
    private const float FadeDuration = 0.3f;

    private const MissionGuideBoard.CharacterDisplay Display =
        MissionGuideBoard.CharacterDisplay.Both;

    // 힌트 중심 개편 —
    // "발판에 한 명씩", "총 3번"은 손목 UI와 진행 패널(물방울 3개)이 이미 알려 주므로 빼고,
    // 안내가 없으면 막히는 것만 남겼다:
    //   ① 두 사람이 모두 맞혀야 1회로 인정된다 (혼자 맞혀도 집계되지 않는다)
    //   ② 눈에 보이는 물결이 전부 진짜는 아니다
    //   ③ 둘이 느끼는 신호가 달라 서로 비교해야 한다
    // "가짜는 진동이 없다"는 정답은 공개하지 않는다. 발판도 역할별로 고정되어 있지 않아
    // 어느 쪽에 누가 서는지 지정하지 않는다.
    private const string Kicker = "MISSION 02 · 미션 힌트";
    private const string Title = "물결이 오는 타이밍을 맞춰라";

    // 문구는 전부 실제 코드에서 확인한 동작이다.
    //
    //  01 — RiverbedMissionNet.BothPlayersStanding() : 역할별로 두 사람을 찾아 각각
    //       IsNearAnyStand 로 본다. "아무" 발판이나 하나면 통과하므로 어느 발판에
    //       누가 서는지는 정해져 있지 않다 (:158-185). 그래서 문구도 발판을 지정하지 않는다.
    //  02 — RiverbedRoleVisibility.hideForRole = Role.ear → 물결 패턴은 루멘에게만 보인다.
    //       RiverbedEarHaptics.onlyForRole = Role.ear → 진동은 에코에게만 온다 (:21).
    //       가짜 물결은 fakeHapticScale = 0 이라 진동이 아예 없고, 시각은 진짜와 동일하다
    //       (RiverbedFlowController 주석: "isReal 은 절대 셰이더로 넘기지 않는다").
    //  03 — RiverbedTriggerInput : useLeftHand·useRightHand 둘 다 1, onlyForRole = none.
    //       양손 어느 트리거든 되고 두 역할 다 누를 수 있다 (:21-22, :63-64).
    //       호스트는 mentalHit && earHit 일 때만 성공으로 센다
    //       (RiverbedMissionNet.HostReceiveHitInternal :211-215).
    //  04 — 판정 규칙이 역할마다 다르다. 루멘(mental)은 비트 주변 좁은 창(IsMentalHit),
    //       에코(ear)는 진동 평탄 구간(hapticIntensity >= earPlateauThreshold 0.95)
    //       (:713-775). 진동이 없으면 가짜이고, 가짜를 누르면 그 회차는 그대로 끝난다
    //       (HostReceiveHitInternal :203-209). rounds[0].fakeChance = 0 이라
    //       가짜는 2회차부터 섞인다.
    //  05 — successCount >= controller.rounds.Count 로 클리어. 씬의 rounds 는 3개다.
    private static readonly string[][] Steps =
    {
        new[]
        {
            "01 · 두 사람의 타이밍",
            "강 양쪽 발판에서 물결에 맞춰 트리거를 눌러보세요.\n" +
            "두 사람 모두 성공해야 해요."
        },
        new[]
        {
            "02 · 모든 물결이 같지는 않아요",
            "눈에 보이는 물결이 모두 진짜는 아니에요.\n" +
            "서로가 느끼는 신호를 비교해 보세요."
        },
        new[]
        {
            "03 · 호흡 맞추기",
            "루멘과 에코가 각자의 감각을 활용해\n" +
            "알맞은 순간을 찾아보세요."
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

    // Footer 띠(체크 + 완료 조건 한 줄)는 만들지 않는다 — 확정 문구에 그 자리에 넣을
    // 줄이 없고, 없는 문구를 지어내지 않는다. 완료 조건은 카드 05가 설명한다.
    // Participation 띠도 같은 이유로 만들지 않는다(역할 분담은 카드 02·04가 설명한다).
    private static float BoardHeight => StepsTop + StepsBlockHeight + BottomPad;

    private static Vector2 BoardSize => new Vector2(BoardWidth, BoardHeight);

    // ───────────────────────────── 메뉴 ─────────────────────────────

    // 400~401 은 Stage3EndingSetup, 410~412 는 미션 1 안내판이 쓴다.
    [MenuItem("Re_Bloom/Stage3/Mission 2 Guide Board - Dry Run", priority = 420)]
    private static void DryRunMenu() => BuildAndPlace(false);

    [MenuItem("Re_Bloom/Stage3/Mission 2 Guide Board - Build and Place", priority = 421)]
    private static void BuildAndPlaceMenu() => BuildAndPlace(true);

    [MenuItem("Re_Bloom/Stage3/Mission 2 Guide Board - Validate", priority = 422)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 본체 ─────────────────────────────

    /// <summary>안내판 한 장 분. 어느 발판을 맡고 어디에 서는지.</summary>
    private sealed class BoardSpec
    {
        public string Label;          // "Near" / "Far" — 로그용
        public string Name;           // Hierarchy 이름
        public Transform Stool;       // 맡은 발판
        public Vector3 LandDirection; // 발판에서 육지 쪽
        public Vector3 Flat;          // 놓을 XZ (Y는 지면 레이캐스트)

        // 런타임에 채운다
        public MissionGuideBoard Board;
        public bool Created;
    }

    private static void BuildAndPlace(bool apply)
    {
        string mode = apply ? "BUILD + PLACE" : "DRY RUN";
        Debug.Log($"===== Stage3 미션 2 가이드 보드 [{mode}] =====");

        if (!TryGetScene(out Scene scene))
            return;

        if (!TryGetStools(scene, out List<Transform> stools))
            return;

        if (!TryGetApproachPoint(scene, out Vector3 approach, out string approachName))
            return;

        List<BoardSpec> specs = BuildSpecs(stools, approach);
        List<Transform> panels = FindPanels(scene);

        ReportPlan(specs, approach, approachName, panels);

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 아무것도 만들거나 바꾸지 않았습니다.\n" +
                "적용하려면 'Mission 2 Guide Board - Build and Place' 를 실행하세요.");
            return;
        }

        GameObject prefab = RebuildPrefab();

        if (prefab == null)
            return;

        if (!TryResolveInstances(scene, prefab, specs))
            return;

        foreach (BoardSpec spec in specs)
            PlaceAndWire(spec);

        EditorSceneManager.MarkSceneDirty(scene);

        if (specs[0].Board != null)
        {
            Selection.activeGameObject = specs[0].Board.gameObject;
            EditorGUIUtility.PingObject(specs[0].Board.gameObject);
        }

        Debug.Log(
            "완료했습니다. 씬은 자동 저장하지 않았습니다 — 확인 후 Ctrl+S 로 저장하세요.\n" +
            "되돌리려면 Ctrl+Z 입니다.");

        Validate();
    }

    /// <summary>
    /// 발판 둘을 Near/Far 로 나누고 각자의 놓을 자리를 계산한다.
    /// 진입점에서 가까운 쪽이 Near 다.
    /// </summary>
    private static List<BoardSpec> BuildSpecs(List<Transform> stools, Vector3 approach)
    {
        Transform near = NearestTo(stools, approach);
        Transform far = stools[0] == near ? stools[1] : stools[0];

        var specs = new List<BoardSpec>
        {
            new BoardSpec { Label = "Near", Name = InstanceName + NearSuffix, Stool = near },
            new BoardSpec { Label = "Far", Name = InstanceName + FarSuffix, Stool = far },
        };

        foreach (BoardSpec spec in specs)
        {
            Transform other = spec.Stool == near ? far : near;
            spec.LandDirection = LandDirection(spec.Stool.position, other.position);
            spec.Flat = spec.Stool.position + spec.LandDirection * PlacementDistance;
        }

        return specs;
    }

    /// <summary>
    /// 발판에서 강 반대쪽(육지)으로 향하는 수평 단위 방향.
    /// </summary>
    /// <remarks>
    /// 발판이 강 양쪽에 하나씩이므로 "상대 발판에서 멀어지는 방향"이 곧 육지 쪽이다.
    /// 진입점 쪽으로 미는 방식은 쓰지 않는다 — 건너편(Far) 발판에서는 진입점이
    /// <b>강을 건너</b> 있어서, 그 방향으로 밀면 보드가 물 위에 놓인다.
    /// </remarks>
    private static Vector3 LandDirection(Vector3 stool, Vector3 otherStool)
    {
        Vector3 away = stool - otherStool;
        away.y = 0f;

        return away.sqrMagnitude > 0.000001f ? away.normalized : Vector3.back;
    }

    /// <summary>
    /// 두 발판. 시작 판정이 쓰는 <c>RiverbedMissionNet.standPoints</c> 를 그대로 읽는다.
    /// </summary>
    /// <remarks>
    /// 이름으로 찾지 않는 이유 — 두 발판은 Stage3_Environment 프리팹 안에 있고
    /// <b>이름이 둘 다 "Stool"</b> 로 같아 이름만으로는 구분할 수 없다.
    /// 비어 있으면 <c>RiverbedFlowController.requiredStations</c> 의 StandZone 들로 대체한다.
    /// </remarks>
    private static bool TryGetStools(Scene scene, out List<Transform> stools)
    {
        stools = new List<Transform>();

        RiverbedMissionNet net = FindInScene<RiverbedMissionNet>(scene);

        if (net != null && net.standPoints != null)
        {
            foreach (Transform point in net.standPoints)
            {
                if (point != null)
                    stools.Add(point);
            }
        }

        if (stools.Count == 0)
        {
            Debug.LogWarning(
                net == null
                    ? "  ! RiverbedMissionNet 을 찾지 못했습니다. requiredStations 로 대체합니다."
                    : "  ! RiverbedMissionNet.standPoints 가 비어 있습니다. " +
                      "requiredStations 로 대체합니다.");

            RiverbedFlowController flow = FindInScene<RiverbedFlowController>(scene);

            if (flow != null && flow.requiredStations != null)
            {
                foreach (StandZone zone in flow.requiredStations)
                {
                    if (zone != null)
                        stools.Add(zone.transform);
                }
            }
        }

        if (stools.Count == 0)
        {
            Debug.LogError(
                "미션 2 발판을 하나도 찾지 못했습니다.\n" +
                "배치 기준을 정할 수 없어 멈춥니다 — 임의 좌표를 쓰지 않습니다.");
            return false;
        }

        if (stools.Count != RequiredStoolCount)
        {
            Debug.LogError(
                $"미션 2 발판이 {stools.Count}개입니다(기대 {RequiredStoolCount}개).\n" +
                "이 도구는 '강 양쪽에 하나씩'을 전제로 육지 방향을 계산합니다. " +
                "발판 수가 다르면 어느 쪽이 육지인지 추측하지 않고 멈춥니다.");

            foreach (Transform stool in stools)
                Debug.LogError($"  · {GetPath(stool)} {Format(stool.position)}", stool);

            return false;
        }

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
            $"Waterfront 진입점({string.Join(" / ", ApproachFromNames)})을 찾지 못했습니다.\n" +
            "들어오는 방향을 정할 수 없어 멈춥니다 — 임의 좌표를 쓰지 않습니다.");
        return false;
    }

    /// <summary>진입점에서 수평으로 가장 가까운 발판. 플레이어가 먼저 올라오는 쪽이다.</summary>
    private static Transform NearestTo(List<Transform> candidates, Vector3 point)
    {
        Transform best = candidates[0];
        float bestDistance = float.MaxValue;

        foreach (Transform candidate in candidates)
        {
            Vector3 delta = candidate.position - point;
            delta.y = 0f;

            if (delta.sqrMagnitude >= bestDistance)
                continue;

            bestDistance = delta.sqrMagnitude;
            best = candidate;
        }

        return best;
    }

    /// <summary>진행 상황 패널 둘. 컴포넌트에서 먼저 읽고, 없으면 이름으로 찾는다.</summary>
    private static List<Transform> FindPanels(Scene scene)
    {
        var panels = new List<Transform>();

        RiverbedMissionPanel display = FindInScene<RiverbedMissionPanel>(scene);

        if (display != null && display.panels != null)
        {
            foreach (RiverbedMissionPanel.PanelRefs refs in display.panels)
            {
                if (refs != null && refs.root != null)
                    panels.Add(refs.root.transform);
            }
        }

        if (panels.Count > 0)
            return panels;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name.StartsWith(PanelNamePrefix))
                    panels.Add(transform);
            }
        }

        if (panels.Count == 0)
            Debug.LogWarning($"  ! {PanelNamePrefix}* 를 찾지 못해 간섭 검사를 건너뜁니다.");

        return panels;
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

        // 공용 스킨. Stage2·Stage3 미션 1 안내판이 쓰는 그 스프라이트·폰트·머티리얼 그대로다.
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
        SetObject(so, "lumenPortrait", refs.LumenPortrait);
        SetObject(so, "echoPortrait", refs.EchoPortrait);
        SetObject(so, "lumenNameText", refs.LumenName);
        SetObject(so, "echoNameText", refs.EchoName);
        SetObject(so, "fogDepthPrime", refs.DepthPrime);
        SetObject(so, "stepSource", stepSource);

        // Footer / Participation 칸을 만들지 않았으므로 참조도 비운다.
        SetObject(so, "footerText", null);
        SetObject(so, "participationText", null);

        // anchor 는 비워 둔다 — 보드 자기 위치에서 거리를 재야 입구 안내판 구실을 한다.
        SetObject(so, "anchor", null);

        SetArray(so, "accentGraphics", refs.Accents);
        SetColor(so, "accentColor", AccentDefault);

        SetString(so, "kicker", Kicker);
        SetString(so, "title", Title);
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

    /// <summary>루멘·에코 두 칸. 미션 2는 둘이 함께 하므로 미션 1과 같은 두 칸짜리다.</summary>
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

    /// <summary>
    /// 두 보드를 각 발판에 짝지운다. 이미 있는 것은 그대로 쓰고 없는 것만 새로 놓는다.
    /// </summary>
    /// <remarks>
    /// 둘은 같은 프리팹에서 나와 이름·프리팹 경로로는 구분할 수 없으므로,
    /// <b>각 인스턴스가 어느 발판에 가장 가까운가</b>로 짝을 정한다.
    /// 그래서 Hierarchy 이름을 바꿔 두었거나 손으로 옮겨 놓았어도 짝이 어긋나지 않는다.
    /// 한쪽 발판에 두 보드가 몰려 있으면 어느 것을 지울지 추측하지 않고 멈춘다.
    /// </remarks>
    private static bool TryResolveInstances(Scene scene, GameObject prefab, List<BoardSpec> specs)
    {
        List<MissionGuideBoard> mine = FindBoards(scene, PrefabPath);

        if (mine.Count > specs.Count)
        {
            Debug.LogError(
                $"씬에 미션 2 안내판이 {mine.Count}개 있습니다(기대 {specs.Count}개). " +
                "어느 것을 남길지 추측하지 않고 멈춥니다.");

            foreach (MissionGuideBoard duplicate in mine)
                Debug.LogError(
                    $"  · {GetPath(duplicate.transform)} {Format(duplicate.transform.position)}",
                    duplicate);

            return false;
        }

        // 1단계 — 각 보드가 "자기에게 가장 가까운 발판"의 자리를 차지한다.
        var claimed = new bool[mine.Count];

        for (int s = 0; s < specs.Count; s++)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int b = 0; b < mine.Count; b++)
            {
                if (claimed[b])
                    continue;

                if (NearestSpecIndex(specs, mine[b].transform.position) != s)
                    continue;

                float distance = FlatDistance(mine[b].transform.position, specs[s].Stool.position);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = b;
            }

            if (best < 0)
                continue;

            claimed[best] = true;
            specs[s].Board = mine[best];
            Debug.Log(
                $"  [{specs[s].Label}] 기존 인스턴스를 씁니다: {GetPath(mine[best].transform)} " +
                $"(발판까지 {bestDistance:F2} m, 중복 생성 없음)",
                mine[best]);
        }

        // 2단계 — 짝을 못 찾은 보드가 남았는데 빈 자리가 있으면 그쪽에 넣는다.
        // (양쪽 보드를 한쪽에 몰아 둔 경우처럼, 가장 가까운 발판이 겹칠 때)
        for (int b = 0; b < mine.Count; b++)
        {
            if (claimed[b])
                continue;

            BoardSpec empty = specs.Find(spec => spec.Board == null);

            if (empty == null)
            {
                Debug.LogError(
                    $"안내판 {GetPath(mine[b].transform)} 을 어느 발판에 묶을지 정할 수 없습니다. " +
                    "두 보드가 같은 발판 쪽에 몰려 있습니다 — 하나를 지우거나 옮긴 뒤 다시 실행하세요.",
                    mine[b]);
                return false;
            }

            claimed[b] = true;
            empty.Board = mine[b];
            Debug.LogWarning(
                $"  ! [{empty.Label}] {GetPath(mine[b].transform)} 을 남은 자리에 묶었습니다 " +
                $"(발판까지 {FlatDistance(mine[b].transform.position, empty.Stool.position):F2} m). " +
                "Transform 은 건드리지 않습니다 — 위치가 어색하면 직접 옮기세요.",
                mine[b]);
        }

        // 3단계 — 빈 자리에 새로 놓는다.
        foreach (BoardSpec spec in specs)
        {
            if (spec.Board != null)
                continue;

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);

            if (instance == null)
            {
                Debug.LogError($"프리팹을 놓지 못했습니다: {PrefabPath}");
                return false;
            }

            Undo.RegisterCreatedObjectUndo(instance, "Create Stage3 Mission 2 Guide Board");

            spec.Board = instance.GetComponent<MissionGuideBoard>();
            spec.Created = true;

            if (spec.Board == null)
            {
                Debug.LogError($"놓은 오브젝트에 MissionGuideBoard 가 없습니다: {instance.name}");
                return false;
            }

            Debug.Log($"  [{spec.Label}] 새로 놓았습니다: {spec.Name}", instance);
        }

        return true;
    }

    /// <summary>이 위치에서 가장 가까운 발판을 맡은 spec 의 번호.</summary>
    private static int NearestSpecIndex(List<BoardSpec> specs, Vector3 position)
    {
        int best = 0;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < specs.Count; i++)
        {
            float distance = FlatDistance(position, specs[i].Stool.position);

            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = i;
        }

        return best;
    }

    private static void PlaceAndWire(BoardSpec spec)
    {
        MissionGuideBoard board = spec.Board;
        Transform boardTransform = board.transform;

        Undo.RecordObject(boardTransform, "Place Stage3 Mission 2 Guide Board");

        if (spec.Created)
        {
            float ground = ProbeGround(spec.Flat, spec.Stool.position.y, out string groundSource);
            var position = new Vector3(spec.Flat.x, ground + BoardCenterHeight, spec.Flat.z);

            boardTransform.position = position;

            // 발판을 향해 세운다 — 발판에 선 사람이 읽을 방향이다. 빌보드가 켜져 있으니
            // 실행 중에는 카메라를 따라 돌고, 이 회전은 Scene 뷰에서 보이는 기본 방향이다.
            boardTransform.rotation = Quaternion.LookRotation(-spec.LandDirection, Vector3.up);
            boardTransform.localScale = Vector3.one * BoardScale;

            Debug.Log(
                $"  [{spec.Label}] 새 배치\n" +
                $"    맡은 발판 : {Format(spec.Stool.position)}  ({GetPath(spec.Stool)})\n" +
                $"    육지 방향 : {Format(spec.LandDirection)} (강 반대쪽)\n" +
                $"    거리      : {PlacementDistance} m\n" +
                $"    지면      : {ground:F3}  ({groundSource})\n" +
                $"    → 위치    : {Format(position)}  회전 Y {boardTransform.eulerAngles.y:F1}° " +
                "(발판을 향함)",
                board);
        }
        else
        {
            Debug.Log(
                $"  [{spec.Label}] 기존 Transform 유지 — " +
                $"위치 {Format(boardTransform.position)} / " +
                $"회전 Y {boardTransform.eulerAngles.y:F1}° / scale {boardTransform.localScale.x}\n" +
                $"    (이 규칙으로 새로 놓는다면 XZ ({spec.Flat.x:F3}, {spec.Flat.z:F3}) 이지만 " +
                "손으로 맞춘 값을 그대로 둡니다)",
                board);
        }

        // 프리팹 높이가 바뀌었을 때만 sizeDelta 를 맞춘다. 위치·회전·크기는 건드리지 않는다.
        if (boardTransform is RectTransform rect && rect.sizeDelta != BoardSize)
        {
            Debug.Log($"    sizeDelta {rect.sizeDelta} → {BoardSize}", board);
            rect.sizeDelta = BoardSize;
        }

        // 이름만 바꾼다. Transform 은 위에서 이미 끝났고 여기서 건드리지 않는다.
        if (board.gameObject.name != spec.Name)
        {
            Undo.RecordObject(board.gameObject, "Rename Stage3 Mission 2 Guide Board");
            Debug.Log($"    이름 {board.gameObject.name} → {spec.Name} (Transform 은 그대로)", board);
            board.gameObject.name = spec.Name;
        }

        // 기존 인스턴스에도 단계 소스가 붙어 있도록 보장한다.
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
            Undo.RecordObject(group, "Preview Stage3 Mission 2 Guide Board");
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
        Debug.Log("===== Stage3 미션 2 가이드 보드 [VALIDATE] =====");

        if (!TryGetScene(out Scene scene))
            return;

        int problems = 0;

        // 미션 1 안내판 보존 확인 (읽기만)
        problems += ReportMission1(scene);

        List<MissionGuideBoard> mine = FindBoards(scene, PrefabPath);

        problems += Expect(mine.Count == RequiredStoolCount,
            $"미션 2 안내판 {RequiredStoolCount}개 (현재 {mine.Count}개)", null);

        if (!TryGetStools(scene, out List<Transform> stools))
            return;

        if (!TryGetApproachPoint(scene, out Vector3 approach, out _))
            return;

        List<BoardSpec> specs = BuildSpecs(stools, approach);
        List<Transform> panels = FindPanels(scene);

        // 보드를 발판에 짝지어 둔다(검증용이므로 새로 놓지는 않는다).
        var claimed = new bool[mine.Count];

        for (int s = 0; s < specs.Count; s++)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int b = 0; b < mine.Count; b++)
            {
                if (claimed[b])
                    continue;

                float distance = FlatDistance(mine[b].transform.position, specs[s].Stool.position);

                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = b;
            }

            if (best < 0)
                continue;

            claimed[best] = true;
            specs[s].Board = mine[best];
        }

        foreach (BoardSpec spec in specs)
        {
            if (spec.Board == null)
            {
                Debug.LogError(
                    $"  ✗ [{spec.Label}] 발판 {Format(spec.Stool.position)} 을 맡은 안내판이 없습니다.",
                    spec.Stool);
                problems++;
                continue;
            }

            Debug.Log($"━━ [{spec.Label}] {GetPath(spec.Board.transform)} ━━", spec.Board);
            problems += ValidateBoard(spec.Board);
            problems += ValidateClearance(spec, specs, panels);
        }

        // 두 보드가 같은 자리에서 동시에 뜨지는 않는지 — 발판 사이가 충분히 먼지 본다.
        problems += CheckBoardsApart(specs);

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음. (실행 중 동작은 Play로 확인하세요)"
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    /// <summary>
    /// 두 안내판이 서로 충분히 떨어져 있는지. 겹쳐 뜨면 같은 문구가 두 장 보인다.
    /// </summary>
    private static int CheckBoardsApart(List<BoardSpec> specs)
    {
        if (specs.Count < 2 || specs[0].Board == null || specs[1].Board == null)
            return 0;

        float distance = FlatDistance(
            specs[0].Board.transform.position, specs[1].Board.transform.position);

        float hideDistance = ShowDistance + HideHysteresis;
        float required = hideDistance * 2f;

        return Expect(distance > required,
            $"두 안내판 사이 {distance:F2} m > {required:F1} m (동시에 뜨지 않음)", specs[0].Board);
    }

    private static int ValidateBoard(MissionGuideBoard board)
    {
        GameObject go = board.gameObject;
        int problems = 0;

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
        problems += ExpectRef(so, "lumenPortrait", null);
        problems += ExpectRef(so, "echoPortrait", null);
        problems += ExpectRef(so, "lumenNameText", null);
        problems += ExpectRef(so, "echoNameText", null);
        problems += ExpectSprite(so, "lumenPortrait");
        problems += ExpectSprite(so, "echoPortrait");

        // 비어 있어야 정상인 참조
        problems += ExpectNull(so, "anchor", "보드 자기 위치에서 거리를 재야 입구 안내판이 된다");
        problems += ExpectNull(so, "footerText", "확정 문구에 완료 안내 줄이 없다 — 카드 05가 설명한다");
        problems += ExpectNull(so, "participationText", "확정 문구에 그 자리에 넣을 줄이 없다");

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

        Vector3 position = board.transform.position;

        Debug.Log(
            $"  · 위치 {Format(position)} / 월드 {world.x:F2} × {world.y:F2} m\n" +
            $"  · 상단 {position.y + world.y * 0.5f:F2} m / 하단 {position.y - world.y * 0.5f:F2} m " +
            "(지면이 아니라 월드 Y 기준)", go);

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

        Debug.Log(
            $"  · 참고: MissionGuideBoard.visibleStep({UnusedStage2Step}) 은 Stage2 전용입니다. " +
            "stepSource 가 연결돼 있으면 읽지 않습니다 — 단계 판정은 위의 " +
            $"Stage3MissionStepSource.visibleStep({VisibleStep}) 이 합니다.", board);

        return problems;
    }

    /// <summary>
    /// 발판·진행 패널과의 간섭. 발판에서 패널을 보는 시선을 가로막지 않는지까지 본다.
    /// </summary>
    /// <summary>
    /// 이 보드가 맡은 발판에서 읽히는지, 그리고 발판·진행 패널을 방해하지 않는지.
    /// </summary>
    private static int ValidateClearance(
        BoardSpec spec, List<BoardSpec> all, List<Transform> panels)
    {
        int problems = 0;

        MissionGuideBoard board = spec.Board;
        Vector3 boardPosition = board.transform.position;

        var boardRect = board.transform as RectTransform;
        float halfWidth = boardRect != null
            ? boardRect.sizeDelta.x * board.transform.localScale.x * 0.5f
            : 0.5f;

        // ── 맡은 발판에서 읽히는가 ───────────────────────────────
        float own = FlatDistance(boardPosition, spec.Stool.position);

        problems += Expect(own <= ShowDistance,
            $"맡은 발판까지 {own:F2} m ≤ showDistance {ShowDistance} m " +
            "(그 발판에 선 사람이 읽을 수 있음)", spec.Stool);

        problems += Expect(own >= RequiredStoolClearance,
            $"맡은 발판과 여유 {own:F2} m ≥ {RequiredStoolClearance} m (올라서기 방해 없음)",
            spec.Stool);

        // ── 건너편 발판과의 관계 (너무 가까우면 양쪽에서 두 장이 뜬다) ──
        foreach (BoardSpec other in all)
        {
            if (other == spec)
                continue;

            float distance = FlatDistance(boardPosition, other.Stool.position);

            Debug.Log(
                $"  · 건너편 발판까지 {distance:F2} m " +
                (distance <= ShowDistance
                    ? "(showDistance 안 — 건너편에서도 이 보드가 뜹니다)"
                    : "(showDistance 밖 — 건너편에서는 그쪽 보드만 뜸)"),
                other.Stool);

            problems += Expect(distance >= RequiredStoolClearance,
                $"건너편 발판과 여유 {distance:F2} m ≥ {RequiredStoolClearance} m", other.Stool);
        }

        // ── 진행 패널과 거리 ────────────────────────────────────
        foreach (Transform panel in panels)
        {
            float distance = FlatDistance(boardPosition, panel.position);

            problems += Expect(distance >= RequiredPanelClearance,
                $"{panel.name} 과 여유 {distance:F2} m ≥ {RequiredPanelClearance} m", panel);
        }

        // ── 발판 → 패널 시선을 가로막는지 ────────────────────────
        float required = halfWidth + RequiredSightlineClearance;

        foreach (BoardSpec stoolOwner in all)
        {
            foreach (Transform panel in panels)
            {
                float offset = DistanceToSegmentXZ(
                    boardPosition, stoolOwner.Stool.position, panel.position);

                problems += Expect(offset >= required,
                    $"시선 '{stoolOwner.Label} 발판 → {panel.name}' 에서 " +
                    $"{offset:F2} m 비켜 있음 (필요 {required:F2} m)", panel);
            }
        }

        return problems;
    }

    /// <summary>점에서 선분까지의 수평 거리. 보드가 시선 위에 있는지 재는 데 쓴다.</summary>
    private static float DistanceToSegmentXZ(Vector3 point, Vector3 a, Vector3 b)
    {
        var p = new Vector2(point.x, point.z);
        var from = new Vector2(a.x, a.z);
        var to = new Vector2(b.x, b.z);

        Vector2 along = to - from;
        float lengthSq = along.sqrMagnitude;

        if (lengthSq <= 0.000001f)
            return Vector2.Distance(p, from);

        float t = Mathf.Clamp01(Vector2.Dot(p - from, along) / lengthSq);
        return Vector2.Distance(p, from + along * t);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 delta = a - b;
        delta.y = 0f;
        return delta.magnitude;
    }

    /// <summary>미션 1 안내판이 그대로 있는지. 이 도구는 건드리지 않는다.</summary>
    private static int ReportMission1(Scene scene)
    {
        List<MissionGuideBoard> boards = FindBoards(scene, Mission1PrefabPath);

        if (boards.Count == 1)
        {
            Transform t = boards[0].transform;
            Debug.Log(
                $"  ✓ 미션 1 안내판 보존 — {GetPath(t)} 위치 {Format(t.position)} " +
                "(이 도구는 건드리지 않음)", boards[0]);
            return 0;
        }

        Debug.LogWarning(
            $"  ! 미션 1 안내판이 {boards.Count}개입니다(기대 1개). " +
            "이 도구가 만든 것이 아니므로 그대로 둡니다.");
        return 0;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan(
        List<BoardSpec> specs, Vector3 approach, string approachName, List<Transform> panels)
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine($"  안내판        : {specs.Count}개 — 발판마다 한 장, 문구는 완전히 동일");
        text.AppendLine($"  표시 단계     : Stage3MissionStepSource / {VisibleStep} (= 미션 2 진행 중)");
        text.AppendLine($"  거리          : {Mode} / show {ShowDistance} / hyst {HideHysteresis}");
        text.AppendLine($"  보드 크기     : {BoardWidth} × {BoardHeight} @ {BoardScale} " +
                        $"→ 월드 {BoardWidth * BoardScale:F2} × {BoardHeight * BoardScale:F2} m");
        text.AppendLine($"  표시 캐릭터   : {Display}  (Footer·Participation 띠 없음)");
        text.AppendLine($"  들어오는 쪽   : {approachName} {Format(approach)}");

        foreach (Transform panel in panels)
            text.AppendLine($"  진행 패널     : {panel.name} {Format(panel.position)}");

        foreach (BoardSpec spec in specs)
        {
            text.AppendLine();
            text.AppendLine($"  ── {spec.Name} ──");
            text.AppendLine($"    맡은 발판 : {Format(spec.Stool.position)}  {GetPath(spec.Stool)}");
            text.AppendLine($"    육지 방향 : {Format(spec.LandDirection)} (강 반대쪽)");
            text.AppendLine($"    예상 XZ   : ({spec.Flat.x:F3}, ?, {spec.Flat.z:F3})  " +
                            "(Y는 지면 레이캐스트, 이미 놓였으면 기존 Transform 유지)");

            foreach (Transform panel in panels)
                text.AppendLine($"    {panel.name} 까지 " +
                                $"{FlatDistance(spec.Flat, panel.position):F2} m");
        }

        text.AppendLine();
        text.AppendLine($"  {Title}");

        for (int i = 0; i < StepCount; i++)
        {
            text.AppendLine(
                $"    {Steps[i][0]}  [카드 {StepCardHeightOf(i):F0} / 본문 {StepBodyHeightOf(i):F0} " +
                $"= {BodyLineCount(i)}줄 + 여유]");
            text.AppendLine($"      {Steps[i][1].Replace("\n", " / ")}");
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
            "Assets/ReBloom/Scenes/Stage3.unity 를 연 뒤 다시 실행하세요.");
        return false;
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T found = root.GetComponentInChildren<T>(true);

            if (found != null)
                return found;
        }

        return null;
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
