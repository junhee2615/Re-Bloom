using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stage2 MISSION 1 조작 가이드 보드(<see cref="MissionGuideBoard"/>)의 프리팹을
/// 만들고 다시 꾸미는 셋업 도구.
///
/// 만들어지는 프리팹 —
/// <code>
/// MissionGuideBoard               Canvas(WorldSpace, SO 1) + CanvasGroup(0) + MissionGuideBoard
/// ├── Panel                       둥근 남색 반투명 배경 (9-slice)
/// ├── AccentBar
/// │   └── PanelOutline            둥근 테두리 (9-slice, 강조색)
/// ├── Header
/// │   ├── Kicker                   "MISSION 01 · 조작 가이드"
/// │   ├── Title                    "수로 복구 방법"
/// │   ├── Characters
/// │   │   ├── Lumen → LumenCard / LumenPortrait / LumenName / LumenAccent
/// │   │   └── Echo  → EchoCard  / EchoPortrait  / EchoName  / EchoAccent
/// │   └── Participation            "두 캐릭터 모두 참여할 수 있어요."
/// ├── Steps
/// │   ├── Step1 → (카드 배경) Step1Label / Step1Body
/// │   ├── Step2 → …
/// │   └── Step3 → …
/// └── Footer
///     ├── FooterCheck              체크 배지
///     └── FooterLabel              완료 안내
/// </code>
///
/// 메뉴 —
///  · <b>Create</b>   : 프리팹이 <b>없을 때만</b> 새로 만든다.
///  · <b>Restyle</b>  : <b>이미 있는</b> 프리팹의 디자인만 새로 입힌다.
///    연결된 초상 Sprite와 <see cref="MissionGuideBoard"/> 의 설정값(문구·거리·페이드·
///    표시 모드 등)을 먼저 읽어 두었다가 새 계층에 되돌려 넣는다. 즉 사용자가 Inspector에서
///    맞춘 값은 살아남고 레이아웃만 바뀐다. 실행 전 같은 폴더에 백업 프리팹을 남긴다.
///  · <b>Validate</b> : 구조·참조·XR 안전 설정을 점검한다.
///
/// 왜 에디터 도구인가:
/// <see cref="TextMeshProUGUI"/> 의 직렬화 블록은 한 컴포넌트가 80줄이 넘고 패키지 버전에 따라
/// 필드가 달라진다. 프리팹 YAML을 손으로 쓰면 글자 하나만 어긋나도 컴포넌트가 깨져 Inspector에서
/// 복구가 어렵다. Unity의 프리팹 API로 만들면 현재 설치된 TMP 버전의 직렬화 형식이 그대로 나온다.
/// (<see cref="ItemDescriptionBoardSetup"/> 과 같은 패턴이다)
///
/// 둥근 모서리 스프라이트:
/// 프로젝트에 9-slice 경계가 설정된 UI 스프라이트가 하나도 없어서, 이 도구가 필요한 것을
/// 직접 그려 PNG로 저장한다(<see cref="SpriteFolder"/>). 이미 있으면 다시 만들지 않는다.
/// 전부 흰색이라 <c>Image.color</c> 로 색을 입힌다.
///
/// 안전 규칙 —
///  · GraphicRaycaster를 붙이지 않는다. Collider도 만들지 않는다.
///  · 모든 Graphic의 raycastTarget을 끈다.
///  · CanvasGroup은 alpha 0 / interactable false / blocksRaycasts false.
///  · 씬·미션 스크립트·네트워크 코드는 건드리지 않는다.
/// </summary>
public static class MissionGuideBoardSetup
{
    private const string PrefabFolder = "Assets/ReBloom/Prefabs/UI";
    private const string PrefabPath = PrefabFolder + "/MissionGuideBoard.prefab";
    private const string BackupPath = PrefabFolder + "/MissionGuideBoard_Backup.prefab";
    private const string KoreanFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";

    private const string SpriteFolder = "Assets/ReBloom/UI/Stage2";
    private const string PanelFillPath = SpriteFolder + "/MissionGuideBoard_PanelFill.png";
    private const string PanelOutlinePath = SpriteFolder + "/MissionGuideBoard_PanelOutline.png";
    private const string PanelGlowPath = SpriteFolder + "/MissionGuideBoard_PanelGlow.png";
    private const string CardFillPath = SpriteFolder + "/MissionGuideBoard_CardFill.png";
    private const string CheckPath = SpriteFolder + "/MissionGuideBoard_Check.png";

    // 화면 안개가 보드를 덮지 않도록 깊이만 찍는 쿼드.
    private const string DepthPrimeShaderName = "ReBloom/World UI Depth Prime";
    private const string MaterialFolder = "Assets/ReBloom/Materials";
    private const string DepthPrimeMaterialPath = MaterialFolder + "/M_WorldUiDepthPrime.mat";

    // 첫 생성에만 쓰는 기본 초상. 사용자가 실제 캐릭터 PNG로 바꾼 뒤에는
    // Restyle이 프리팹에 들어 있던 Sprite를 그대로 되돌려 넣으므로 여기로 되돌아가지 않는다.
    private const string DefaultLumenPortraitPath = "Assets/ReBloom/UI/ReBloom_Lumen_Portrait.png";
    private const string DefaultEchoPortraitPath = "Assets/ReBloom/UI/ReBloom_Echo_Portrait.png";

    // ── 레이아웃 (캔버스 단위) ─────────────────────────────────────────────
    // 캔버스 해상도는 그대로 두고 Scale만 1.4배로 키운다(0.0012 → 0.00168).
    // 1000 × 0.00168 = 1.68 m, 1250 × 0.00168 = 2.10 m.
    private static readonly Vector2 BoardSize = new Vector2(1000f, 1250f);
    private const float DefaultBoardScale = 0.00168f;  // 월드 1.68 × 2.10 m

    private const float Pad = 36f;                     // 좌우 안쪽 여백

    private const float PanelCornerRadius = 32f;       // 9-slice 경계와 같은 값
    private const float CardCornerRadius = 16f;
    private const float PanelOutlineStroke = 3f;

    // 테두리 발광. 패널 바깥으로 번지는 폭(캔버스 단위).
    // 포스트 프로세싱 Bloom을 쓰지 않는다 — 프로젝트 전역 설정을 건드리지 않기 위해
    // 스프라이트로 그린 가짜 발광이다. 그래서 눈부심이 생기지 않는다.
    private const float PanelGlowWidth = 26f;
    private const float PanelGlowAlpha = 0.40f;

    // 머리말 / 제목
    private const float KickerTop = 36f;
    private const float KickerHeight = 32f;
    private const float KickerFontSize = 24f;
    private const float KickerSpacing = 10f;

    private const float TitleTop = 74f;
    private const float TitleHeight = 70f;
    private const float TitleFontSize = 54f;

    // 캐릭터 카드
    private const float CharactersTop = 160f;
    private const float CharactersHeight = 220f;
    private const float CharacterCardGap = 18f;
    private const float PortraitTop = 16f;
    private const float PortraitSize = 140f;           // 이전 104 → 140 (면적 +81%)
    private const float CharacterNameTop = 162f;
    private const float CharacterNameHeight = 36f;
    private const float CharacterNameFontSize = 30f;
    private const float RoleBarTop = 206f;
    private const float RoleBarHeight = 4f;
    private const float RoleBarWidth = 120f;

    // 참여 안내
    private const float ParticipationTop = 392f;
    private const float ParticipationHeight = 34f;
    private const float ParticipationFontSize = 25f;

    // STEP 카드
    //
    // 본문 영역 높이는 "가장 긴 칸"에 맞춘다. 02 · 바위 옮기기가 세 줄(\n 고정)이라
    // 27pt × 줄간격 4 기준 약 122px가 필요하다. 124로 잡아 글이 카드를 넘지 않게 한다.
    // (실제 줄 높이는 폰트마다 달라서 Validate가 GetPreferredValues로 한 번 더 잰다)
    private const float StepsTop = 442f;
    private const float StepCardHeight = 224f;
    private const float StepCardGap = 14f;
    private const float StepInnerPad = 28f;
    private const float StepLabelTop = 22f;
    private const float StepLabelHeight = 46f;
    private const float StepLabelFontSize = 36f;
    private const float StepBodyTop = 84f;             // 제목 아래 16px 간격
    private const float StepBodyHeight = 124f;
    private const float StepBodyFontSize = 27f;
    private const float StepBodyLineSpacing = 4f;
    private const int StepCount = 3;

    // 완료 안내
    private const float FooterTop = 1166f;
    private const float FooterHeight = 50f;
    private const float FooterCheckSize = 34f;
    private const float FooterTextLeft = 50f;          // Footer 밴드 왼쪽 기준
    private const float FooterFontSize = 28f;

    // ── 색 ────────────────────────────────────────────────────────────────
    //
    // Stage2는 어둡고 안개가 짙다. 화면 안개는 보드 픽셀을 안개색 쪽으로 lerp 하는데,
    // 그 섞임은 패널과 글자에 똑같이 적용되므로 둘의 "차이"만 줄어든다.
    // 그래서 섞이기 전의 명암차를 최대로 벌려 둔다 — 패널은 더 어둡고 더 불투명하게,
    // 글자는 거의 순백으로. (안개 자체는 깊이 프라임 쿼드로 따로 줄인다)
    private static readonly Color AccentDefault = new Color(0.30980393f, 0.76862746f, 0.84705883f, 1f);
    private static readonly Color PanelColor = new Color(0.02745098f, 0.05490196f, 0.08627451f, 0.96f);
    private static readonly Color CardColor = new Color(0.07450981f, 0.1254902f, 0.17254902f, 0.98f);
    private static readonly Color PortraitBackColor = new Color(0.13725491f, 0.21176471f, 0.27450982f, 1f);
    private static readonly Color TitleColor = new Color(1f, 1f, 1f, 1f);
    private static readonly Color BodyColor = new Color(0.9019608f, 0.94509804f, 0.96862745f, 1f);
    private static readonly Color FooterColor = new Color(0.8392157f, 0.8901961f, 0.92156863f, 1f);

    // 역할색은 Lobby의 MentalBtn(#B16BF3) / EarBtn(#3DB3FF) 계열을 유지하되,
    // 안개 너머에서도 색이 살도록 한 단계 밝게 잡았다.
    // 강조색(accentGraphics)이 덮어쓰면 안 되므로 그 배열에 넣지 않는다.
    private static readonly Color LumenColor = new Color(0.78431374f, 0.57254905f, 1f, 1f);
    private static readonly Color EchoColor = new Color(0.42352942f, 0.7764706f, 1f, 1f);

    private const float OutlineAlpha = 0.55f;

    /// <summary>
    /// Restyle이 옛 컴포넌트에서 새 컴포넌트로 그대로 옮기는 "값" 필드들.
    /// 오브젝트 참조(TMP·Image·배열)는 새 계층의 것으로 다시 연결하므로 여기에 넣지 않는다.
    ///
    /// <c>footer</c> 가 빠져 있는 이유: 하단 문구가 새 기획안에서 바뀌었다. 옛 값을 되살리면
    /// 요청한 문구가 다시 덮여 버리므로, 이 필드만 스크립트의 새 기본값을 쓴다.
    /// </summary>
    private static readonly string[] PreservedValueFields =
    {
        "kicker", "title",
        "characterDisplay", "lumenName", "echoName",
        "participationBoth", "participationLumenOnly", "participationEchoOnly",
        "visibleStep", "hideDuringCutscene",
        "showDistance", "hideHysteresis", "fadeDuration", "disableCanvasWhenHidden",
        "billboard", "lockPitch", "accentColor", "tintTitle", "logVisibility",
    };

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board Prefab - Dry Run", priority = 350)]
    private static void DryRunMenu() => Create(false);

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board Prefab - Create", priority = 351)]
    private static void CreateMenu() => Create(true);

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board Prefab - Restyle (Dry Run)", priority = 353)]
    private static void RestyleDryRunMenu() => Restyle(false);

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board Prefab - Restyle", priority = 354)]
    private static void RestyleMenu() => Restyle(true);

    [MenuItem("Re_Bloom/Stage2/Mission Guide Board Prefab - Validate", priority = 356)]
    private static void ValidateMenu() => Validate();

    // ───────────────────────────── 생성 ─────────────────────────────

    private static void Create(bool apply)
    {
        string mode = apply ? "CREATE" : "DRY RUN";
        Debug.Log($"===== 미션 가이드 보드 프리팹 [{mode}] =====");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            Debug.LogWarning(
                $"이미 존재합니다: {PrefabPath}\n" +
                "연결해 둔 Sprite와 설정을 날리지 않기 위해 새로 만들지 않습니다. " +
                "디자인만 새로 입히려면 'Mission Guide Board Prefab - Restyle' 을 쓰세요.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
        {
            Debug.LogError($"폴더가 없습니다: {PrefabFolder}");
            return;
        }

        ReportPlan();

        if (!apply)
        {
            Debug.Log("DRY RUN이므로 에셋을 만들지 않았습니다. 만들려면 Create를 실행하세요.");
            return;
        }

        var skin = Skin.Load(true);
        var content = BoardContent.Default();

        GameObject root = BuildHierarchy(skin, content, out BuiltRefs refs);

        try
        {
            SaveHierarchy(root, refs.Accents.Count);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        Validate();
    }

    // ───────────────────────────── 리스타일 ─────────────────────────────

    private static void Restyle(bool apply)
    {
        string mode = apply ? "RESTYLE" : "RESTYLE DRY RUN";
        Debug.Log($"===== 미션 가이드 보드 프리팹 [{mode}] =====");

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (asset == null)
        {
            Debug.LogError(
                $"프리팹이 없습니다: {PrefabPath}\n" +
                "먼저 'Mission Guide Board Prefab - Create' 를 실행하세요.");
            return;
        }

        var content = BoardContent.Capture(asset);
        content.Report();

        ReportPlan();

        if (!apply)
        {
            Debug.Log(
                "DRY RUN이므로 프리팹을 바꾸지 않았습니다. 적용하려면 Restyle을 실행하세요.\n" +
                "적용 시 위에 적힌 Sprite와 설정값은 그대로 되돌려 넣고, 레이아웃만 교체합니다.");
            return;
        }

        // 되돌릴 길을 먼저 만든다. 직접 만든 자식이나 수동 조정이 있었다면 여기서 되살릴 수 있다.
        AssetDatabase.DeleteAsset(BackupPath);

        if (AssetDatabase.CopyAsset(PrefabPath, BackupPath))
            Debug.Log($"  백업을 남겼습니다: {BackupPath}  (확인 후 지우셔도 됩니다)");
        else
            Debug.LogWarning($"  ! 백업을 만들지 못했습니다: {BackupPath}");

        var skin = Skin.Load(true);
        GameObject root = BuildHierarchy(skin, content, out BuiltRefs refs);

        try
        {
            SaveHierarchy(root, refs.Accents.Count);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }

        Validate();
    }

    private static void SaveHierarchy(GameObject root, int accentCount)
    {
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);

        if (!success || saved == null)
        {
            Debug.LogError($"프리팹 저장에 실패했습니다: {PrefabPath}");
            return;
        }

        AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"프리팹을 저장했습니다: {PrefabPath}  (강조 Graphic {accentCount}개 연결)");

        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    // ───────────────────────────── 보존 대상 ─────────────────────────────

    /// <summary>
    /// 프리팹에서 그대로 가져가야 하는 것들. 디자인이 아니라 "내용과 설정"이다.
    /// </summary>
    private sealed class BoardContent
    {
        public Sprite LumenPortrait;
        public Sprite EchoPortrait;
        public Color LumenPortraitTint = Color.white;
        public Color EchoPortraitTint = Color.white;

        /// <summary>항상 새 기본값을 쓴다. 옛 값은 보고용으로만 들고 있는다.</summary>
        public float BoardScale => DefaultBoardScale;
        public float PreviousScale = DefaultBoardScale;

        /// <summary>옛 컴포넌트. 값 필드를 그대로 복사해 올 원본이다. 없으면 null.</summary>
        public MissionGuideBoard Source;

        /// <summary>옛 steps의 문구/아이콘. 참조(TMP)는 가져오지 않는다.</summary>
        public readonly List<StepContent> Steps = new List<StepContent>();

        public struct StepContent
        {
            public string Label;
            public string Body;
            public Sprite Icon;
        }

        public static BoardContent Default()
        {
            var content = new BoardContent
            {
                LumenPortrait = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultLumenPortraitPath),
                EchoPortrait = AssetDatabase.LoadAssetAtPath<Sprite>(DefaultEchoPortraitPath),
            };

            return content;
        }

        public static BoardContent Capture(GameObject asset)
        {
            var content = new BoardContent();

            // Scale은 일부러 가져오지 않는다. 이번 개선에서 월드 크기를 1.4배로 키우기로 했고,
            // 옛 값을 되살리면 그 변경이 매번 되돌려지기 때문이다.
            // (씬 인스턴스의 Scale은 배치 도구가 따로 맞춘다)
            var rootRect = asset.transform as RectTransform;

            if (rootRect != null)
                content.PreviousScale = rootRect.localScale.x;

            content.Source = asset.GetComponent<MissionGuideBoard>();

            if (content.Source != null)
            {
                var so = new SerializedObject(content.Source);

                content.LumenPortrait = ReadSprite(so, "lumenPortrait", ref content.LumenPortraitTint);
                content.EchoPortrait = ReadSprite(so, "echoPortrait", ref content.EchoPortraitTint);

                SerializedProperty steps = so.FindProperty("steps");

                if (steps != null)
                {
                    for (int i = 0; i < steps.arraySize; i++)
                    {
                        SerializedProperty element = steps.GetArrayElementAtIndex(i);

                        content.Steps.Add(new StepContent
                        {
                            Label = ReadString(element, "label"),
                            Body = ReadString(element, "body"),
                            Icon = ReadObject(element, "icon") as Sprite,
                        });
                    }
                }
            }

            // 스크립트 참조가 비어 있던 경우를 대비해 계층 이름으로도 한 번 더 찾아본다.
            if (content.LumenPortrait == null)
                content.LumenPortrait = FindSpriteByName(asset, "LumenPortrait");

            if (content.EchoPortrait == null)
                content.EchoPortrait = FindSpriteByName(asset, "EchoPortrait");

            return content;
        }

        private static Sprite ReadSprite(SerializedObject so, string fieldName, ref Color tint)
        {
            SerializedProperty property = so.FindProperty(fieldName);
            var image = property != null ? property.objectReferenceValue as Image : null;

            if (image == null)
                return null;

            tint = image.color;
            return image.sprite;
        }

        private static Sprite FindSpriteByName(GameObject asset, string childName)
        {
            foreach (Image image in asset.GetComponentsInChildren<Image>(true))
            {
                if (image.name == childName)
                    return image.sprite;
            }

            return null;
        }

        private static string ReadString(SerializedProperty element, string name)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null ? property.stringValue : null;
        }

        private static Object ReadObject(SerializedProperty element, string name)
        {
            SerializedProperty property = element.FindPropertyRelative(name);
            return property != null ? property.objectReferenceValue : null;
        }

        public void Report()
        {
            Debug.Log(
                "  보존할 내용 —\n" +
                $"    루멘 초상 : {(LumenPortrait != null ? LumenPortrait.name : "비어 있음")}\n" +
                $"    에코 초상 : {(EchoPortrait != null ? EchoPortrait.name : "비어 있음")}\n" +
                $"    루트 Scale : {PreviousScale} → {DefaultBoardScale} 로 교체 " +
                $"(월드 {BoardSize.x * DefaultBoardScale:F2} × {BoardSize.y * DefaultBoardScale:F2} m)\n" +
                $"    STEP 문구 : {Steps.Count}칸\n" +
                $"    설정 필드 : {PreservedValueFields.Length}개 (문구·거리·페이드·표시 모드 등)\n" +
                "    footer 문구는 새 기획안 문구로 교체합니다.");
        }

        /// <summary>새로 만든 컴포넌트에 보존 값을 되돌려 넣는다.</summary>
        public void ApplyTo(SerializedObject target)
        {
            if (Source == null)
                return;

            var sourceSo = new SerializedObject(Source);

            foreach (string field in PreservedValueFields)
            {
                SerializedProperty property = sourceSo.FindProperty(field);

                if (property == null)
                    continue;

                target.CopyFromSerializedProperty(property);
            }

            SerializedProperty steps = target.FindProperty("steps");

            if (steps == null)
                return;

            int count = Mathf.Min(Steps.Count, steps.arraySize);

            for (int i = 0; i < count; i++)
            {
                SerializedProperty element = steps.GetArrayElementAtIndex(i);
                StepContent step = Steps[i];

                WriteString(element, "label", step.Label);
                WriteString(element, "body", step.Body);

                SerializedProperty icon = element.FindPropertyRelative("icon");

                if (icon != null && step.Icon != null)
                    icon.objectReferenceValue = step.Icon;
            }
        }

        private static void WriteString(SerializedProperty element, string name, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            SerializedProperty property = element.FindPropertyRelative(name);

            if (property != null)
                property.stringValue = value;
        }
    }

    // ───────────────────────────── 스킨 ─────────────────────────────

    private sealed class Skin
    {
        public TMP_FontAsset Font;
        public Sprite PanelFill;
        public Sprite PanelOutline;
        public Sprite PanelGlow;
        public Sprite CardFill;
        public Sprite Check;
        public Material DepthPrime;

        public static Skin Load(bool generateMissing)
        {
            var skin = new Skin
            {
                Font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(KoreanFontPath),
            };

            if (skin.Font == null)
            {
                Debug.LogWarning(
                    $"한글 TMP 폰트를 찾지 못했습니다: {KoreanFontPath}\n" +
                    "TMP 기본 폰트에는 한글 글리프가 없어 문구가 깨집니다.");
            }

            if (!generateMissing)
                return skin;

            // 텍스처 크기는 반지름의 3배로 잡는다. 9-slice 경계(= 반지름) 바깥으로
            // 가운데 슬라이스가 1px 이상 남아야 늘렸을 때 모서리가 뭉개지지 않는다.
            int panelSize = Mathf.RoundToInt(PanelCornerRadius) * 3;
            int cardSize = Mathf.RoundToInt(CardCornerRadius) * 3;

            skin.PanelFill = EnsureRoundedSprite(
                PanelFillPath, panelSize, Mathf.RoundToInt(PanelCornerRadius), 0f);
            skin.PanelOutline = EnsureRoundedSprite(
                PanelOutlinePath, panelSize, Mathf.RoundToInt(PanelCornerRadius), PanelOutlineStroke);
            skin.CardFill = EnsureRoundedSprite(
                CardFillPath, cardSize, Mathf.RoundToInt(CardCornerRadius), 0f);
            skin.Check = EnsureCheckSprite(CheckPath, 64);

            // 발광은 테두리 바깥으로 번져야 하므로 9-slice 경계가 반지름 + 번짐폭이다.
            int glowBorder = Mathf.RoundToInt(PanelCornerRadius + PanelGlowWidth);
            skin.PanelGlow = EnsureGlowSprite(
                PanelGlowPath, glowBorder * 3, Mathf.RoundToInt(PanelCornerRadius),
                PanelGlowWidth, glowBorder);

            skin.DepthPrime = EnsureDepthPrimeMaterial();

            return skin;
        }
    }

    /// <summary>
    /// 깊이만 찍는 머티리얼. 없으면 만든다.
    /// </summary>
    /// <remarks>
    /// 셰이더를 못 찾으면 null을 돌려주고 경고만 남긴다. 깊이 프라임이 빠져도
    /// 보드 자체는 그대로 동작한다 — 안개에 조금 더 묻힐 뿐이다.
    /// </remarks>
    private static Material EnsureDepthPrimeMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(DepthPrimeMaterialPath);

        if (existing != null)
            return existing;

        Shader shader = Shader.Find(DepthPrimeShaderName);

        if (shader == null)
        {
            Debug.LogWarning(
                $"셰이더를 찾지 못했습니다: {DepthPrimeShaderName}\n" +
                "Assets/ReBloom/Shaders/WorldUiDepthPrime.shader 가 컴파일됐는지 확인하세요. " +
                "깊이 프라임 없이 보드만 만들며, 화면 안개가 보드를 더 흐리게 덮습니다.");
            return null;
        }

        if (!AssetDatabase.IsValidFolder(MaterialFolder))
        {
            Debug.LogError($"폴더가 없습니다: {MaterialFolder}");
            return null;
        }

        var material = new Material(shader) { name = "M_WorldUiDepthPrime" };
        AssetDatabase.CreateAsset(material, DepthPrimeMaterialPath);
        AssetDatabase.ImportAsset(DepthPrimeMaterialPath, ImportAssetOptions.ForceUpdate);

        Debug.Log($"  머티리얼을 만들었습니다: {DepthPrimeMaterialPath}");

        return AssetDatabase.LoadAssetAtPath<Material>(DepthPrimeMaterialPath);
    }

    // ───────────────────────────── 계층 ─────────────────────────────

    private sealed class BuiltRefs
    {
        public readonly List<Graphic> Accents = new List<Graphic>();
        public CanvasGroup Group;
        public TextMeshProUGUI Kicker;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Footer;
        public TextMeshProUGUI Participation;
        public Image LumenPortrait;
        public Image EchoPortrait;
        public Renderer DepthPrime;
        public TextMeshProUGUI LumenName;
        public TextMeshProUGUI EchoName;
        public readonly List<TextMeshProUGUI> StepLabels = new List<TextMeshProUGUI>();
        public readonly List<TextMeshProUGUI> StepBodies = new List<TextMeshProUGUI>();
    }

    /// <summary>
    /// 프리팹으로 저장할 임시 계층을 만든다. 씬에는 올리지 않는다.
    /// </summary>
    private static GameObject BuildHierarchy(Skin skin, BoardContent content, out BuiltRefs refs)
    {
        refs = new BuiltRefs();

        // ── 루트 ────────────────────────────────────────────────
        var root = new GameObject("MissionGuideBoard", typeof(RectTransform));
        var rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0.5f);
        rootRect.anchorMax = new Vector2(0.5f, 0.5f);
        rootRect.pivot = new Vector2(0.5f, 0.5f);
        rootRect.sizeDelta = BoardSize;
        rootRect.localPosition = Vector3.zero;
        rootRect.localRotation = Quaternion.identity;
        rootRect.localScale = Vector3.one * content.BoardScale;

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 1;
        // GraphicRaycaster / CanvasScaler 를 일부러 넣지 않는다.

        refs.Group = root.AddComponent<CanvasGroup>();
        refs.Group.alpha = 0f;
        refs.Group.interactable = false;
        refs.Group.blocksRaycasts = false;

        // ── PanelGlow ───────────────────────────────────────────
        // Panel 보다 먼저(뒤에) 그려야 발광이 패널 뒤로 깔린다.
        // 네 변을 번짐폭만큼 바깥으로 밀어 패널보다 크게 잡는다.
        RectTransform glow = CreateChild("PanelGlow", rootRect);
        Stretch(glow, -PanelGlowWidth, -PanelGlowWidth, -PanelGlowWidth, -PanelGlowWidth);
        refs.Accents.Add(AddSlicedImage(glow, skin.PanelGlow, WithAlpha(AccentDefault, PanelGlowAlpha)));

        // ── Panel ───────────────────────────────────────────────
        RectTransform panel = CreateChild("Panel", rootRect);
        Stretch(panel, 0f, 0f, 0f, 0f);
        AddSlicedImage(panel, skin.PanelFill, PanelColor);

        // ── AccentBar ───────────────────────────────────────────
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
            kickerRect, skin.Font, "MISSION 01 · 조작 가이드", KickerFontSize,
            AccentDefault, TextAlignmentOptions.TopLeft);
        refs.Kicker.characterSpacing = KickerSpacing;
        refs.Accents.Add(refs.Kicker);

        RectTransform titleRect = TopBand("Title", header, TitleTop, TitleHeight, Pad, Pad);
        refs.Title = AddText(
            titleRect, skin.Font, "수로 복구 방법", TitleFontSize,
            TitleColor, TextAlignmentOptions.TopLeft);
        refs.Title.fontStyle = FontStyles.Bold;

        // 캐릭터 카드 2장
        RectTransform characters = TopBand(
            "Characters", header, CharactersTop, CharactersHeight, Pad, Pad);

        float cardWidth = (BoardSize.x - Pad * 2f - CharacterCardGap) * 0.5f;

        refs.LumenPortrait = BuildCharacterCard(
            characters, skin, "Lumen", 0f, cardWidth,
            content.LumenPortrait, content.LumenPortraitTint,
            "루멘", LumenColor, out refs.LumenName);

        refs.EchoPortrait = BuildCharacterCard(
            characters, skin, "Echo", cardWidth + CharacterCardGap, cardWidth,
            content.EchoPortrait, content.EchoPortraitTint,
            "에코", EchoColor, out refs.EchoName);

        RectTransform participationRect = TopBand(
            "Participation", header, ParticipationTop, ParticipationHeight, Pad, Pad);
        refs.Participation = AddText(
            participationRect, skin.Font, "두 캐릭터 모두 참여할 수 있어요.", ParticipationFontSize,
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
                labelRect, skin.Font, $"0{i + 1} · 제목", StepLabelFontSize,
                AccentDefault, TextAlignmentOptions.TopLeft);
            label.fontStyle = FontStyles.Bold;
            refs.StepLabels.Add(label);
            refs.Accents.Add(label);

            RectTransform bodyRect = TopBand(
                stepName + "Body", card, StepBodyTop, StepBodyHeight, StepInnerPad, StepInnerPad);
            TextMeshProUGUI body = AddText(
                bodyRect, skin.Font, "본문", StepBodyFontSize,
                BodyColor, TextAlignmentOptions.TopLeft);
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
            footerLabel, skin.Font, "모두 제거하면 수로가 복구됩니다.", FooterFontSize,
            FooterColor, TextAlignmentOptions.Left);

        // ── FogDepthPrime ───────────────────────────────────────
        refs.DepthPrime = BuildDepthPrime(rootRect, skin);

        // ── 스크립트 + 참조 배선 ────────────────────────────────
        var board = root.AddComponent<MissionGuideBoard>();
        var so = new SerializedObject(board);

        // 먼저 보존 값을 되돌린 뒤 참조를 덮어쓴다.
        // (CopyFromSerializedProperty가 참조 필드를 건드릴 일은 없지만 순서를 분명히 해 둔다)
        content.ApplyTo(so);

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

        SerializedProperty steps = so.FindProperty("steps");

        if (steps == null)
        {
            Debug.LogWarning("  ! MissionGuideBoard에 'steps' 필드가 없습니다.");
        }
        else
        {
            if (steps.arraySize != StepCount)
            {
                Debug.LogWarning(
                    $"  ! steps 길이가 {steps.arraySize} 입니다(기대 {StepCount}). 맞춰 둡니다.");
                steps.arraySize = StepCount;
            }

            for (int i = 0; i < StepCount; i++)
            {
                SerializedProperty element = steps.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("labelText").objectReferenceValue = refs.StepLabels[i];
                element.FindPropertyRelative("bodyText").objectReferenceValue = refs.StepBodies[i];
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();

        // anchor는 비워 둔다. 씬 배치 단계에서 WaterwayZone을 연결한다.
        // (씬 오브젝트는 프리팹 에셋에서 참조할 수 없다)

        // 직렬화된 문구를 TMP에 실제로 찍어 둔다. 문구의 출처를 한 곳으로 묶기 위함이다.
        board.ApplyContent();
        board.ApplyAccent();

        return root;
    }

    /// <summary>
    /// 보드와 같은 자리에 깊이만 찍는 쿼드. 화면 안개가 보드를 "배경 깊이"로 덮는 것을 막는다.
    /// </summary>
    /// <remarks>
    /// Canvas 자식이지만 UI가 아니라 MeshRenderer 다. 그래서 UI Mask·TMP·CanvasGroup 알파와
    /// 아무 상호작용이 없다. 셰이더가 <c>ColorMask 0</c> 이라 화면에는 아무것도 그리지 않고,
    /// <c>Offset 2, 2</c> 로 깊이를 아주 조금 뒤로 밀어 UI의 ZTest(LEqual)를 방해하지 않는다.
    ///
    /// 꺼짐 처리는 <c>MissionGuideBoard.ApplyAlpha</c> 가 맡는다. 보드가 숨겨진 동안에도
    /// 깊이를 찍으면 허공에 안개가 걷힌 사각형이 남기 때문이다.
    ///
    /// 머티리얼을 못 만들었으면(셰이더 컴파일 전 등) 쿼드를 만들지 않고 null을 돌려준다.
    /// 보드는 그대로 동작하고 안개에 조금 더 묻힐 뿐이다.
    /// </remarks>
    private static Renderer BuildDepthPrime(RectTransform parent, Skin skin)
    {
        if (skin.DepthPrime == null)
            return null;

        Mesh quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        if (quad == null)
        {
            Debug.LogWarning("  ! 내장 Quad 메시를 찾지 못해 깊이 프라임을 건너뜁니다.");
            return null;
        }

        var go = new GameObject("FogDepthPrime");
        go.transform.SetParent(parent, false);
        go.layer = parent.gameObject.layer;

        // 보드 면과 같은 크기·같은 평면. Z는 셰이더의 Offset이 책임진다.
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(BoardSize.x, BoardSize.y, 1f);

        go.AddComponent<MeshFilter>().sharedMesh = quad;

        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = skin.DepthPrime;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.allowOcclusionWhenDynamic = false;

        return renderer;
    }

    private static Image BuildCharacterCard(
        RectTransform parent, Skin skin, string name, float offsetX, float width,
        Sprite portraitSprite, Color portraitTint,
        string displayName, Color roleColor, out TextMeshProUGUI nameText)
    {
        RectTransform card = CreateChild(name, parent);
        card.anchorMin = new Vector2(0f, 0f);
        card.anchorMax = new Vector2(0f, 1f);
        card.pivot = new Vector2(0f, 0.5f);
        card.sizeDelta = new Vector2(width, 0f);
        card.anchoredPosition = new Vector2(offsetX, 0f);
        AddSlicedImage(card, skin.CardFill, CardColor);

        // 초상 뒤에 한 단계 밝은 판을 깔아 대비를 올린다.
        // 안개가 끼면 초상과 카드 배경의 명도가 붙어 인물이 묻히기 때문이다.
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

        Image portrait = AddImage(portraitRect, portraitTint);
        portrait.preserveAspect = true;   // 비율 유지 — 잘리지 않는다

        if (portraitSprite != null)
            portrait.sprite = portraitSprite;

        RectTransform nameRect = TopBand(name + "Name", card, CharacterNameTop, CharacterNameHeight, 0f, 0f);
        nameText = AddText(
            nameRect, skin.Font, displayName, CharacterNameFontSize,
            roleColor, TextAlignmentOptions.Top);
        nameText.fontStyle = FontStyles.Bold;

        RectTransform roleBar = CreateChild(name + "Accent", card);
        roleBar.anchorMin = new Vector2(0.5f, 1f);
        roleBar.anchorMax = new Vector2(0.5f, 1f);
        roleBar.pivot = new Vector2(0.5f, 1f);
        roleBar.sizeDelta = new Vector2(RoleBarWidth, RoleBarHeight);
        roleBar.anchoredPosition = new Vector2(0f, -RoleBarTop);
        AddImage(roleBar, roleColor);   // 역할색은 accentGraphics에 넣지 않는다

        return portrait;
    }

    // ───────────────────────────── 스프라이트 생성 ─────────────────────────────

    /// <summary>
    /// 둥근 사각형 스프라이트를 만들어 둔다. 이미 있으면 그대로 쓴다.
    /// </summary>
    /// <param name="stroke">0이면 채움, 양수면 그 두께의 테두리만 그린다.</param>
    /// <remarks>
    /// 9-slice 경계를 모서리 반지름과 같게 잡으므로, 어떤 크기로 늘려도 모서리 곡률이 그대로다.
    /// 흰색으로 그려 두고 <c>Image.color</c> 로 색을 입힌다.
    /// </remarks>
    private static Sprite EnsureRoundedSprite(string path, int size, int radius, float stroke)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (existing != null)
            return existing;

        var pixels = new Color32[size * size];
        float half = size * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;
                float d = RoundedRectSdf(px, py, half, half, radius);

                float alpha = stroke <= 0f
                    ? Mathf.Clamp01(0.5f - d)
                    : Mathf.Clamp01(Mathf.Min(d + stroke, -d) + 0.5f);

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        return WriteSprite(path, size, pixels, radius);
    }

    /// <summary>
    /// 둥근 사각형 바깥으로 부드럽게 번지는 발광 스프라이트. 이미 있으면 그대로 쓴다.
    /// </summary>
    /// <remarks>
    /// 안쪽은 비워 둔다(패널이 덮으므로 그릴 필요가 없고, 반투명 패널 뒤에서 비쳐 보이면
    /// 배경이 탁해진다). 경계 바로 안쪽 몇 px만 1로 채워 패널과 이음새가 생기지 않게 한다.
    /// 바깥은 제곱 감쇠라 가장자리가 뚜렷하지 않고 은은하게 사라진다.
    /// </remarks>
    private static Sprite EnsureGlowSprite(
        string path, int size, int radius, float glowWidth, int border)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (existing != null)
            return existing;

        const float InnerBleed = 6f;   // 패널과 겹쳐 이음새를 가리는 폭

        var pixels = new Color32[size * size];
        float half = size * 0.5f;

        // 발광이 바깥으로 번질 자리를 남기려면, 기준 사각형을 번짐폭만큼 줄여 둬야 한다.
        float innerHalf = half - glowWidth;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f - half;
                float py = y + 0.5f - half;
                float d = RoundedRectSdf(px, py, innerHalf, innerHalf, radius);

                float alpha;

                if (d <= -InnerBleed)
                {
                    alpha = 0f;
                }
                else if (d <= 0f)
                {
                    alpha = (d + InnerBleed) / InnerBleed;
                }
                else
                {
                    float t = Mathf.Clamp01(1f - d / glowWidth);
                    alpha = t * t;
                }

                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        return WriteSprite(path, size, pixels, border);
    }

    /// <summary>둥근 사각형의 부호 있는 거리. 안쪽이 음수다.</summary>
    private static float RoundedRectSdf(float px, float py, float halfW, float halfH, float radius)
    {
        float qx = Mathf.Abs(px) - halfW + radius;
        float qy = Mathf.Abs(py) - halfH + radius;

        float outsideX = Mathf.Max(qx, 0f);
        float outsideY = Mathf.Max(qy, 0f);
        float outside = Mathf.Sqrt(outsideX * outsideX + outsideY * outsideY);

        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    /// <summary>원 테두리 + 체크 표시. 완료 안내 앞에 붙인다.</summary>
    private static Sprite EnsureCheckSprite(string path, int size)
    {
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);

        if (existing != null)
            return existing;

        var pixels = new Color32[size * size];
        float half = size * 0.5f;

        float ringRadius = half - 4f;
        float ringHalfStroke = 1.6f;
        float checkHalfStroke = 2.2f;

        // 체크 꺾은선 (텍스처 좌표, y는 위로).
        var a = new Vector2(size * 0.30f, size * 0.52f);
        var b = new Vector2(size * 0.44f, size * 0.36f);
        var c = new Vector2(size * 0.72f, size * 0.66f);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);

                float toCenter = Mathf.Abs(Vector2.Distance(p, new Vector2(half, half)) - ringRadius);
                float ring = Mathf.Clamp01(ringHalfStroke - toCenter + 0.5f);

                float toCheck = Mathf.Min(
                    DistanceToSegment(p, a, b),
                    DistanceToSegment(p, b, c));
                float mark = Mathf.Clamp01(checkHalfStroke - toCheck + 0.5f);

                float alpha = Mathf.Max(ring, mark);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }

        return WriteSprite(path, size, pixels, 0);
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float lengthSqr = ab.sqrMagnitude;

        if (lengthSqr <= 1e-6f)
            return Vector2.Distance(p, a);

        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
        return Vector2.Distance(p, a + ab * t);
    }

    private static Sprite WriteSprite(string path, int size, Color32[] pixels, int border)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply();

        byte[] png = texture.EncodeToPNG();
        Object.DestroyImmediate(texture);

        string directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory) && !AssetDatabase.IsValidFolder(directory))
        {
            Debug.LogError($"폴더가 없습니다: {directory}");
            return null;
        }

        File.WriteAllBytes(path, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer == null)
        {
            Debug.LogError($"임포터를 찾지 못했습니다: {path}");
            return null;
        }

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spriteBorder = new Vector4(border, border, border, border);
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();

        Debug.Log($"  스프라이트를 만들었습니다: {path}  ({size}×{size}, 9-slice {border})");

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ───────────────────────────── 검증 ─────────────────────────────

    private static void Validate()
    {
        Debug.Log("===== 미션 가이드 보드 프리팹 [VALIDATE] =====");

        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (asset == null)
        {
            Debug.LogError($"프리팹이 없습니다: {PrefabPath}");
            return;
        }

        int problems = 0;

        string[] expected =
        {
            "PanelGlow", "Panel",
            "AccentBar", "AccentBar/PanelOutline",
            "Header", "Header/Kicker", "Header/Title", "Header/Participation",
            "Header/Characters",
            "Header/Characters/Lumen", "Header/Characters/Lumen/LumenPortraitBack",
            "Header/Characters/Lumen/LumenPortrait",
            "Header/Characters/Lumen/LumenName", "Header/Characters/Lumen/LumenAccent",
            "Header/Characters/Echo", "Header/Characters/Echo/EchoPortraitBack",
            "Header/Characters/Echo/EchoPortrait",
            "Header/Characters/Echo/EchoName", "Header/Characters/Echo/EchoAccent",
            "Steps",
            "Steps/Step1", "Steps/Step1/Step1Label", "Steps/Step1/Step1Body",
            "Steps/Step2", "Steps/Step2/Step2Label", "Steps/Step2/Step2Body",
            "Steps/Step3", "Steps/Step3/Step3Label", "Steps/Step3/Step3Body",
            "Footer", "Footer/FooterCheck", "Footer/FooterLabel",
            "FogDepthPrime",
        };

        foreach (string path in expected)
        {
            if (asset.transform.Find(path) == null)
            {
                Debug.LogError($"  ✗ 자식 없음: {path}", asset);
                problems++;
            }
        }

        var canvas = asset.GetComponent<Canvas>();
        problems += Expect(canvas != null, "Canvas 있음", asset);

        if (canvas != null)
        {
            problems += Expect(canvas.renderMode == RenderMode.WorldSpace,
                $"Canvas.renderMode = WorldSpace (현재 {canvas.renderMode})", asset);
            problems += Expect(canvas.sortingOrder == 1,
                $"Canvas.sortingOrder = 1 (현재 {canvas.sortingOrder})", asset);
        }

        var group = asset.GetComponent<CanvasGroup>();
        problems += Expect(group != null, "CanvasGroup 있음", asset);

        if (group != null)
        {
            problems += Expect(Mathf.Approximately(group.alpha, 0f),
                $"CanvasGroup.alpha = 0 (현재 {group.alpha})", asset);
            problems += Expect(!group.interactable, "CanvasGroup.interactable = false", asset);
            problems += Expect(!group.blocksRaycasts, "CanvasGroup.blocksRaycasts = false", asset);
        }

        // ── XR 안전 ─────────────────────────────────────────────
        problems += Expect(asset.GetComponentInChildren<GraphicRaycaster>(true) == null,
            "GraphicRaycaster 없음", asset);
        problems += Expect(asset.GetComponentInChildren<Collider>(true) == null,
            "Collider 없음", asset);

        foreach (Graphic graphic in asset.GetComponentsInChildren<Graphic>(true))
        {
            if (graphic.raycastTarget)
            {
                Debug.LogError($"  ✗ raycastTarget이 켜져 있습니다: {graphic.name}", graphic);
                problems++;
            }
        }

        // ── 스크립트 참조 ───────────────────────────────────────
        var board = asset.GetComponent<MissionGuideBoard>();
        problems += Expect(board != null, "MissionGuideBoard 있음", asset);

        if (board != null)
        {
            var so = new SerializedObject(board);

            problems += ExpectRef(so, "canvasGroup");
            problems += ExpectRef(so, "kickerText");
            problems += ExpectRef(so, "titleText");
            problems += ExpectRef(so, "footerText");
            problems += ExpectRef(so, "lumenPortrait");
            problems += ExpectRef(so, "echoPortrait");
            problems += ExpectRef(so, "lumenNameText");
            problems += ExpectRef(so, "echoNameText");
            problems += ExpectRef(so, "participationText");

            SerializedProperty accents = so.FindProperty("accentGraphics");
            // 발광 + 윤곽선 + Kicker + STEP 라벨 + 체크
            int expectedAccents = 1 + 1 + 1 + StepCount + 1;
            problems += Expect(accents != null && accents.arraySize == expectedAccents,
                $"accentGraphics {expectedAccents}개 " +
                $"(현재 {(accents == null ? "필드 없음" : accents.arraySize.ToString())})",
                asset);

            SerializedProperty steps = so.FindProperty("steps");
            problems += Expect(steps != null && steps.arraySize == StepCount,
                $"steps {StepCount}칸 " +
                $"(현재 {(steps == null ? "필드 없음" : steps.arraySize.ToString())})",
                asset);

            if (steps != null)
            {
                for (int i = 0; i < steps.arraySize; i++)
                {
                    SerializedProperty element = steps.GetArrayElementAtIndex(i);
                    problems += ExpectRelativeRef(element, "labelText", i);
                    problems += ExpectRelativeRef(element, "bodyText", i);
                }
            }

            SerializedProperty anchor = so.FindProperty("anchor");

            if (anchor != null && anchor.objectReferenceValue == null)
                Debug.Log("  · anchor 비어 있음 — 씬 배치 단계에서 WaterwayZone을 연결하세요. (정상)");
        }

        // ── 초상 스프라이트 ─────────────────────────────────────
        problems += ExpectSprite(asset, "Header/Characters/Lumen/LumenPortrait");
        problems += ExpectSprite(asset, "Header/Characters/Echo/EchoPortrait");

        // ── 깊이 프라임 ─────────────────────────────────────────
        Transform depthPrime = asset.transform.Find("FogDepthPrime");
        var depthRenderer = depthPrime != null ? depthPrime.GetComponent<Renderer>() : null;

        if (depthRenderer == null)
        {
            Debug.LogWarning(
                "  ! FogDepthPrime 이 없습니다. 보드는 동작하지만 화면 안개에 더 묻힙니다. " +
                $"'{DepthPrimeShaderName}' 셰이더가 컴파일됐는지 확인하고 다시 Restyle 하세요.");
        }
        else if (depthRenderer.sharedMaterial == null)
        {
            Debug.LogError("  ✗ FogDepthPrime 에 머티리얼이 없습니다.", depthRenderer);
            problems++;
        }
        else
        {
            Debug.Log($"  ✓ FogDepthPrime → {depthRenderer.sharedMaterial.name}", depthRenderer);
        }

        // ── 월드 크기 ───────────────────────────────────────────
        var rootRect = asset.transform as RectTransform;

        if (rootRect != null)
        {
            Vector2 world = rootRect.sizeDelta * rootRect.localScale.x;
            Debug.Log($"  · 월드 크기 {world.x:F2} × {world.y:F2} m (scale {rootRect.localScale.x})");
        }

        // ── 글이 영역을 넘지 않는지 ─────────────────────────────
        problems += CheckTextOverflow(asset);

        // ── 폰트 ────────────────────────────────────────────────
        foreach (TMP_Text text in asset.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.font == null)
            {
                Debug.LogError($"  ✗ {text.name}: Font Asset이 비어 있습니다.", text);
                problems++;
                continue;
            }

            string path = AssetDatabase.GetAssetPath(text.font);

            if (path != KoreanFontPath)
            {
                Debug.LogWarning(
                    $"  ! {text.name}: 폰트가 {path} 입니다. 한글이 깨질 수 있습니다 " +
                    $"(기대: {KoreanFontPath})", text);
            }
        }

        Debug.Log(problems == 0
            ? "검증 통과 — 문제 없음."
            : $"검증 실패 — 문제 {problems}건. 위 로그를 확인하세요.");
    }

    /// <summary>
    /// TMP가 자기 RectTransform을 넘치는지 본다.
    /// </summary>
    /// <remarks>
    /// 프리팹 에셋의 TMP는 레이아웃이 계산돼 있지 않을 수 있어
    /// <c>ForceMeshUpdate</c> 로 한 번 재서 <c>GetPreferredValues</c> 와 비교한다.
    /// 값은 참고용이다 — 실제 렌더 결과는 Unity에서 눈으로 확인해야 한다.
    /// </remarks>
    private static int CheckTextOverflow(GameObject asset)
    {
        int problems = 0;

        foreach (TMP_Text text in asset.GetComponentsInChildren<TMP_Text>(true))
        {
            var rect = text.rectTransform;
            float width = rect.rect.width;
            float height = rect.rect.height;

            if (width <= 1f || height <= 1f)
                continue;

            Vector2 preferred = text.GetPreferredValues(text.text, width, 0f);

            if (preferred.y > height + 1f)
            {
                Debug.LogWarning(
                    $"  ! {text.name}: 글 높이 {preferred.y:F0} > 영역 {height:F0} — 잘릴 수 있습니다.",
                    text);
                problems++;
            }
        }

        return problems;
    }

    private static int ExpectSprite(GameObject asset, string path)
    {
        Transform found = asset.transform.Find(path);
        Image image = found != null ? found.GetComponent<Image>() : null;

        if (image == null)
            return 0;   // 자식 누락은 위에서 이미 셌다

        if (image.sprite == null)
        {
            Debug.LogError($"  ✗ {path}: Sprite가 비어 있습니다.", image);
            return 1;
        }

        Debug.Log($"  ✓ {path} → {image.sprite.name}", image);
        return 0;
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

    private static int ExpectRef(SerializedObject so, string fieldName)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ '{fieldName}' 필드가 없습니다. 스크립트가 바뀌었는지 확인하세요.");
            return 1;
        }

        if (property.objectReferenceValue == null)
        {
            Debug.LogError($"  ✗ {fieldName} 참조가 비어 있습니다.");
            return 1;
        }

        Debug.Log($"  ✓ {fieldName} → {property.objectReferenceValue.name}");
        return 0;
    }

    private static int ExpectRelativeRef(SerializedProperty element, string fieldName, int index)
    {
        SerializedProperty property = element.FindPropertyRelative(fieldName);

        if (property == null)
        {
            Debug.LogError($"  ✗ steps[{index}].{fieldName} 필드가 없습니다.");
            return 1;
        }

        if (property.objectReferenceValue == null)
        {
            Debug.LogError($"  ✗ steps[{index}].{fieldName} 참조가 비어 있습니다.");
            return 1;
        }

        Debug.Log($"  ✓ steps[{index}].{fieldName} → {property.objectReferenceValue.name}");
        return 0;
    }

    // ───────────────────────────── 계획 보고 ─────────────────────────────

    private static void ReportPlan()
    {
        float cardWidth = (BoardSize.x - Pad * 2f - CharacterCardGap) * 0.5f;

        Debug.Log(
            $"  대상 경로: {PrefabPath}\n" +
            $"  보드 크기: {BoardSize.x} × {BoardSize.y} @ scale {DefaultBoardScale} " +
            $"→ 월드 {BoardSize.x * DefaultBoardScale:F2} × {BoardSize.y * DefaultBoardScale:F2} m " +
            "(기존 프리팹의 Scale이 다르면 그 값을 유지합니다)\n" +
            "  MissionGuideBoard\n" +
            "    Canvas         Render Mode = World Space, Sorting Order = 1\n" +
            "    CanvasGroup    alpha 0, interactable false, blocksRaycasts false\n" +
            "    GraphicRaycaster  추가하지 않음\n" +
            $"  ├ PanelGlow      바깥 발광 {PanelGlowWidth}px, 강조색 α{PanelGlowAlpha} (스프라이트, Bloom 미사용)\n" +
            $"  ├ Panel          둥근 남색 반투명 (r{PanelCornerRadius}, α{PanelColor.a})\n" +
            $"  ├ AccentBar/PanelOutline  둥근 테두리 {PanelOutlineStroke}px, 강조색 α{OutlineAlpha}\n" +
            "  ├ Header\n" +
            $"  │ ├ Kicker       {KickerFontSize}pt, 강조색, 자간 {KickerSpacing}\n" +
            $"  │ ├ Title        {TitleFontSize}pt Bold\n" +
            $"  │ ├ Characters   카드 2장 {cardWidth:F0} × {CharactersHeight}\n" +
            $"  │ │   Portrait   {PortraitSize} × {PortraitSize}, Preserve Aspect\n" +
            $"  │ │   Name       {CharacterNameFontSize}pt Bold + 역할색 바 {RoleBarWidth}×{RoleBarHeight}\n" +
            $"  │ └ Participation {ParticipationFontSize}pt, 가운데\n" +
            $"  ├ Steps          카드 {StepCount}장, 높이 {StepCardHeight} / 간격 {StepCardGap}\n" +
            $"  │   Label {StepLabelFontSize}pt Bold → 본문 사이 {StepBodyTop - StepLabelTop - StepLabelHeight}px\n" +
            $"  │   Body  {StepBodyFontSize}pt, 줄간격 {StepBodyLineSpacing}, 영역 {StepBodyHeight}\n" +
            $"  ├ Footer         체크 {FooterCheckSize}px + {FooterFontSize}pt\n" +
            "  └ FogDepthPrime  깊이만 찍는 쿼드 — 화면 안개가 보드를 배경 깊이로 덮는 것을 막음");
    }

    // ───────────────────────────── 보조 ─────────────────────────────

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

    /// <summary>네 변을 부모에 붙인다. 값은 각 변의 안쪽 여백(px).</summary>
    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>부모 위쪽에 붙인 가로 띠. 위에서 <paramref name="top"/> px 내려와 <paramref name="height"/> px 높이.</summary>
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
        image.raycastTarget = false;   // XR Ray/Poke가 이 UI를 집지 않게 한다
        return image;
    }

    /// <summary>9-slice 스프라이트를 쓰는 Image. 스프라이트가 없으면 단색 사각형으로 떨어진다.</summary>
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
    {
        return new Color(color.r, color.g, color.b, alpha);
    }

    private static void SetObject(SerializedObject so, string fieldName, Object value)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.objectReferenceValue = value;
    }

    private static void SetArray(SerializedObject so, string fieldName, List<Graphic> values)
    {
        SerializedProperty property = so.FindProperty(fieldName);

        if (property == null)
        {
            Debug.LogWarning($"  ! MissionGuideBoard에 '{fieldName}' 필드가 없습니다.");
            return;
        }

        property.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
