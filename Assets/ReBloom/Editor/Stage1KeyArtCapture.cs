using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// 졸업전시 대표 이미지용 Stage1(복원 전) 배경을 정사각 PNG로 찍는 도구.
///
/// <b>왜 전용 씬을 쓰는가</b> —
/// <c>Stage1_PanoramaCapture.unity</c> 는 Stage1 복제본이면서 촬영에 방해되는 것들이
/// 이미 정리돼 있다. <c>NetworkRunner</c>·<c>PersistentVRSystem</c>·<c>ConnectionManager</c> 가
/// 아예 없어 Fusion 없이 Edit 모드로 열리고, <c>TutorialMissionManager</c> 와
/// <c>--- DEBUG (Test Only) ---</c> 도 꺼져 있다. 그래서 <b>Stage1.unity 원본은 건드리지 않는다</b>.
///
/// <b>게임과 같은 그림이 나오는 이유</b> —
/// 이 프로젝트의 후처리는 씬 Volume 이 아니라 프로젝트 기본 볼륨
/// (<c>Assets/Settings/DefaultVolumeProfile.asset</c> — Haze/Oasis 안개)에 들어 있고,
/// 안개·SSAO 는 <c>PC_Renderer.asset</c> 의 Renderer Feature(<c>FlatKitFog</c>,
/// <c>ScreenSpaceAmbientOcclusion</c>)다. 둘 다 <b>카메라가 URP 로 그리기만 하면 자동 적용</b>된다.
/// 그래서 임시 카메라에 <c>renderPostProcessing</c> 만 켜면 게임 화면과 같아진다.
/// (Stage1 은 Unity 내장 안개 <c>m_Fog</c> 가 꺼져 있고, 분위기는 Directional Light +
///  스카이박스 + 보랏빛 앰비언트가 만든다 — 모두 씬에 그대로 있으므로 따로 손댈 것이 없다)
///
/// <b>비가 Edit 모드에서도 찍히는 이유</b> —
/// <c>Rain_shower</c> 는 ParticleSystem 8개로 되어 있는데 파티클은 Edit 모드에서 스스로 돌지 않는다.
/// 그래서 찍기 직전에 <see cref="ParticleSystem.Simulate(float,bool,bool)"/> 로 미리 돌려 둔다.
/// Play 모드가 필요 없다.
///
/// <b>공명 상태(안개)</b> —
/// 이 게임의 안개는 FlatKit 거리 안개이고, 그 강도는 공명 제약 시스템이 소유한다
/// (<c>FogConstraint.Tick</c>). Edit 모드에는 그 로직이 아예 없어서 가만히 찍으면
/// <b>항상 최대 제약(1.0×)</b>, 즉 두 플레이어가 가장 멀리 떨어진 최악의 화면이 나온다.
/// 그래서 게임에 실제로 존재하는 세 상태를 골라 찍을 수 있게 해 둔다 —
/// 자세한 내용은 <see cref="ResonanceFogState"/> 와 <see cref="ApplyResonanceFogState"/>.
///
/// <b>공명 포즈</b> —
/// 엔딩씬(Stage3)의 "두 캐릭터가 마주 보고 손바닥을 맞대는" 포즈를 여기서 재현할 수 있다.
/// 그 포즈는 애니메이션 클립이 아니라 Animation Rigging 의 <c>TwoBoneIKConstraint</c> 이고,
/// <c>Stage3EndingCutscene.ApplyHandReach(1)</c> 에서 IK 타깃이 정확히 <c>MeetPose</c> 가 된다.
/// 그래서 Animator 가 돌지 않는 Edit 모드에서도 손 위치·방향은 런타임과 같은 값으로 재현된다.
/// 실제 모델은 <c>Player_mental</c>·<c>Player_ear</c> 프리팹의 임시 복제본을 쓰고 렌더가 끝나면
/// 지운다 — 자세한 내용은 <see cref="ApplyResonancePose"/>.
///
/// <b>원상복구</b> —
/// 비·천둥 켜기, Canvas 끄기, Outline 끄기, 공명 상태, 임시 카메라, RenderTexture 는 전부 임시이고
/// <c>finally</c> 에서 <b>역순으로</b> 되돌린다. 씬은 저장하지 않는다.
/// 되돌린 뒤 상태가 원래와 같으므로, 실수로 저장해도 내용은 달라지지 않는다.
/// 공유 에셋(<c>FogSettings.asset</c>·Effect Material·URP Renderer)은
/// <b>값을 쓰지 않는 경로</b>를 기본으로 쓰고, 부득이한 경우에도 dirty 플래그까지 되돌린다.
/// </summary>
public class Stage1KeyArtCapture : EditorWindow
{
    // ── 고정값 ────────────────────────────────────────────────────────────
    private const string SceneName = "Stage1_PanoramaCapture";
    private const string ScenePath = "Assets/ReBloom/Scenes/Stage1_PanoramaCapture.unity";
    private const string OutputFolder = "Assets/ReBloom/KeyArt";
    private const string OutputName = "Stage1_Before_KeyArt";

    private const int PreviewSize = 1024;
    private const int CaptureSize = 2048;
    private const int MsaaSamples = 4;

    /// <summary>임시로 켤 연출. 씬에서는 꺼져 있다(파노라마 촬영용으로 꺼 둔 상태).</summary>
    private static readonly string[] WeatherRootNames = { "Rain_shower", "Thunder_vfx" };

    /// <summary>임시로 끌 것. 미션 UI 캔버스가 화면에 들어오면 안 된다.</summary>
    private static readonly string[] HideRootNames = { "Canvas" };

    // ── 캐릭터 배치 가이드 ────────────────────────────────────────────────
    //
    // 실제 캐릭터 모델을 씬에 넣기 전에 "어디에 얼마나 크게 서는지"만 눈으로 확인하는 가이드다.
    // 그리기만 하고 씬에는 아무것도 만들지 않으며, OnGUI 에서만 그리므로 저장되는 PNG 에는
    // 들어가지 않는다(렌더는 Render() 가 따로 한다).
    //
    // 좌표는 프리셋 D 설계 때 쓴 값이다 — 지면 y = 0.03 은 Floor 와 두 스폰 포인트에서
    // 확인한 값이고, z = -8.45 는 스폰 Z선, x = ±1.10 은 거리 중심축(x ≈ 0) 기준 대칭이다.
    // 높이·폭은 아직 측정값이 아니라 가정이다(아바타는 VR HMD 구동이라 메시 높이를
    // YAML 로 잴 수 없다).

    private const float CharacterGuideHeight = 1.7f;
    private const float CharacterGuideWidth = 0.5f;

    // 기본 좌표. 거리 중심축(x ≈ 0)을 기준으로 좌우 대칭이고, 카메라가 x = 0 인
    // 프리셋(D·E)에서 화면 좌우 ±0.401 로 대칭이 된다.
    //
    // 프리셋 F 처럼 카메라를 옆으로 옮기면 가까운 캐릭터가 시차로 크게 밀리므로,
    // 프리셋마다 제 좌표를 들고 다닌다(<see cref="CameraPreset.LumenPosition"/>).
    private static readonly Vector3 DefaultLumenGuidePosition = new Vector3(-1.10f, 0.03f, -8.45f);
    private static readonly Vector3 DefaultEchoGuidePosition = new Vector3(1.10f, 0.03f, -8.45f);

    // 기존 로고(청록)·지면(호박) 가이드와 헷갈리지 않도록 보라/민트를 쓴다.
    private static readonly Color LumenGuideColor = new Color(0.85f, 0.45f, 1f);
    private static readonly Color EchoGuideColor = new Color(0.45f, 1f, 0.70f);

    // ── 로고 합성 ─────────────────────────────────────────────────────────
    //
    // 졸업전시 대표 이미지에 들어갈 로고를 배경·캐릭터 렌더가 끝난 뒤 픽셀로 얹는다.
    //
    // 왜 디스크에서 직접 읽는가 — ReBloom_Logo.png.meta 는 <c>isReadable: 0</c> 이고
    // <c>textureType: 8</c>(Sprite)이다. 그래서 <c>AssetDatabase.LoadAssetAtPath&lt;Texture2D&gt;</c>
    // 로 가져와도 <c>GetPixels</c> 가 막혀 있고, 플랫폼 오버라이드에 압축이 걸려 있어
    // 원본 픽셀도 아니다. Import Settings 를 바꾸지 않는다는 조건을 지키려면
    // <b>PNG 바이트를 직접 디코드</b>하는 수밖에 없고, 그게 "원본 디자인 그대로"에도 더 맞다.
    //
    // 왜 정규화 좌표인가 — 미리보기(1024)와 최종(2048)이 같은 <c>Render</c> 경로를 타므로,
    // 위치·크기를 이미지 크기에 대한 비율로 두면 두 해상도에서 같은 구도가 나온다.
    //
    // 세로 크기는 조절하지 않는다 — 가로 폭만 받고 세로는 원본 비율(2172×724 = 정확히 3:1)로
    // 유도한다. 그래서 늘어나거나 찌그러질 수 없다.

    private const string LogoAssetPath = "Assets/ReBloom/UI/StartScene/Sprites/ReBloom_Logo.png";

    /// <summary>
    /// 로고 원본 픽셀. 프리멀티플라이드 알파로 들고 있어야 축소할 때 투명한 쪽 색이
    /// 번지지 않는다(straight alpha 를 그냥 평균하면 외곽에 테두리가 생긴다).
    /// </summary>
    private struct LogoSource
    {
        public Color[] Premultiplied;
        public int Width;
        public int Height;

        public bool IsValid => Premultiplied != null && Width > 0 && Height > 0;

        /// <summary>세로/가로. 이 값으로 세로 크기를 유도하므로 비율이 깨지지 않는다.</summary>
        public float Aspect => (float)Height / Width;
    }

    // ── 공명 상태 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 게임에서 실제로 나타나는 안개 상태. 배율은 <c>FogConstraint.Tick</c> 의 식을 그대로 쓴다.
    /// <code>
    /// float relief = maxConstraintRelief * proximity;
    /// float target = inactive ? 0f : activeIntensity * (1f - relief);
    /// </code>
    /// </summary>
    private enum ResonanceFogState
    {
        /// <summary><c>proximity = 0</c> — 두 플레이어가 공명 거리 밖. 에셋에 저장된 값 그대로.</summary>
        MaxConstraint = 0,

        /// <summary><c>proximity = 1</c> — 루멘과 에코가 최대로 붙어 있는 순간.</summary>
        Relieved = 1,

        /// <summary><c>inactive = true</c> — 협동 활성화 성공(공명 해제) 이후. 안개가 사라진다.</summary>
        Released = 2,
    }

    /// <summary>
    /// <c>activeIntensity</c>·<c>maxConstraintRelief</c> 의 출처.
    /// 게임이 실제로 쓰는 <c>ResonanceSystem.prefab</c> 에서 읽어 오고, 못 읽으면 폴백을 쓴다.
    /// </summary>
    private const string ResonancePrefabPath = "Assets/ReBloom/Prefabs/ResonanceSystem.prefab";

    /// <summary>프리팹을 못 읽을 때의 폴백. 2026-10-31 기준 프리팹의 실제 값과 같다.</summary>
    private const float FallbackActiveIntensity = 1f;
    private const float FallbackMaxConstraintRelief = 0.4f;

    private struct FogConstraintValues
    {
        public float ActiveIntensity;
        public float MaxConstraintRelief;

        /// <summary>프리팹에서 실제로 읽었는가. false 면 폴백 상수를 쓴 것이다.</summary>
        public bool FromPrefab;
    }

    private static readonly int DistanceFogIntensityId = Shader.PropertyToID("_DistanceFogIntensity");
    private static readonly int HeightFogIntensityId = Shader.PropertyToID("_HeightFogIntensity");

    /// <summary>
    /// <c>FlatKitFog</c> 가 실제로 쓰는 머티리얼 필드. 여기에 임시 복제본을 끼워 넣으면
    /// 공유 에셋을 한 글자도 건드리지 않고 안개 강도를 바꿀 수 있다.
    /// </summary>
    private static readonly FieldInfo FogEffectMaterialField =
        typeof(FlatKit.FlatKitFog).GetField("_effectMaterial",
            BindingFlags.Instance | BindingFlags.NonPublic);

    // ── 구도 프리셋 ───────────────────────────────────────────────────────
    //
    // 씬에서 확인한 사실 — City_Buildings 인스턴스는 (13.5, 0, 43.7)에 회전 0 · 스케일 1 로
    // 놓여 있어서, 자식 건물의 월드 좌표 = (13.5, 0, 43.7) + 로컬 좌표다. 그 결과:
    //
    //   Building_07_city        (-16.17,  -0.37,  25.19)   ← 왼쪽 폐허
    //   Building_14_city        ( 13.38,  15.93,  25.54)   ← 오른쪽 높은 건물
    //   Construction_site_01    (-47.32,  16.13,  25.20)   ← 더 왼쪽(화각 밖)
    //   Building_11_city        (-37.32,   4.13,   1.59)
    //   Building_12_city        ( 29.14,  29.33,  -0.26)   ← 오른쪽 옆(화각 밖)
    //   Bridge_01_city          ( 71.08,   7.53,  40.99)
    //   Building_04 (별도 루트) ( 12.00,   0.03,  -4.78)   ← 카메라 옆
    //   Train                   (  2.03,   1.88,  19.91)   ← 중앙 구조물
    //   HostSpawnPoint          (  1.53,   0.03,  -8.45)   yaw 0 → +Z 를 바라봄
    //   Floor (plane, scale 7)  원점 중심 약 70×70 → x,z 모두 -35~35 가 지면
    //
    // 여기서부터는 추정 — 아래 카메라 값은 위 좌표로 화각을 계산해 "왼쪽 폐허 ·
    // 중앙 거리 · 오른쪽 건물"이 1:1 안에 들어오도록 삼각법으로 잡은 값이다.
    // 실제로 어떻게 보이는지는 미리보기로 확인해야 한다. 조명·건물 실루엣·잔해 배치까지
    // 계산한 것이 아니므로 그대로 쓰기보다 출발점으로 삼고 손으로 다듬는 편이 좋다.
    private struct CameraPreset
    {
        public string Name;
        public string Note;
        public Vector3 Position;
        public Vector3 Euler;
        public float FieldOfView;

        // 클리핑도 프리셋에 담는다 — Scene 뷰에서 구도를 가져오면(PullFromSceneView) 이 값이
        // 바뀌므로, 담아 두지 않으면 프리셋을 다시 눌러도 그때의 상태로 돌아오지 않는다.
        public float Near;
        public float Far;

        // 캐릭터 가이드 좌표도 프리셋마다 따로 둔다. 카메라를 옆으로 옮기면 가까운 캐릭터가
        // 먼 배경보다 훨씬 크게 밀리므로(시차), 배경을 맞춘 구도마다 캐릭터 위치가 달라진다.
        public Vector3 LumenPosition;
        public Vector3 EchoPosition;

        /// <summary>
        /// true 면 <see cref="ApplyPreset"/> 이 <see cref="Setup"/> 의 값까지 전부 덮어쓴다.
        /// A~F 는 이 칸을 쓰지 않으므로(struct 기본값 false) 적용 동작이 전과 똑같다 —
        /// 구도만 바뀌고 공명 상태·포즈·로고는 사용자가 맞춰 둔 그대로 남는다.
        /// </summary>
        public bool AppliesSetup;

        public PresetSetup Setup;
    }

    /// <summary>
    /// 구도 말고 "촬영 한 장을 재현하는 데 필요한 나머지 전부". 전시용 최종 프리셋처럼
    /// 눌렀을 때 결과가 결정적이어야 하는 경우에만 쓴다.
    /// </summary>
    private struct PresetSetup
    {
        public ResonanceFogState FogState;

        public bool ShowPose;
        public Vector3 PosePairCenter;
        public float PosePairYaw;
        public bool ApplyCloseGap;
        public bool EvaluateRig;

        public bool ShowLogo;
        public Vector2 LogoCenter;
        public float LogoWidth;
        public float LogoOpacity;
    }

    private static readonly CameraPreset[] Presets =
    {
        new CameraPreset
        {
            Name = "A · 시네마틱 와이드 (추천)",
            Note = "캐릭터 뒤에서 도시를 바라보는 포스터 구도. 카메라를 띄우고 살짝 내려다본다.\n" +
                   "계산상 Building_07(좌 -21.8°) · Train(중앙 +4.3°) · Building_14(우 +19.3°) 가 들어오고,\n" +
                   "지면은 카메라 앞 약 3.3 m 부터 보여 하단에 캐릭터를 올릴 자리가 남는다.",
            Position = new Vector3(-0.5f, 2.6f, -14.0f),
            Euler = new Vector3(6f, -3f, 0f),
            FieldOfView = 55f,
            Near = 0.3f,
            Far = 1000f,
            LumenPosition = DefaultLumenGuidePosition,
            EchoPosition = DefaultEchoGuidePosition,
        },
        new CameraPreset
        {
            Name = "B · 낮은 앵글 · 하늘 넓게",
            Note = "카메라를 낮추고 거의 수평으로 둬서 위쪽 하늘이 넓다 — 로고 자리를 크게 잡고 싶을 때.\n" +
                   "A 보다 건물이 더 올려다보이고 웅장해진다. 지면은 약 3.5 m 부터.",
            Position = new Vector3(0f, 1.8f, -17.0f),
            Euler = new Vector3(2f, -2f, 0f),
            FieldOfView = 50f,
            Near = 0.3f,
            Far = 1000f,
            LumenPosition = DefaultLumenGuidePosition,
            EchoPosition = DefaultEchoGuidePosition,
        },
        new CameraPreset
        {
            Name = "C · 플레이어 시점 (게임 그대로)",
            Note = "씬에 원래 있던 PanoramaCaptureCamera 값 그대로. HostSpawnPoint 위치 · 눈높이 1.6 m.\n" +
                   "게임 시작 시 실제로 보이는 장면이지만, 하단 지면 여유와 로고 공간은 가장 적다.",
            Position = new Vector3(1.5f, 1.6f, -8.5f),
            Euler = Vector3.zero,
            FieldOfView = 60f,
            Near = 0.3f,
            Far = 1000f,
            LumenPosition = DefaultLumenGuidePosition,
            EchoPosition = DefaultEchoGuidePosition,
        },
        new CameraPreset
        {
            // 씬 실측으로 설계한 구도. 수치 근거는 배열 아래 주석에 정리해 두었다.
            Name = "D · 캐릭터 중심 시네마틱",
            Note = "하단 중앙에 캐릭터 둘(뒷모습)을 세우고 상단 1/3 을 로고로 비우는 포스터 구도.\n" +
                   "A 의 두 가지 문제를 수치로 교정했다 — 하단 침수 도로가 화면의 35~40% 를\n" +
                   "차지하던 것을 4.3% 로 줄이고(지면이 5.36 m 부터 보인다), 정중앙에서 시선을\n" +
                   "끌던 Stage2Sign_low · Train 을 중심 아래(sy -0.46~-0.49)로 내려 두 캐릭터\n" +
                   "사이 빈 공간에 들어가게 했다. 로고 영역을 침범하던 Building_14 도 밖으로 나간다.\n" +
                   "캐릭터는 (±1.10, 0.03, -8.45) 에 세우는 것을 전제로 계산했다(화면 높이의 30.6%).",
            Position = new Vector3(0f, 1.15f, -14.78f),
            Euler = new Vector3(-13.2f, 0f, 0f),
            FieldOfView = 50f,
            Near = 0.3f,
            Far = 1000f,
            LumenPosition = DefaultLumenGuidePosition,
            EchoPosition = DefaultEchoGuidePosition,
        },
        new CameraPreset
        {
            // D 와 같은 장소를 정면에서 본 구도. 수치 근거는 배열 아래 주석.
            Name = "E · 중앙 정렬 정면 구도",
            Note = "도로의 소실점을 화면 정중앙에 놓은 구도. pitch·yaw 가 0 이므로 소실점이\n" +
                   "정확히 (0.000, 0.000) 에 오고, 건물·가로등의 수직 에지가 화면에서 그대로\n" +
                   "수직을 유지한다(D 의 올려보기에서는 위로 수렴했다).\n" +
                   "중앙 플랫폼(TrainFloor·고가철로·간판)이 sy -0.07~-0.09 로 올라와 화면 중심에 선다.\n" +
                   "캐릭터 높이 31.0% · 둘 사이 빈 공간 0.62 · 하단 도로 4% 는 D 와 같다.\n" +
                   "대신 Building_14 가 sy +0.72 로 올라오므로 로고는 상단 좌측~중앙에 둔다.",
            Position = new Vector3(0f, 2.55f, -14.33f),
            Euler = Vector3.zero,
            FieldOfView = 50f,
            Near = 0.3f,
            Far = 1000f,
            LumenPosition = DefaultLumenGuidePosition,
            EchoPosition = DefaultEchoGuidePosition,
        },
        new CameraPreset
        {
            // Unity Scene 뷰에서 손으로 잡아 확정한 구도. E 에서 카메라만 x -0.55 m 옮긴 것이다.
            Name = "F · 수동 중앙 정렬 (확정 후보)",
            Note = "E 를 기준으로 카메라를 왼쪽으로 0.55 m 옮긴 구도. yaw 가 0 이라 도로 소실점은\n" +
                   "여전히 sx 0.000 에 그대로 있고(소실점은 방향만으로 정해져 카메라를 옆으로\n" +
                   "옮겨도 움직이지 않는다), 배경만 오른쪽으로 밀려 두 건물 중점이\n" +
                   "sx -0.079 에서 -0.049 로 중앙에 더 가까워진다.\n" +
                   "캐릭터 가이드도 카메라와 같이 x -0.55 옮겨 둬서(루멘 -1.65 · 에코 0.55)\n" +
                   "화면에서 다시 좌우 ±0.401 대칭이고, Stage2Sign_low 도 두 캐릭터 사이로 돌아온다.",
            Position = new Vector3(-0.55f, 2.55f, -14.33f),
            Euler = Vector3.zero,
            FieldOfView = 50f,
            Near = 0.3f,
            Far = 1000f,

            // 카메라를 x -0.55 옮긴 만큼 캐릭터도 같이 옮긴다 — 그래야 화면에서 다시
            // 좌우 ±0.401 로 대칭이 되고, Stage2Sign_low 가 왼쪽 캐릭터 뒤에서 빠져나온다.
            LumenPosition = new Vector3(-1.65f, 0.03f, -8.45f),
            EchoPosition = new Vector3(0.55f, 0.03f, -8.45f),
        },
        new CameraPreset
        {
            // Unity 에서 직접 맞춰 확정한 전시 최종본. 이 프리셋만 공명 상태·포즈·로고까지
            // 함께 적용한다(AppliesSetup) — 전시 제출본은 버튼 한 번으로 똑같이 재현되어야 한다.
            Name = "G · 졸업전시 최종 대표 이미지",
            Note = "확정본. 누르면 구도뿐 아니라 공명 해제(0×) · 공명 포즈 ON · 로고 ON 까지 한 번에\n" +
                   "맞춘다. F 와 달리 캐릭터가 뒷모습이 아니라 서로 마주 보고 손바닥을 맞댄\n" +
                   "엔딩씬의 공명 포즈이고, 쌍의 중점이 카메라에서 5.4 m 라 F 보다 크게 잡힌다.\n" +
                   "계산값 — 캐릭터 높이 37.5% / 38.8%, 머리선 sy -0.01, 맞댄 손바닥 sy -0.20\n" +
                   "(가로 정중앙), 로고 sy +0.40 ~ +0.80 이라 로고와 캐릭터가 겹치지 않는다.",
            Position = new Vector3(-0.55f, 1.75f, -14.33f),
            Euler = Vector3.zero,
            FieldOfView = 45f,
            Near = 0.3f,
            Far = 1000f,

            // 가이드 사각형을 실제로 포즈가 서는 자리에 맞춘다 — closeGap 까지 적용한 뒤의
            // 발 위치다(쌍 중점 -8.95 기준). 그래야 가이드로 높이·겹침을 가늠할 수 있다.
            // (포즈는 측면이라 폭 0.5 m 가정은 참고용이다)
            LumenPosition = new Vector3(-0.90f, 0.03f, -8.86f),
            EchoPosition = new Vector3(-0.20f, 0.03f, -9.04f),

            AppliesSetup = true,
            Setup = new PresetSetup
            {
                FogState = ResonanceFogState.Released,   // 공명 해제 0× — 안개가 캐릭터를 덮지 않는다

                ShowPose = true,
                PosePairCenter = new Vector3(-0.55f, 0.03f, -8.95f),
                PosePairYaw = 0f,
                ApplyCloseGap = true,
                EvaluateRig = true,

                ShowLogo = true,
                LogoCenter = new Vector2(0.50f, 0.20f),
                LogoWidth = 0.60f,
                LogoOpacity = 1f,
            },
        },
    };

    // ── 프리셋 D 의 수치 근거 ─────────────────────────────────────────────
    //
    // 씬 YAML 실측값으로 투영을 계산해 잡은 값이다. 화면 좌표는 sx, sy 모두
    // -1(좌/하) ~ +1(우/상) 로 적는다.
    //
    // 실측 — 지면 y = 0.03 (Floor 와 두 스폰 포인트가 일치),
    //   거리 중심축 x ≈ 0 (가로등 3쌍의 중점), 스폰 Z선 z = -8.45,
    //   Stage2Sign_low (-2.56, 1.27, 16.96), Train (2.03, 1.88, 19.91) scale 1.4,
    //   TrainTrack_low 13개가 z = 19.80 · y = 1.46 에 x = -27.58 ~ +33.21,
    //   Building_07_city (-16.17, -0.37, 25.19), Building_14_city (13.38, 15.93, 25.54).
    //
    // 각 수치를 고른 이유 —
    //   z = -14.78  : 캐릭터와 6.33 m. FOV 50 에서 1.70 m 가 화면 높이 30.6% 가 되는 거리.
    //   y = 1.15    : 머리(1.73 m)보다 낮게 둬야 지면이 거의 안 보이고 철로 띠가 머리선
    //                 아래로 내려간다. 눈높이 1.6 m 로 올리면 띠가 sy -0.33 까지 올라와
    //                 머리선(-0.30)과 겹친다.
    //   pitch -13.2 : 발을 sy -0.91, 머리를 sy -0.30 에 놓는 각도. 이 둘을 정하면
    //                 pitch 는 자동으로 결정된다.
    //   FOV 50      : 좌측 Building_07(방위각 -22.03°)을 남기는 최소값. 45 면 sx -0.977 로
    //                 가장자리에 걸리고 40 이면 프레임 밖으로 나간다.
    //   yaw 0       : 거리 중심축이 x ≈ 0 이라 좌우 가로등 3쌍이 대칭으로 들어온다.
    //
    // 해결되지 않는 것 — 고가철로(60 m)와 Train(약 35 m)은 FOV 50 의 전체 폭보다
    // 넓어서 +Z 를 보는 어떤 구도에서도 화면을 가로지른다. 높이를 중심 아래로 내리는
    // 것까지가 한계다. 또 왼쪽의 높은 구조물(Construction_site_01_city 방위각 -49.4°,
    // Building_11_city -65.6°)은 어떤 FOV 로도 들어오지 않아, 좌우 균형은 가로등과
    // 거리 원근으로 잡는다.
    //
    // 추정 — 캐릭터 신장 1.70 m 가정(아바타는 VR HMD 구동이라 메시 높이를 YAML 로
    // 측정할 수 없다). 1.60 m 면 28.8%, 1.80 m 면 32.4% 로 목표 범위 28~35% 안에 남는다.
    // 건물 메시의 실루엣 높이는 피벗만 확인했으므로, Building_14 가 로고 영역을 침범하는지는
    // 미리보기로 확인해야 한다 — 로고는 중앙 또는 좌측 배치를 권한다.
    //
    // ── 프리셋 E 의 수치 근거 ─────────────────────────────────────────────
    //
    // +Z 로 뻗은 평행선(도로)의 소실점은 카메라가 +Z 를 보는 방향 그 자체이므로
    //
    //     소실점 sx = -tan(yaw)   / tan(FOV/2)
    //     소실점 sy =  tan(pitch) / tan(FOV/2)
    //
    // 로 닫힌 식이 나온다. D(pitch -13.2, yaw 0)는 sx 0.000 · sy -0.503 이었다 —
    // 거리 중심축이 x ≈ 0 이고 yaw = 0 이라 가로는 이미 맞아 있었고, pitch 만 문제였다.
    //
    // 각 수치를 고른 이유 —
    //   Rotation 0   : 소실점이 정확히 (0.000, 0.000). 덤으로 키스톤이 사라져 수직 에지가
    //                  화면에서 수직을 유지한다 — 도시 이미지의 "자연스러움"이 보통 이것이다.
    //   y = 2.55     : pitch 가 0 이면 캐릭터를 아래로 내리는 수단이 카메라 높이뿐이다.
    //                  cy = 1.73 + 0.30 * tan(FOV/2) * d 로 머리가 sy -0.30 에 온다.
    //   z = -14.33   : 캐릭터와 5.88 m. d = 1.7 / (0.62 * tan(FOV/2)) 로 높이 31.0%.
    //   FOV 50       : D 와 동일. FOV 를 바꿔도 아래의 Building_14 문제는 풀리지 않는다.
    //
    // 가로 정렬 교차 검증(yaw 0) — 도로 소실점 0.000, 가로등 쌍 +0.005 / +0.036,
    // 프레임 내 두 건물 중점 -0.079, 플랫폼+기차 중점 -0.146. 여러 기준이 전부 중앙
    // ±0.08 안에 모이므로 yaw 를 틀 이유가 없다(틀면 더 강한 단서인 도로가 어긋난다).
    //
    // 풀리지 않는 충돌 — 소실점을 중앙에 두면 수평선이 화면 중앙에 오고, 도시의 높은
    // 건물은 정의상 그 위로 솟는다. Building_14 의 고도각이 18.6° 로 고정이라 sy 0.333
    // 아래로 누르려면 FOV 90° 이상이 필요하다. pitch 로는 -11° 이하에서만 가능한데
    // 그때 소실점은 sy -0.43 으로 D 와 다를 바가 없다. 그래서 E 는 Building_14 를
    // 상단 우측 요소로 받아들이고 로고를 상단 좌측~중앙에 두는 쪽을 택했다.
    //
    // ── 프리셋 F 에서 캐릭터가 치우치는 이유 ──────────────────────────────
    //
    // F 는 E 에서 카메라만 x -0.55 m 옮긴 것이다. 소실점은 방향만으로 정해지므로
    // 그대로 sx 0.000 에 있지만, 실제 오브젝트는 전부 오른쪽으로 밀린다. 그 양은
    // 거리에 반비례하므로 가까운 캐릭터(5.88 m)가 먼 배경(31~40 m)보다 훨씬 크게 밀린다.
    //
    //   배경 두 건물 중점 : sx -0.079 -> -0.049   (중앙에 더 가까워짐 = 의도한 효과)
    //   캐릭터 좌         : sx -0.401 -> -0.201
    //   캐릭터 우         : sx +0.401 -> +0.602
    //   캐릭터 중점       : sx  0.000 -> +0.201   (오른쪽으로 치우침)
    //
    // 그래서 F 는 캐릭터 X 좌표도 카메라와 같이 -0.55 옮겼다 —
    // 루멘 (-1.65, 0.03, -8.45) · 에코 (0.55, 0.03, -8.45). 그 결과:
    //
    //   캐릭터 좌/우      : sx -0.401 / +0.401   (다시 대칭)
    //   두 캐릭터 빈 공간 : sx -0.310 ~ +0.310   (폭 0.620, D·E 와 같다)
    //   Stage2Sign_low    : sx -0.138  -> 빈 공간 안으로 돌아옴
    //   고가철로 / Train  : sx +0.035 / +0.162 -> 빈 공간 안
    //
    // A~E 는 기본 좌표(±1.10)를 그대로 쓴다. 가이드 좌표는 프리셋을 적용할 때
    // lumenGuidePosition / echoGuidePosition 으로 들어가고, 창의 readout 에 표시된다.

    // ── 공명 포즈 (엔딩씬 HandReach 최종 포즈) ────────────────────────────
    //
    // 엔딩씬(Stage3)의 "두 캐릭터가 마주 보고 손바닥을 맞대는" 포즈를 촬영 씬에서 재현한다.
    // 이 포즈는 애니메이션 클립이 아니라 Animation Rigging 의 TwoBoneIKConstraint 이고,
    // <c>Stage3EndingCutscene.ApplyHandReach(progress)</c> 가 IK 타깃을 MeetPose 로 끌어간다.
    //
    // progress = 1 에서 그 식이 무엇이 되는지 (Stage3EndingCutscene.cs:541-577) —
    //
    //     rise = close = turn = 1
    //     delta  = meetPose.position - restPosition
    //     up     = Vector3.Project(delta, Vector3.up)
    //     across = delta - up
    //     target.position = restPosition + up * 1 + across * 1   = meetPose.position
    //     target.rotation = Slerp(restRotation, meetPose.rotation, 1) = meetPose.rotation
    //
    // restPosition(애니메이션이 다리 옆에 내려 둔 손)이 완전히 상쇄된다. 그래서 최종 포즈는
    // MeetPose 하나로 정해지고, Animator 가 돌지 않는 Edit 모드에서도 손 위치·방향은
    // 런타임과 같은 값이 된다. 이것이 Edit 모드 재현을 가능하게 하는 핵심 사실이다.
    //
    // 반대로 몸통·다리는 Animator(Idle)가 만든다. Edit 모드에는 그 Animator 가 돌지 않으므로
    // 몸 자세는 FBX 바인드 포즈 그대로이고, 런타임의 Idle 과 다르다. 이번 테스트의 목적은
    // "손과 팔이 IK 로 실제로 움직이는가" 이므로 Idle 샘플링은 하지 않는다 — 미검증 사항.

    private const string LumenPosePrefabPath = "Assets/ReBloom/Prefabs/Characters/Player_mental.prefab";
    private const string EchoPosePrefabPath = "Assets/ReBloom/Prefabs/Characters/Player_ear.prefab";

    /// <summary>
    /// 엔딩씬에서 실측한 값. Stage3.unity 의 <c>Stage3Ending / Actors</c> 는 회전 0 · 스케일 1 이라
    /// 아래 로컬값이 곧 월드값이다(부모 체인 확인). 상대 배치를 그대로 옮기기 위해 쌍 전체를
    /// 하나의 강체로 보고 <see cref="MapPosePoint"/> 로 변환한다.
    /// </summary>
    private struct PoseActorSpec
    {
        public string Label;
        public string PrefabPath;

        /// <summary>손을 올리는 팔의 IK. 루멘은 왼손, 에코는 오른손이다(엔딩씬과 동일).</summary>
        public string HandIkName;

        public Vector3 Stage3Position;
        public Quaternion Stage3Rotation;

        /// <summary>손바닥이 닿는 순간의 손 위치·방향 = <c>MeetPose_*</c>.</summary>
        public Vector3 Stage3MeetPosition;
        public Quaternion Stage3MeetRotation;
    }

    private static readonly PoseActorSpec[] PoseActors =
    {
        new PoseActorSpec
        {
            Label = "루멘",
            PrefabPath = LumenPosePrefabPath,
            HandIkName = "LeftHandIK",
            // Ending_mental — yaw +90, 즉 +X 를 바라본다
            Stage3Position = new Vector3(2.11f, 40.031f, 100.706f),
            Stage3Rotation = new Quaternion(0f, 0.7071068f, 0f, 0.7071068f),
            // MeetPose_mental
            Stage3MeetPosition = new Vector3(2.4575953f, 41.290012f, 100.698906f),
            Stage3MeetRotation = new Quaternion(0.0043694032f, -0.6306124f, -0.0023524654f, 0.77608216f),
        },
        new PoseActorSpec
        {
            Label = "에코",
            PrefabPath = EchoPosePrefabPath,
            HandIkName = "RightHandIK",
            // Ending_ear — yaw -90, 즉 -X 를 바라본다. 둘이 서로 마주 본다.
            Stage3Position = new Vector3(2.902f, 40.031002f, 100.501f),
            Stage3Rotation = new Quaternion(0f, -0.7071068f, 0f, 0.7071068f),
            // MeetPose_ear
            Stage3MeetPosition = new Vector3(2.5544047f, 41.290012f, 100.67385f),
            Stage3MeetRotation = new Quaternion(-0.0048455987f, -0.8049944f, -0.001162831f, -0.5932614f),
        },
    };

    // 엔딩씬 Stage3EndingCutscene 의 직렬화값. 같은 포즈가 나오도록 그대로 쓴다.
    private const float EndingCloseGap = 0.1f;          // 두 사람을 서로에게 0.05 m 씩 당긴다
    private const float EndingElbowDrop = 0.35f;        // AimElbow — 힌트를 아래로
    private const float EndingElbowOut = 0.2f;          // AimElbow — 힌트를 바깥으로
    private const float EndingHandRotationWeight = 1f;  // 손바닥이 정확히 마주 보게 한다

    /// <summary>IK 가 실제로 풀렸는지 판정하는 기준. 손끝이 목표에서 이보다 멀면 안 움직인 것이다.</summary>
    private const float PoseSolvedTolerance = 0.02f;

    /// <summary>
    /// 스킨 루트 본의 이름. 7개/6개 <c>SkinnedMeshRenderer</c> 가 모두 이 본을 <c>m_RootBone</c> 으로
    /// 쓰고 <c>m_UpdateWhenOffscreen: 0</c> 이므로, 컬링 Bounds 는 이 본의 월드 행렬로 계산된다.
    /// 그래서 스켈레톤 붕괴를 가장 먼저 드러내는 본이다(프리팹 YAML 에서 확인).
    /// </summary>
    private const string PoseSkinRootBoneName = "root.x";

    /// <summary>Bounds 크기가 이보다 작으면 붕괴로 본다. 사람 메시는 어느 축이든 이보다 크다.</summary>
    private const float PoseCollapsedBoundsSize = 0.01f;

    // ── 조정값 (창에 남는다) ──────────────────────────────────────────────
    [SerializeField] private Vector3 cameraPosition = new Vector3(1.5f, 1.6f, -8.5f);
    [SerializeField] private Vector3 cameraEuler = Vector3.zero;
    [SerializeField] private float fieldOfView = 60f;
    [SerializeField] private float nearClip = 0.3f;
    [SerializeField] private float farClip = 1000f;

    [SerializeField] private bool enableWeather = true;
    [SerializeField] private float weatherWarmupSeconds = 3f;
    [SerializeField] private bool hideCanvas = true;
    [SerializeField] private bool hideOutlines = true;

    [SerializeField] private ResonanceFogState resonanceFogState = ResonanceFogState.MaxConstraint;

    /// <summary>지금 쓰는 캐릭터 가이드 좌표. 프리셋을 적용할 때 그 프리셋 값으로 바뀐다.</summary>
    [SerializeField] private Vector3 lumenGuidePosition = new Vector3(-1.10f, 0.03f, -8.45f);
    [SerializeField] private Vector3 echoGuidePosition = new Vector3(1.10f, 0.03f, -8.45f);

    [SerializeField] private bool showGuides = true;
    [SerializeField] private bool showCharacterGuides = true;
    [SerializeField] private bool autoPreview;

    // ── 공명 포즈 테스트 ──────────────────────────────────────────────────

    /// <summary>켜면 렌더 직전에 실제 모델을 임시로 만들어 공명 포즈를 입힌다. 렌더가 끝나면 지운다.</summary>
    [SerializeField] private bool showResonancePose;

    /// <summary>두 캐릭터 쌍의 중점(발밑). 엔딩씬의 상대 배치는 유지하고 이 점만 옮긴다.</summary>
    [SerializeField] private Vector3 posePairCenter = new Vector3(-0.55f, 0.03f, -8.45f);

    /// <summary>쌍 전체를 수평으로 돌린다. 0 이면 엔딩씬과 같은 방향(루멘이 +X 를 본다).</summary>
    [SerializeField] private float posePairYaw;

    /// <summary><c>Stage3EndingCutscene.StepActorsCloser</c> 를 적용할지. 엔딩은 켜고 쓴다.</summary>
    [SerializeField] private bool poseApplyCloseGap = true;

    /// <summary>
    /// <c>RigBuilder.Build()</c> + <c>Evaluate(0)</c> 로 IK 를 실제로 풀지.
    /// 끄면 타깃·힌트만 놓이고 팔은 움직이지 않는다 — A안이 Edit 모드에서 듣지 않을 때
    /// "타깃 배치는 맞는데 해석만 안 되는 것"인지 가려내는 데 쓴다.
    /// </summary>
    [SerializeField] private bool poseEvaluateRig = true;

    /// <summary>
    /// 켜면 <c>Build()</c> 이전 · <c>Build()</c> 직후 · <c>Evaluate(0f)</c> 직후의 본과 Bounds 를
    /// Console 에 찍는다. 값을 읽기만 하므로 포즈 결과와 렌더 결과는 달라지지 않는다.
    /// </summary>
    [SerializeField] private bool poseDiagnostics = true;

    // ── 로고 합성 ─────────────────────────────────────────────────────────

    /// <summary>끄면 합성 코드가 즉시 빠져나가므로 결과가 기존 렌더와 한 픽셀도 다르지 않다.</summary>
    [SerializeField] private bool showLogo = true;

    /// <summary>
    /// 로고 중심. 이미지 크기에 대한 비율이고 <b>좌상단이 (0, 0)</b> 이다(y 가 아래로 자란다).
    /// 기본값은 상단 중앙 — 캐릭터 머리선과 맞댄 손바닥보다 한참 위다.
    /// </summary>
    [SerializeField] private Vector2 logoCenter = new Vector2(0.5f, 0.20f);

    /// <summary>로고 가로 폭. 이미지 가로에 대한 비율. 세로는 원본 비율로 유도한다.</summary>
    [SerializeField] private float logoWidth = 0.60f;

    [SerializeField] private float logoOpacity = 1f;

    // ── 레터링 배경 (가로형) ──────────────────────────────────────────────
    //
    // 전시용 가로 레터링 이미지(최종 8000×1920)의 배경 구도를 잡는 전용 설정이다.
    // 대표 이미지와 카메라를 공유하지 않는다 — 비율이 4.1667:1 이라 필요한 화각 자체가 다르고,
    // 프리셋 A~G 를 건드리지 않으려면 따로 들고 있는 편이 안전하다.
    //
    // 왜 이 수치인가 (실측 기반) —
    //   세로 FOV 27° -> 가로 90.0°. 가로를 더 벌리면 가장자리 왜곡이 커지고, 무엇보다
    //   왼쪽 지면이 일찍 끊긴다 — 씬의 바닥은 Floor(x -35~35) 와 Floor (1)(x 32.5~102.5)
    //   둘뿐이어서 x < -35 에는 바닥이 없다. 가로 90° 면 좌측 지면이 깊이 35.6 m 까지
    //   버티고, 120° 면 20.5 m 에서 끊겨 좌하단에 빈 공간이 드러난다.
    //   pitch -2° 는 그 지면 경계를 프레임 아래로 밀어 가리기 위한 값이다.
    //
    // 미리보기 2000×480 은 최종 8000×1920 과 비율이 정확히 같다(둘 다 4.16667:1).
    // 그래서 이 미리보기에서 잡은 구도가 최종에서 그대로 나온다.

    private const int LetteringPreviewWidth = 2000;
    private const int LetteringPreviewHeight = 480;

    /// <summary>최종 출력 크기. 이 단계에서는 비율·화각 계산에만 쓰고 저장은 하지 않는다.</summary>
    private const int LetteringFinalWidth = 8000;
    private const int LetteringFinalHeight = 1920;

    [SerializeField] private Vector3 letteringCameraPosition = new Vector3(-0.55f, 1.75f, -14.33f);
    [SerializeField] private Vector3 letteringCameraEuler = new Vector3(-2f, 0f, 0f);
    [SerializeField] private float letteringFieldOfView = 27f;

    /// <summary>
    /// 레터링 전용 안개. 초기값은 최대 제약(1.0×) — 대표 이미지와 달리 배경을 어둡고 흐릿하게
    /// 가져가는 것이 목적이므로 안개를 켜는 쪽이 유리하다.
    /// </summary>
    [SerializeField] private ResonanceFogState letteringFogState = ResonanceFogState.MaxConstraint;

    /// <summary>
    /// 지금 미리보기가 레터링 것인가. 구도 가이드는 1:1 대표 이미지에 맞춰 계산되어 있어
    /// 가로형에서는 맞지 않으므로, 레터링 미리보기에서는 가이드를 그리지 않는다.
    /// </summary>
    private bool previewIsLettering;

    /// <summary>합성 결과 메시지. <see cref="CompositeLogo"/> 가 채우고 창에 보여 준다.</summary>
    private string logoReport = string.Empty;
    private MessageType logoReportType = MessageType.Info;

    /// <summary>
    /// 마지막으로 읽은 원본의 세로/가로. 창의 readout 이 렌더 전에도 크기를 보여 주려고 쓴다.
    /// 아직 한 번도 못 읽었으면 0 이고, 그때는 readout 이 실측 상수로 적는다.
    /// </summary>
    [SerializeField] private float logoSourceAspect;

    /// <summary>포즈 적용 결과. <see cref="Render"/> 가 채우고 창에 그대로 보여 준다.</summary>
    private string poseReport = string.Empty;

    private MessageType poseReportType = MessageType.Info;

    /// <summary>프리팹에서 읽은 공명 값. <see cref="OnEnable"/> 에서 한 번 읽는다.</summary>
    private FogConstraintValues gameFogValues;

    private Texture2D preview;
    private string status = string.Empty;
    private MessageType statusType = MessageType.Info;
    private Vector2 scroll;

    // ───────────────────────────── 메뉴 ─────────────────────────────

    [MenuItem("Re_Bloom/KeyArt/Stage1 Key Art Capture", priority = 520)]
    private static void Open()
    {
        var window = GetWindow<Stage1KeyArtCapture>("Stage1 KeyArt");
        window.minSize = new Vector2(540f, 720f);
        window.Show();
    }

    private void OnEnable() => gameFogValues = ReadFogConstraintValues();

    /// <summary>
    /// 공명 상태를 파일 이름에 넣는다 — 세 상태를 나란히 비교하려면 서로 덮어쓰지 않아야 한다.
    /// 이름은 배율에서 뽑으므로 프리팹 값이 바뀌면 이름도 따라간다
    /// (1.0× → <c>_Fog100</c>, 0.6× → <c>_Fog060</c>, 0× → <c>_Fog000</c>).
    /// </summary>
    private string CurrentOutputPath
    {
        get
        {
            int percent = Mathf.RoundToInt(MultiplierOf(resonanceFogState, gameFogValues) * 100f);
            return $"{OutputFolder}/{OutputName}_Fog{percent:D3}.png";
        }
    }

    private void OnDisable() => DestroyPreview();

    // ───────────────────────────── 창 ─────────────────────────────

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        DrawSceneSection();
        EditorGUILayout.Space(6f);
        DrawPresetSection();
        EditorGUILayout.Space(6f);
        DrawCameraSection();
        EditorGUILayout.Space(6f);
        DrawResonanceSection();
        EditorGUILayout.Space(6f);
        DrawResonancePoseSection();
        EditorGUILayout.Space(6f);
        DrawLogoSection();
        EditorGUILayout.Space(6f);
        DrawLetteringSection();
        EditorGUILayout.Space(6f);
        DrawOptionSection();
        EditorGUILayout.Space(8f);
        DrawPreviewSection();
        EditorGUILayout.Space(8f);
        DrawCaptureSection();

        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(status, statusType);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawSceneSection()
    {
        EditorGUILayout.LabelField("촬영 씬", EditorStyles.boldLabel);

        bool ready = TryGetScene(out Scene scene);

        if (ready)
        {
            EditorGUILayout.HelpBox(
                $"'{SceneName}' 가 열려 있습니다.\n" +
                "Stage1.unity 원본은 건드리지 않습니다.",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                $"'{SceneName}' 씬이 열려 있지 않습니다.\n" +
                "아래 버튼으로 열거나 직접 여세요.",
                MessageType.Warning);

            if (GUILayout.Button("촬영 씬 열기"))
                OpenCaptureScene();
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorGUILayout.HelpBox(
                "Play 모드입니다. 이 도구는 Edit 모드를 전제로 만들어졌습니다 — " +
                "Play 를 끄고 쓰세요(비는 도구가 직접 시뮬레이션합니다).",
                MessageType.Warning);
        }
    }

    private void DrawPresetSection()
    {
        EditorGUILayout.LabelField("구도 프리셋", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "씬의 건물 좌표로 화각을 계산한 출발점입니다. 실제로 어떻게 보이는지는 " +
            "미리보기로 확인하고 손으로 다듬으세요.",
            MessageType.None);

        for (int i = 0; i < Presets.Length; i++)
        {
            CameraPreset preset = Presets[i];

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(preset.Name, EditorStyles.boldLabel);

                    if (GUILayout.Button("적용", GUILayout.Width(60f)))
                        ApplyPreset(preset);
                }

                EditorGUILayout.LabelField(preset.Note, EditorStyles.wordWrappedMiniLabel);

                EditorGUILayout.LabelField(
                    $"pos {preset.Position}   rot {preset.Euler}   FOV {preset.FieldOfView:F0}   " +
                    $"near {preset.Near:0.##} / far {preset.Far:0.##}",
                    EditorStyles.miniLabel);
            }
        }
    }

    private void ApplyPreset(CameraPreset preset)
    {
        cameraPosition = preset.Position;
        cameraEuler = preset.Euler;
        fieldOfView = preset.FieldOfView;
        nearClip = preset.Near;
        farClip = preset.Far;
        lumenGuidePosition = preset.LumenPosition;
        echoGuidePosition = preset.EchoPosition;

        // A~F 는 여기서 끝난다 — 구도만 바뀌고 공명 상태·포즈·로고는 맞춰 둔 그대로 남는다.
        if (!preset.AppliesSetup)
        {
            SetStatus($"'{preset.Name}' 을 적용했습니다. 미리보기로 확인하세요.", MessageType.Info);
            Repaint();

            if (autoPreview)
                RefreshPreview();

            return;
        }

        PresetSetup setup = preset.Setup;

        resonanceFogState = setup.FogState;

        showResonancePose = setup.ShowPose;
        posePairCenter = setup.PosePairCenter;
        posePairYaw = setup.PosePairYaw;
        poseApplyCloseGap = setup.ApplyCloseGap;
        poseEvaluateRig = setup.EvaluateRig;

        showLogo = setup.ShowLogo;
        logoCenter = setup.LogoCenter;
        logoWidth = setup.LogoWidth;
        logoOpacity = setup.LogoOpacity;

        SetStatus(
            $"'{preset.Name}' 을 적용했습니다 — 구도 · 공명 {ShortNameOf(setup.FogState)} · " +
            $"공명 포즈 {(setup.ShowPose ? "ON" : "OFF")} · 로고 {(setup.ShowLogo ? "ON" : "OFF")} 를 " +
            "모두 맞췄습니다. 미리보기로 확인하세요.",
            MessageType.Info);

        Repaint();

        if (autoPreview)
            RefreshPreview();
    }

    private void DrawCameraSection()
    {
        EditorGUILayout.LabelField("카메라 구도", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Scene 뷰 구도 가져오기"))
                PullFromSceneView();

            if (GUILayout.Button("Scene 뷰를 이 구도로 보내기"))
                PushToSceneView();
        }

        EditorGUI.BeginChangeCheck();

        cameraPosition = EditorGUILayout.Vector3Field("위치", cameraPosition);
        cameraEuler = EditorGUILayout.Vector3Field("회전 (Euler)", cameraEuler);
        fieldOfView = EditorGUILayout.Slider("시야각 (FOV)", fieldOfView, 20f, 90f);

        using (new EditorGUILayout.HorizontalScope())
        {
            nearClip = EditorGUILayout.FloatField("Near", nearClip);
            farClip = EditorGUILayout.FloatField("Far", farClip);
        }

        bool changed = EditorGUI.EndChangeCheck();

        EditorGUILayout.HelpBox(
            "1:1 은 16:9 보다 세로가 훨씬 많이 보입니다. Scene 뷰에서 잡은 구도와 결과가 다르므로 " +
            "반드시 미리보기로 확인하세요. 왜곡이 거슬리면 FOV 를 45~50 으로 낮춰 보세요.",
            MessageType.None);

        if (changed && autoPreview)
            RefreshPreview();
    }

    /// <summary>
    /// 공명 상태 고르기. 숫자는 전부 <c>FogConstraint</c> 의 식과
    /// <c>ResonanceSystem.prefab</c> 의 값에서 계산한 것이고, 여기서 임의로 정하지 않는다.
    /// </summary>
    private void DrawResonanceSection()
    {
        EditorGUILayout.LabelField("공명 상태 (안개 강도)", EditorStyles.boldLabel);

        EditorGUILayout.HelpBox(
            "이 게임의 안개는 공명 제약 시스템이 거리에 따라 조절합니다(FogConstraint.Tick). " +
            "Edit 모드에는 그 로직이 없어서 그냥 찍으면 항상 '최대 제약' — 두 플레이어가 " +
            "가장 멀리 떨어진 화면입니다. 아래에서 게임에 실제로 존재하는 상태를 고르세요.",
            MessageType.None);

        EditorGUI.BeginChangeCheck();

        resonanceFogState = (ResonanceFogState)EditorGUILayout.Popup(
            "상태", (int)resonanceFogState, BuildStateLabels(gameFogValues));

        bool changed = EditorGUI.EndChangeCheck();

        float multiplier = MultiplierOf(resonanceFogState, gameFogValues);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(DescriptionOf(resonanceFogState),
                EditorStyles.wordWrappedMiniLabel);

            if (TryGetFogSettings(out FlatKit.FogSettings settings))
            {
                EditorGUILayout.LabelField(
                    $"거리 안개 {settings.distanceFogIntensity:F3} → " +
                    $"{settings.distanceFogIntensity * multiplier:F3}      " +
                    $"높이 안개 {settings.heightFogIntensity:F3} → " +
                    $"{settings.heightFogIntensity * multiplier:F3}",
                    EditorStyles.miniLabel);
            }

            EditorGUILayout.LabelField(
                gameFogValues.FromPrefab
                    ? $"출처 — ResonanceSystem.prefab (activeIntensity {gameFogValues.ActiveIntensity:F2}, " +
                      $"maxConstraintRelief {gameFogValues.MaxConstraintRelief:F2})"
                    : $"⚠ 프리팹을 못 읽어 폴백값을 씁니다 (activeIntensity {gameFogValues.ActiveIntensity:F2}, " +
                      $"maxConstraintRelief {gameFogValues.MaxConstraintRelief:F2})",
                EditorStyles.miniLabel);
        }

        if (resonanceFogState == ResonanceFogState.Relieved && FogEffectMaterialField == null)
        {
            EditorGUILayout.HelpBox(
                "FlatKit 의 내부 머티리얼 필드(_effectMaterial)를 찾지 못했습니다 — FlatKit 버전이 바뀐 것 같습니다.\n" +
                "임시 복제본을 끼워 넣을 수 없어, 촬영 중에만 공유 Effect Material 의 강도를 바꾸고 " +
                "즉시 되돌리는 방식으로 넘어갑니다. 되돌리기는 finally 에서 보장되지만, " +
                "촬영 중 Unity 가 비정상 종료되면 바뀐 값이 남을 수 있습니다.",
                MessageType.Warning);
        }

        if (changed && autoPreview)
            RefreshPreview();
    }

    private static string[] BuildStateLabels(FogConstraintValues values) => new[]
    {
        $"최대 제약 — {MultiplierOf(ResonanceFogState.MaxConstraint, values):0.##}×",
        $"근접 완화 — {MultiplierOf(ResonanceFogState.Relieved, values):0.##}×",
        $"공명 해제 — {MultiplierOf(ResonanceFogState.Released, values):0.##}×",
    };

    /// <summary>상태 표시줄에 넣을 짧은 이름. 긴 설명은 <see cref="DescriptionOf"/> 가 따로 있다.</summary>
    private static string ShortNameOf(ResonanceFogState state) => state switch
    {
        ResonanceFogState.MaxConstraint => "최대 제약 (1.0×)",
        ResonanceFogState.Relieved => "근접 완화 (0.6×)",
        ResonanceFogState.Released => "해제 (0×)",
        _ => state.ToString(),
    };

    private static string DescriptionOf(ResonanceFogState state) => state switch
    {
        ResonanceFogState.MaxConstraint =>
            "두 플레이어가 공명 거리 밖에 있는 상태(proximity = 0). 에셋에 저장된 값 그대로이므로 " +
            "도구가 아무것도 건드리지 않습니다 — 가장 안전합니다.",

        ResonanceFogState.Relieved =>
            "루멘과 에코가 최대로 붙어 있는 순간(proximity = 1). 협동이 성립한 장면이라 " +
            "폐허의 분위기를 남기면서 건물 실루엣이 살아납니다.\n" +
            "촬영 중에만 쓰는 임시 머티리얼 복제본에 강도를 적용합니다. 공유 에셋은 읽기만 합니다.",

        ResonanceFogState.Released =>
            "협동 활성화 성공 이후(공명 해제). 안개가 완전히 사라집니다.\n" +
            "게임이 타이틀 배경에서 쓰는 것과 같은 방식으로 FlatKitFog Renderer Feature 를 " +
            "잠시 끕니다(FogRendererFeatureSceneController 와 동일). 수치는 건드리지 않습니다.",

        _ => string.Empty,
    };

    /// <summary>
    /// 엔딩씬의 공명 포즈를 촬영 씬에서 재현해 보는 테스트. 끄면 아무것도 만들지 않는다.
    /// </summary>
    private void DrawResonancePoseSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("공명 포즈 (엔딩씬 HandReach)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            showResonancePose = GUILayout.Toggle(
                showResonancePose, showResonancePose ? "ON" : "OFF",
                EditorStyles.miniButton, GUILayout.Width(44f));

            if (EditorGUI.EndChangeCheck() && autoPreview)
                RefreshPreview();
        }

        if (!showResonancePose)
        {
            EditorGUILayout.LabelField(
                "꺼져 있습니다 — 씬에는 아무것도 만들지 않고 캐릭터 가이드(사각형)만 그립니다.",
                EditorStyles.miniLabel);
            return;
        }

        EditorGUI.BeginChangeCheck();

        posePairCenter = EditorGUILayout.Vector3Field("쌍의 중점 (발밑)", posePairCenter);
        posePairYaw = EditorGUILayout.Slider("쌍 회전 (Yaw)", posePairYaw, -180f, 180f);
        poseApplyCloseGap = EditorGUILayout.Toggle(
            $"closeGap {EndingCloseGap:0.##} 적용", poseApplyCloseGap);
        poseEvaluateRig = EditorGUILayout.Toggle("RigBuilder 로 IK 풀기", poseEvaluateRig);
        poseDiagnostics = EditorGUILayout.Toggle("진단 로그 (Console)", poseDiagnostics);

        if (EditorGUI.EndChangeCheck() && autoPreview)
            RefreshPreview();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();

            if (GUILayout.Button("프리셋 F 중점으로", EditorStyles.miniButton))
            {
                posePairCenter = new Vector3(-0.55f, 0.03f, -8.45f);
                posePairYaw = 0f;
            }

            if (GUILayout.Button("캐릭터 가이드 중점으로", EditorStyles.miniButton))
            {
                posePairCenter = (lumenGuidePosition + echoGuidePosition) * 0.5f;
                posePairYaw = 0f;
            }

            if (EditorGUI.EndChangeCheck() && autoPreview)
                RefreshPreview();
        }

        EditorGUILayout.LabelField(
            $"엔딩씬 간격 {Vector3.Distance(PoseActors[0].Stage3Position, PoseActors[1].Stage3Position):0.000} m" +
            (poseApplyCloseGap ? $" → closeGap 적용 후 {PosePairGap:0.000} m" : string.Empty) +
            $"      손바닥 높이 발밑 +{(PoseActors[0].Stage3MeetPosition.y - PoseActors[0].Stage3Position.y):0.000} m",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            "미리보기를 갱신할 때만 실제 모델을 임시로 만들고, 렌더가 끝나면 바로 지웁니다. " +
            "원본 씬·프리팹·애니메이션은 건드리지 않습니다.",
            EditorStyles.miniLabel);

        if (!string.IsNullOrEmpty(poseReport))
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(poseReport, poseReportType);
        }
    }

    /// <summary>
    /// 로고 합성 설정. 수치는 모두 이미지 크기에 대한 비율이라 1024 미리보기와 2048 최종이
    /// 같은 구도로 나온다.
    /// </summary>
    private void DrawLogoSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("로고 합성 (ReBloom_Logo)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            showLogo = GUILayout.Toggle(
                showLogo, showLogo ? "ON" : "OFF", EditorStyles.miniButton, GUILayout.Width(44f));

            if (EditorGUI.EndChangeCheck() && autoPreview)
                RefreshPreview();
        }

        if (!showLogo)
        {
            EditorGUILayout.LabelField(
                "꺼져 있습니다 — 렌더 결과가 로고 기능을 넣기 전과 한 픽셀도 다르지 않습니다.",
                EditorStyles.miniLabel);
            return;
        }

        EditorGUI.BeginChangeCheck();

        logoCenter.x = EditorGUILayout.Slider("X 위치 (좌 0 ~ 우 1)", logoCenter.x, 0f, 1f);
        logoCenter.y = EditorGUILayout.Slider("Y 위치 (상 0 ~ 하 1)", logoCenter.y, 0f, 1f);
        logoWidth = EditorGUILayout.Slider("크기 (가로 폭 비율)", logoWidth, 0.05f, 1f);
        logoOpacity = EditorGUILayout.Slider("불투명도", logoOpacity, 0f, 1f);

        if (EditorGUI.EndChangeCheck() && autoPreview)
            RefreshPreview();

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();

            if (GUILayout.Button("상단 중앙 기본값", EditorStyles.miniButton))
            {
                logoCenter = new Vector2(0.5f, 0.20f);
                logoWidth = 0.60f;
                logoOpacity = 1f;
            }

            if (GUILayout.Button("가로 중앙 맞추기", EditorStyles.miniButton))
                logoCenter.x = 0.5f;

            if (EditorGUI.EndChangeCheck() && autoPreview)
                RefreshPreview();
        }

        DrawLogoReadout();

        if (!string.IsNullOrEmpty(logoReport))
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.HelpBox(logoReport, logoReportType);
        }
    }

    /// <summary>
    /// 로고가 차지하는 화면 범위를 숫자로 보여 주고, 캐릭터와 겹치면 경고한다.
    /// </summary>
    private void DrawLogoReadout()
    {
        // 세로 비율은 원본에서 온다. 파일을 아직 못 읽었으면 실측값(2172×724 = 1/3)으로 적는다.
        float aspect = logoSourceAspect > 0f ? logoSourceAspect : 724f / 2172f;
        float height = logoWidth * aspect;

        // 좌상단 원점 비율 -> sy(-1 아래 ~ +1 위)
        float syTop = 1f - 2f * (logoCenter.y - height * 0.5f);
        float syBottom = 1f - 2f * (logoCenter.y + height * 0.5f);
        float sxLeft = 2f * (logoCenter.x - logoWidth * 0.5f) - 1f;
        float sxRight = 2f * (logoCenter.x + logoWidth * 0.5f) - 1f;

        EditorGUILayout.LabelField(
            $"로고 {Mathf.RoundToInt(logoWidth * CaptureSize)}×{Mathf.RoundToInt(height * CaptureSize)} px " +
            $"(2048 기준)      sx {sxLeft:F2} ~ {sxRight:F2}      sy {syBottom:F2} ~ {syTop:F2}",
            EditorStyles.miniLabel);

        // 가리면 안 되는 것 두 가지 — 캐릭터 머리선과 맞댄 손바닥.
        float headSy = float.NaN;
        float palmSy = float.NaN;

        if (TryGetGuideQuad(lumenGuidePosition, out Vector2[] guide))
            headSy = guide[3].y;

        if (showResonancePose &&
            TryProject(MapPosePoint(PoseActors[0].Stage3MeetPosition), out Vector2 palm))
            palmSy = palm.y;

        var parts = new List<string>(2);

        if (!float.IsNaN(headSy))
            parts.Add($"캐릭터 가이드 머리선 sy {headSy:F2}");

        if (!float.IsNaN(palmSy))
            parts.Add($"맞댄 손바닥 sy {palmSy:F2}");

        if (parts.Count > 0)
            EditorGUILayout.LabelField(string.Join("      ", parts), EditorStyles.miniLabel);

        // 로고 아래끝이 머리선/손바닥보다 내려오면 겹친다.
        float lowest = float.NaN;
        string lowestName = string.Empty;

        if (!float.IsNaN(headSy))
        {
            lowest = headSy;
            lowestName = "캐릭터 머리";
        }

        if (!float.IsNaN(palmSy) && (float.IsNaN(lowest) || palmSy > lowest))
        {
            lowest = palmSy;
            lowestName = "맞댄 손바닥";
        }

        if (!float.IsNaN(lowest) && syBottom < lowest)
        {
            EditorGUILayout.HelpBox(
                $"로고 아래끝(sy {syBottom:F2})이 {lowestName}(sy {lowest:F2})보다 내려옵니다 — " +
                "Y 위치를 올리거나 크기를 줄이세요.",
                MessageType.Warning);
        }

        EditorGUILayout.LabelField(
            "원본 PNG 를 직접 읽어 쓰므로 에셋과 Import Settings 는 건드리지 않습니다. " +
            "세로 크기는 원본 비율로 자동 계산됩니다.",
            EditorStyles.miniLabel);
    }

    /// <summary>
    /// 레터링 배경(가로형) 전용 설정. 여기 값은 프리셋 A~G 와 창의 구도 값에 영향을 주지 않는다.
    /// </summary>
    private void DrawLetteringSection()
    {
        EditorGUILayout.LabelField(
            $"레터링 배경 (가로형 {LetteringFinalWidth}×{LetteringFinalHeight})", EditorStyles.boldLabel);

        letteringCameraPosition = EditorGUILayout.Vector3Field("카메라 Position", letteringCameraPosition);
        letteringCameraEuler = EditorGUILayout.Vector3Field("카메라 Rotation", letteringCameraEuler);
        letteringFieldOfView = EditorGUILayout.Slider("세로 FOV", letteringFieldOfView, 10f, 60f);

        letteringFogState = (ResonanceFogState)EditorGUILayout.Popup(
            "공명 안개", (int)letteringFogState, BuildStateLabels(gameFogValues));

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button(
                    $"레터링 배경 미리보기 ({LetteringPreviewWidth}×{LetteringPreviewHeight})",
                    GUILayout.Height(26f)))
            {
                RefreshLetteringPreview();
            }

            if (GUILayout.Button("초기값으로", EditorStyles.miniButton, GUILayout.Width(80f)))
            {
                letteringCameraPosition = new Vector3(-0.55f, 1.75f, -14.33f);
                letteringCameraEuler = new Vector3(-2f, 0f, 0f);
                letteringFieldOfView = 27f;
                letteringFogState = ResonanceFogState.MaxConstraint;
            }
        }

        DrawLetteringReadout();
    }

    /// <summary>
    /// 가로 화각과 좌측 지면이 끊기는 깊이를 보여 준다 — 이 둘이 가로형 구도의 두 제약이다.
    /// </summary>
    private void DrawLetteringReadout()
    {
        float aspect = (float)LetteringFinalWidth / LetteringFinalHeight;
        float halfTan = Mathf.Tan(letteringFieldOfView * 0.5f * Mathf.Deg2Rad);
        float horizontal = 2f * Mathf.Atan(halfTan * aspect) * Mathf.Rad2Deg;

        EditorGUILayout.LabelField(
            $"비율 {aspect:0.0000} : 1      세로 FOV {letteringFieldOfView:0.#}° → " +
            $"가로 FOV {horizontal:0.#}° (가장자리 ±{horizontal * 0.5f:0.#}°)",
            EditorStyles.miniLabel);

        // 씬의 바닥은 Floor(x -35~35) 와 Floor (1)(x 32.5~102.5) 뿐이다 — x < -35 에는 바닥이 없다.
        // 프레임 왼쪽 끝이 x = -35 를 넘어가는 깊이가 곧 좌하단에 빈 공간이 드러나는 지점이다.
        const float floorLeftEdge = -35f;
        float halfTanHorizontal = halfTan * aspect;

        if (halfTanHorizontal > 0.0001f)
        {
            float depth = (letteringCameraPosition.x - floorLeftEdge) / halfTanHorizontal;

            EditorGUILayout.LabelField(
                $"좌측 지면(Floor x = {floorLeftEdge:0.#})이 깊이 {depth:0.0} m " +
                $"(z = {letteringCameraPosition.z + depth:0.0}) 에서 끝납니다 — " +
                "그보다 먼 좌하단에는 바닥이 없습니다.",
                EditorStyles.miniLabel);

            if (horizontal > 100f)
            {
                EditorGUILayout.HelpBox(
                    $"가로 FOV {horizontal:0.#}° 는 가장자리 왜곡이 크고 좌측 지면도 일찍 끊깁니다. " +
                    "세로 FOV 를 27° 부근으로 두면 가로 90° 가 됩니다.",
                    MessageType.Warning);
            }
        }

        EditorGUILayout.LabelField(
            $"미리보기 {LetteringPreviewWidth}×{LetteringPreviewHeight} 는 최종과 비율이 같습니다. " +
            "이 단계에서는 캐릭터·로고·블러·어둡게 처리를 넣지 않습니다.",
            EditorStyles.miniLabel);
    }

    private void DrawOptionSection()
    {
        EditorGUILayout.LabelField("촬영 조건", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();

        enableWeather = EditorGUILayout.Toggle("비·천둥 켜기", enableWeather);

        using (new EditorGUI.DisabledScope(!enableWeather))
        {
            weatherWarmupSeconds =
                EditorGUILayout.Slider("비 예열 (초)", weatherWarmupSeconds, 0.5f, 10f);
        }

        hideCanvas = EditorGUILayout.Toggle("Canvas(UI) 숨기기", hideCanvas);
        hideOutlines = EditorGUILayout.Toggle("Outline 끄기", hideOutlines);

        if (EditorGUI.EndChangeCheck() && autoPreview)
            RefreshPreview();

        EditorGUILayout.LabelField(
            "모두 촬영 중에만 바뀌고 끝나면 원래대로 되돌립니다.",
            EditorStyles.miniLabel);
    }

    private void DrawPreviewSection()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField($"미리보기 ({PreviewSize}×{PreviewSize})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            showGuides = GUILayout.Toggle(showGuides, "구도 가이드", EditorStyles.miniButton, GUILayout.Width(80f));
            showCharacterGuides = GUILayout.Toggle(
                showCharacterGuides, "캐릭터", EditorStyles.miniButton, GUILayout.Width(56f));
            autoPreview = GUILayout.Toggle(autoPreview, "자동 갱신", EditorStyles.miniButton, GUILayout.Width(70f));
        }

        if (GUILayout.Button("미리보기 갱신", GUILayout.Height(26f)))
            RefreshPreview();

        // 그림의 실제 비율로 틀을 잡는다 — 정사각이면 1.0 이라 전과 같고, 가로형이면
        // 가로로 긴 틀이 나와 찌그러지지 않는다.
        float previewAspect = preview != null ? (float)preview.width / preview.height : 1f;
        Rect area = GUILayoutUtility.GetAspectRect(previewAspect);

        if (preview != null)
        {
            GUI.DrawTexture(area, preview, ScaleMode.ScaleToFit);

            // 가이드는 1:1 대표 이미지 기준으로 계산되어 있어 가로형에는 맞지 않는다.
            if (!previewIsLettering)
            {
                if (showGuides)
                    DrawGuides(area);

                if (showCharacterGuides)
                    DrawCharacterGuides(area);
            }
        }
        else
        {
            EditorGUI.DrawRect(area, new Color(0.12f, 0.12f, 0.14f, 1f));
            GUI.Label(area, "미리보기 갱신을 누르세요", EditorStyles.centeredGreyMiniLabel);
        }

        if (previewIsLettering)
        {
            EditorGUILayout.LabelField(
                $"지금 보고 있는 것은 레터링 배경 {preview.width}×{preview.height} 입니다 — " +
                "구도 가이드는 1:1 기준이라 표시하지 않습니다.",
                EditorStyles.miniLabel);
        }
        else if (showCharacterGuides)
        {
            DrawCharacterGuideReadout();
        }
    }

    /// <summary>
    /// 합성 공간을 눈으로 가늠하는 선. 저장되는 PNG 에는 들어가지 않는다.
    /// </summary>
    private static void DrawGuides(Rect area)
    {
        Handles.BeginGUI();

        // 상단 1/3 — 로고 자리
        Color logoZone = new Color(0.31f, 0.77f, 0.85f, 0.9f);
        Handles.color = logoZone;
        float logoY = area.y + area.height / 3f;
        Handles.DrawLine(new Vector3(area.x, logoY), new Vector3(area.xMax, logoY));

        // 하단 1/4 — 캐릭터 뒷모습이 설 지면
        Color groundZone = new Color(1f, 0.78f, 0.25f, 0.9f);
        Handles.color = groundZone;
        float groundY = area.yMax - area.height / 4f;
        Handles.DrawLine(new Vector3(area.x, groundY), new Vector3(area.xMax, groundY));

        // 가운데 세로 — 좌우 균형
        Handles.color = new Color(1f, 1f, 1f, 0.25f);
        float centerX = area.x + area.width / 2f;
        Handles.DrawLine(new Vector3(centerX, area.y), new Vector3(centerX, area.yMax));

        Handles.EndGUI();

        var label = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = logoZone } };
        GUI.Label(new Rect(area.x + 6f, logoY - 16f, 200f, 16f), "↑ 로고 공간", label);

        label.normal.textColor = groundZone;
        GUI.Label(new Rect(area.x + 6f, groundY + 2f, 200f, 16f), "↓ 캐릭터 지면", label);
    }

    // ── 캐릭터 가이드 ─────────────────────────────────────────────────────

    /// <summary>
    /// 월드 좌표를 지금 구도의 화면 좌표(NDC)로 투영한다. x, y 모두 -1(좌·하) ~ +1(우·상).
    /// </summary>
    /// <remarks>
    /// 핀홀 투영을 직접 계산한다 — 임시 카메라를 만들지 않으므로 <c>OnGUI</c> 에서 매 프레임
    /// 불러도 된다. 쓰는 값(<see cref="cameraPosition"/>·<see cref="cameraEuler"/>·
    /// <see cref="fieldOfView"/>)이 <see cref="Render"/> 가 임시 카메라에 넣는 값과 같은 것이라
    /// 미리보기 그림과 가이드가 어긋나지 않는다.
    ///
    /// 촬영이 1:1 정사각이므로 가로·세로 모두 같은 <c>tan(FOV/2)</c> 로 나눈다.
    /// (Unity 카메라의 <c>fieldOfView</c> 는 세로 화각이고, 정사각에서는 가로도 같다)
    /// </remarks>
    private bool TryProject(Vector3 world, out Vector2 ndc)
    {
        // 카메라 로컬로 옮긴다 — z = 전방, x = 우, y = 상.
        Vector3 local = Quaternion.Inverse(Quaternion.Euler(cameraEuler)) * (world - cameraPosition);

        if (local.z <= 0.01f)
        {
            ndc = Vector2.zero;
            return false;   // 카메라 뒤이거나 너무 가깝다
        }

        float halfTan = Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
        ndc = new Vector2(local.x / local.z / halfTan, local.y / local.z / halfTan);
        return true;
    }

    /// <summary>NDC 를 미리보기 영역의 GUI 좌표로 옮긴다. GUI 는 y 가 아래로 자란다.</summary>
    private static Vector2 NdcToArea(Vector2 ndc, Rect area) => new Vector2(
        area.x + (ndc.x + 1f) * 0.5f * area.width,
        area.yMax - (ndc.y + 1f) * 0.5f * area.height);

    /// <summary>
    /// 캐릭터 실루엣의 네 꼭짓점을 NDC 로 구한다. 순서는 좌하 · 우하 · 우상 · 좌상.
    /// </summary>
    /// <remarks>
    /// 세로는 월드 Up(캐릭터는 수직으로 선다), 가로는 카메라의 Right 로 잡아 카메라를 향한
    /// 판처럼 만든다. 그래서 어떤 구도에서도 폭이 <see cref="CharacterGuideWidth"/> 로 보인다.
    /// 네 점을 따로 투영하므로 원근에 따라 살짝 찌그러지는 것까지 그대로 나온다.
    /// </remarks>
    private bool TryGetGuideQuad(Vector3 feetCenter, out Vector2[] ndc)
    {
        Vector3 half = Quaternion.Euler(cameraEuler) * Vector3.right * (CharacterGuideWidth * 0.5f);
        Vector3 top = Vector3.up * CharacterGuideHeight;

        Vector3[] corners =
        {
            feetCenter - half,
            feetCenter + half,
            feetCenter + top + half,
            feetCenter + top - half,
        };

        ndc = new Vector2[4];

        for (int i = 0; i < 4; i++)
        {
            if (!TryProject(corners[i], out ndc[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 두 캐릭터의 예상 실루엣. 미리보기 위에만 그리므로 저장되는 PNG 에는 들어가지 않는다.
    /// </summary>
    private void DrawCharacterGuides(Rect area)
    {
        Handles.BeginGUI();

        DrawOneCharacterGuide(area, lumenGuidePosition, LumenGuideColor, "루멘");
        DrawOneCharacterGuide(area, echoGuidePosition, EchoGuideColor, "에코");

        Handles.EndGUI();
    }

    private void DrawOneCharacterGuide(Rect area, Vector3 feetCenter, Color color, string label)
    {
        if (!TryGetGuideQuad(feetCenter, out Vector2[] ndc))
            return;

        // 프레임을 크게 벗어나면 그리지 않는다 — 미리보기 밖으로 삐져나와 다른 UI 를 덮는 것을 막는다.
        for (int i = 0; i < 4; i++)
        {
            if (Mathf.Abs(ndc[i].x) > 1.5f || Mathf.Abs(ndc[i].y) > 1.5f)
                return;
        }

        var verts = new Vector3[4];

        for (int i = 0; i < 4; i++)
        {
            Vector2 point = NdcToArea(ndc[i], area);
            verts[i] = new Vector3(point.x, point.y, 0f);
        }

        Handles.DrawSolidRectangleWithOutline(
            verts, new Color(color.r, color.g, color.b, 0.18f), color);

        var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = color } };
        Vector2 head = NdcToArea(new Vector2((ndc[2].x + ndc[3].x) * 0.5f, ndc[2].y), area);
        GUI.Label(new Rect(head.x - 22f, head.y - 17f, 48f, 16f), label, style);
    }

    /// <summary>
    /// 가이드를 눈으로만 보지 않고 숫자로도 읽게 한다 — 겹침 확인이 이 기능의 목적이다.
    /// </summary>
    private void DrawCharacterGuideReadout()
    {
        if (!TryGetGuideQuad(lumenGuidePosition, out Vector2[] lumen) ||
            !TryGetGuideQuad(echoGuidePosition, out Vector2[] echo))
        {
            EditorGUILayout.LabelField(
                "캐릭터 가이드: 지금 구도에서는 투영할 수 없습니다(카메라 뒤).",
                EditorStyles.miniLabel);
            return;
        }

        float heightPercent = (lumen[3].y - lumen[0].y) * 0.5f * 100f;

        float lumenLeft = Mathf.Min(lumen[0].x, lumen[1].x);
        float lumenRight = Mathf.Max(lumen[0].x, lumen[1].x);
        float echoLeft = Mathf.Min(echo[0].x, echo[1].x);
        float echoRight = Mathf.Max(echo[0].x, echo[1].x);

        EditorGUILayout.LabelField(
            $"캐릭터 높이 {heightPercent:F1}%   (발 sy {lumen[0].y:F2} → 머리 sy {lumen[3].y:F2})",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            $"루멘 sx {lumenLeft:F2} ~ {lumenRight:F2}      에코 sx {echoLeft:F2} ~ {echoRight:F2}" +
            $"      둘 사이 빈 공간 {(echoLeft - lumenRight):F2}",
            EditorStyles.miniLabel);

        EditorGUILayout.LabelField(
            "sx·sy 는 -1(좌·하) ~ +1(우·상). 중앙 구조물이 이 빈 공간 안에 들어오면 겹치지 않는다.",
            EditorStyles.miniLabel);

        // 프리셋마다 좌표가 달라지므로 지금 쓰는 값을 같이 보여 준다.
        EditorGUILayout.LabelField(
            $"가이드 좌표 — 루멘 {lumenGuidePosition}   에코 {echoGuidePosition}   " +
            $"({CharacterGuideHeight:0.##} m × {CharacterGuideWidth:0.##} m)",
            EditorStyles.miniLabel);
    }

    private void DrawCaptureSection()
    {
        EditorGUILayout.LabelField($"저장 ({CaptureSize}×{CaptureSize} PNG)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(CurrentOutputPath, EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(!TryGetScene(out _)))
        {
            if (GUILayout.Button($"{CaptureSize}×{CaptureSize} 로 촬영", GUILayout.Height(34f)))
                Capture();
        }
    }

    // ───────────────────────────── 동작 ─────────────────────────────

    private void OpenCaptureScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (!File.Exists(ScenePath))
        {
            SetStatus($"씬 파일을 찾지 못했습니다: {ScenePath}", MessageType.Error);
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SetStatus($"'{SceneName}' 를 열었습니다.", MessageType.Info);
    }

    private void PullFromSceneView()
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (view == null || view.camera == null)
        {
            SetStatus("Scene 뷰를 열고 구도를 잡은 뒤 다시 누르세요.", MessageType.Warning);
            return;
        }

        Transform viewCamera = view.camera.transform;
        cameraPosition = viewCamera.position;
        cameraEuler = viewCamera.rotation.eulerAngles;
        fieldOfView = view.camera.fieldOfView;
        nearClip = view.camera.nearClipPlane;
        farClip = Mathf.Max(view.camera.farClipPlane, 1000f);

        SetStatus("Scene 뷰 구도를 가져왔습니다. 미리보기로 1:1 결과를 확인하세요.", MessageType.Info);
        Repaint();

        if (autoPreview)
            RefreshPreview();
    }

    /// <summary>창에서 숫자로 맞춘 구도를 Scene 뷰로 보내 눈으로 확인한다.</summary>
    private void PushToSceneView()
    {
        SceneView view = SceneView.lastActiveSceneView;

        if (view == null)
        {
            SetStatus("Scene 뷰가 없습니다.", MessageType.Warning);
            return;
        }

        var rotation = Quaternion.Euler(cameraEuler);
        view.LookAtDirect(cameraPosition + rotation * Vector3.forward * 5f, rotation);
        view.Repaint();

        SetStatus("Scene 뷰를 이 구도로 옮겼습니다.", MessageType.Info);
    }

    private void RefreshPreview()
    {
        Texture2D rendered = Render(CurrentRequest(PreviewSize, PreviewSize));

        if (rendered == null)
            return;

        DestroyPreview();
        preview = rendered;
        previewIsLettering = false;

        SetStatus("미리보기를 갱신했습니다.", MessageType.Info);
        Repaint();
    }

    /// <summary>
    /// 레터링 배경만 가로형으로 한 장 그린다. 캐릭터와 로고는 넣지 않는다.
    /// </summary>
    private void RefreshLetteringPreview()
    {
        Texture2D rendered = Render(
            LetteringRequest(LetteringPreviewWidth, LetteringPreviewHeight));

        if (rendered == null)
            return;

        DestroyPreview();
        preview = rendered;
        previewIsLettering = true;

        SetStatus(
            $"레터링 배경 미리보기 {LetteringPreviewWidth}×{LetteringPreviewHeight} 를 그렸습니다 — " +
            $"최종 {LetteringFinalWidth}×{LetteringFinalHeight} 와 비율이 같습니다. " +
            "캐릭터와 로고는 넣지 않았습니다.",
            MessageType.Info);

        Repaint();
    }

    private void Capture()
    {
        Texture2D rendered = Render(CurrentRequest(CaptureSize, CaptureSize));

        if (rendered == null)
            return;

        try
        {
            Directory.CreateDirectory(OutputFolder);

            string path = CurrentOutputPath;
            File.WriteAllBytes(path, rendered.EncodeToPNG());

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.maxTextureSize = CaptureSize;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            string full = Path.GetFullPath(path);

            SetStatus(
                $"저장했습니다 — {CaptureSize}×{CaptureSize}\n{full}",
                MessageType.Info);

            Debug.Log($"[KeyArt] Stage1 Before 저장: {path}\n" +
                      $"  위치 {cameraPosition}  회전 {cameraEuler}  FOV {fieldOfView}\n" +
                      $"  공명 상태 {resonanceFogState} " +
                      $"({MultiplierOf(resonanceFogState, gameFogValues):0.##}×)\n" +
                      "  씬·FogSettings.asset·URP Renderer 는 저장하지 않았습니다. " +
                      "임시 변경은 모두 되돌렸습니다.");

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }
        finally
        {
            DestroyImmediate(rendered);
        }
    }

    // ───────────────────────────── 렌더 ─────────────────────────────

    /// <summary>
    /// 한 장을 그리는 데 필요한 모든 것. 창의 필드를 직접 읽지 않고 이 꾸러미를 받게 해 둔 이유는,
    /// 대표 이미지(정사각)와 레터링 배경(가로형)이 <b>같은 렌더 경로</b>를 쓰면서도 서로의
    /// 설정을 건드리지 않게 하려는 것이다.
    /// </summary>
    private struct RenderRequest
    {
        public int Width;
        public int Height;

        public Vector3 CameraPosition;
        public Vector3 CameraEuler;
        public float FieldOfView;
        public float Near;
        public float Far;

        public ResonanceFogState FogState;

        /// <summary>false 면 공명 포즈 캐릭터를 아예 만들지 않는다.</summary>
        public bool IncludePose;

        /// <summary>false 면 로고를 합성하지 않는다.</summary>
        public bool IncludeLogo;

        public string Label;
    }

    /// <summary>
    /// 창에 맞춰 둔 값 그대로. 기존 1024 미리보기·2048 촬영이 쓰던 것과 같은 값이므로
    /// 결과가 달라지지 않는다(포즈·로고는 각자 제 토글을 계속 본다).
    /// </summary>
    private RenderRequest CurrentRequest(int width, int height) => new RenderRequest
    {
        Width = width,
        Height = height,
        CameraPosition = cameraPosition,
        CameraEuler = cameraEuler,
        FieldOfView = fieldOfView,
        Near = nearClip,
        Far = farClip,
        FogState = resonanceFogState,
        IncludePose = true,
        IncludeLogo = true,
        Label = "대표 이미지",
    };

    /// <summary>
    /// 레터링 배경 전용. 카메라·안개를 따로 들고 다니므로 프리셋 A~G 와 창의 구도 값에
    /// 영향을 주지도, 받지도 않는다. 캐릭터와 로고는 이 단계에서 넣지 않는다.
    /// </summary>
    private RenderRequest LetteringRequest(int width, int height) => new RenderRequest
    {
        Width = width,
        Height = height,
        CameraPosition = letteringCameraPosition,
        CameraEuler = letteringCameraEuler,
        FieldOfView = letteringFieldOfView,
        Near = 0.3f,      // 프리셋 F·G 와 동일
        Far = 1000f,
        FogState = letteringFogState,
        IncludePose = false,
        IncludeLogo = false,
        Label = "레터링 배경",
    };

    /// <summary>
    /// 요청한 크기로 한 장 그린다. 임시 상태 변경 → 렌더 → <c>finally</c> 에서 역순 복구.
    /// </summary>
    private Texture2D Render(RenderRequest request)
    {
        if (!TryGetScene(out Scene scene))
        {
            SetStatus($"'{SceneName}' 씬이 열려 있지 않습니다.", MessageType.Error);
            return null;
        }

        var restore = new List<Action>();
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture target = null;
        GameObject cameraObject = null;
        Texture2D image = null;

        try
        {
            ApplyTemporaryState(scene, restore, request.IncludePose);

            target = new RenderTexture(request.Width, request.Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = MsaaSamples,
            };
            target.Create();

            cameraObject = new GameObject("KeyArtCaptureCamera")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(
                request.CameraPosition, Quaternion.Euler(request.CameraEuler));
            camera.fieldOfView = request.FieldOfView;
            camera.nearClipPlane = request.Near;
            camera.farClipPlane = Mathf.Max(request.Far, 1000f);

            // targetTexture 를 주면 보통 자동으로 잡히지만, camera.Render() 를 직접 부르는
            // 경로라 비율을 명시해 둔다. 정사각이면 1.0 이라 기존과 같다.
            camera.aspect = (float)request.Width / request.Height;

            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowMSAA = true;
            camera.targetTexture = target;
            camera.enabled = false;   // 이번 한 장만 직접 그린다

            // 게임과 같은 URP 경로로 그린다. 안개(FlatKitFog)·SSAO 는 Renderer Feature 라
            // 따로 켜지 않아도 들어오고, 색감·Bloom 은 프로젝트 기본 볼륨에서 온다.
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;
            cameraData.renderShadows = true;

            // 깊이 텍스처를 이 카메라에서 직접 요구한다.
            //
            // 안개(FlatKitFog)는 <c>SampleSceneDepth</c> 로 <c>_CameraDepthTexture</c> 를 읽어
            // 거리를 구한다(FogCore.hlsl:29·32-34). 그 깊이가 없으면 역-Z 에서 0 이
            // "가장 먼 거리"로 해석되어 <b>화면 전체가 최대 안개</b>가 된다
            // (설정상 카메라에서 5 m 만 넘어도 안개가 포화된다 — FogSettings.far = 5).
            //
            // 기본값은 <c>UsePipelineSettings</c> 이고 PC_RPAsset 은 이미
            // <c>m_RequireDepthTexture: 1</c> 이라 보통은 들어온다. 다만 여기는 정상 렌더 루프가
            // 아니라 <c>camera.Render()</c> 를 직접 부르는 경로이므로, 파이프라인 설정에
            // 기대지 않고 명시적으로 켜 둔다. 이미 켜져 있었다면 결과는 달라지지 않는다.
            cameraData.requiresDepthTexture = true;

            // 공명 상태는 렌더 직전에 적용한다.
            //
            // 임시 복제본을 끼워 넣는 방식은 <c>FlatKitFog.Create()</c> 가 다시 돌면 풀린다.
            // 적용과 <c>Render()</c> 사이를 최대한 붙여 그 틈을 없앤다.
            // (<c>AddRenderPasses</c> 는 설정을 다시 밀어 넣지 않으므로 한 번 끼우면 이 렌더 동안 유지된다 —
            //  FlatKitFog.cs:75-84 확인)
            ApplyResonanceFogState(restore, request.FogState);

            camera.Render();

            RenderTexture.active = target;
            image = new Texture2D(request.Width, request.Height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0f, 0f, request.Width, request.Height), 0, 0);
            image.Apply();

            // 로고는 배경·캐릭터를 다 그린 뒤에 얹는다. 미리보기와 최종 PNG 가 같은 경로를 타므로
            // 정규화 좌표만 쓰면 1024 와 2048 에서 같은 구도가 나온다.
            // 레터링 배경은 이 단계에서 로고를 넣지 않는다(순수 구도만 확인).
            if (request.IncludeLogo)
                CompositeLogo(image);
        }
        catch (Exception error)
        {
            SetStatus($"렌더 중 오류: {error.Message}", MessageType.Error);
            Debug.LogException(error);

            if (image != null)
            {
                DestroyImmediate(image);
                image = null;
            }
        }
        finally
        {
            RenderTexture.active = previousActive;

            if (cameraObject != null)
                DestroyImmediate(cameraObject);

            if (target != null)
            {
                target.Release();
                DestroyImmediate(target);
            }

            // 건드린 역순으로 되돌린다.
            for (int i = restore.Count - 1; i >= 0; i--)
            {
                try { restore[i](); }
                catch (Exception error) { Debug.LogException(error); }
            }
        }

        return image;
    }

    // ───────────────────────────── 공명 상태 ─────────────────────────────

    private struct FogFeatureRef
    {
        public FlatKit.FlatKitFog Feature;
        public ScriptableRendererData Owner;
    }

    /// <summary>
    /// 고른 공명 상태를 지금 렌더에만 적용한다. 되돌리는 방법은 <paramref name="restore"/> 에 쌓는다.
    /// </summary>
    /// <remarks>
    /// 상태별로 <b>건드리는 대상이 다르다</b> —
    /// <list type="bullet">
    /// <item>최대 제약 — 아무것도 건드리지 않는다(에셋에 저장된 값이 곧 그 상태다).</item>
    /// <item>근접 완화 — 임시 머티리얼 복제본을 피처에 끼운다(공유 에셋은 읽기만).</item>
    /// <item>공명 해제 — 피처를 끈다(게임의 <c>FogRendererFeatureSceneController</c> 와 동일).</item>
    /// </list>
    /// 어느 쪽도 <c>FogSettings.asset</c> 의 필드에 쓰지 않는다. 그 필드에 쓰는 것은
    /// 런타임의 <c>FogConstraint.Apply</c> 뿐이고, 그 경로가 이미 에셋에 값을 남긴 적이 있다
    /// (0.95 → 0.949962). 촬영 도구가 같은 일을 반복하지 않도록 일부러 피했다.
    /// </remarks>
    private void ApplyResonanceFogState(List<Action> restore, ResonanceFogState state)
    {
        if (state == ResonanceFogState.MaxConstraint)
            return;

        List<FogFeatureRef> features = FindFogFeatures();

        if (features.Count == 0)
        {
            Debug.LogWarning(
                "[KeyArt] 활성 URP 파이프라인에서 FlatKitFog Renderer Feature 를 찾지 못했습니다. " +
                "공명 상태를 적용하지 않고 현재 설정 그대로 찍습니다.");
            return;
        }

        if (state == ResonanceFogState.Released)
        {
            SuppressFogFeatures(features, restore);
            return;
        }

        ScaleFogIntensity(features, MultiplierOf(state, gameFogValues), restore);
    }

    /// <summary>
    /// 피처 자체를 끈다 — 게임이 <c>StartScene</c>·<c>Stage1_TitleBackground</c> 와
    /// 엔딩에서 쓰는 것과 같은 방식이다. 수치는 전혀 건드리지 않는다.
    /// </summary>
    private static void SuppressFogFeatures(List<FogFeatureRef> features, List<Action> restore)
    {
        foreach (FogFeatureRef entry in features)
        {
            FlatKit.FlatKitFog feature = entry.Feature;
            ScriptableRendererData owner = entry.Owner;

            if (feature == null || !feature.isActive)
                continue;

            // SetActive 는 직렬화 필드(m_Active)를 건드린다. 원래 dirty 가 아니었다면
            // 값을 되돌린 뒤 플래그까지 지워, 실수로 저장돼도 Renderer 에셋이 바뀌지 않게 한다.
            bool featureWasDirty = EditorUtility.IsDirty(feature);
            bool ownerWasDirty = owner != null && EditorUtility.IsDirty(owner);

            feature.SetActive(false);

            restore.Add(() =>
            {
                if (feature == null)
                    return;

                feature.SetActive(true);

                if (!featureWasDirty)
                    EditorUtility.ClearDirty(feature);

                if (owner != null && !ownerWasDirty)
                    EditorUtility.ClearDirty(owner);
            });
        }
    }

    /// <summary>
    /// 안개 강도에 <paramref name="multiplier"/> 를 먹인다.
    /// </summary>
    /// <remarks>
    /// 기본 경로는 <b>임시 머티리얼 복제본</b>이다. <c>FlatKitFog</c> 는 자기 <c>_effectMaterial</c>
    /// 만 보고 그리고(<c>AddRenderPasses</c>), 설정값을 다시 밀어 넣는 것은 <c>Create()</c> 와
    /// <c>FogSettings.OnValidate</c> 뿐이다. 그래서 그 필드에 복제본을 끼워 두면 이 렌더 한 장 동안
    /// 유지되고, 공유 에셋은 읽기만 한다.
    ///
    /// 복제본을 끼울 수 없을 때만(필드 이름이 바뀐 FlatKit 버전) 공유 머티리얼을 직접 쓰고,
    /// 그 경우에도 원래 값과 dirty 플래그를 되돌린다.
    /// </remarks>
    private static void ScaleFogIntensity(
        List<FogFeatureRef> features, float multiplier, List<Action> restore)
    {
        foreach (FogFeatureRef entry in features)
        {
            FlatKit.FlatKitFog feature = entry.Feature;

            if (feature == null || !feature.isActive)
                continue;

            FlatKit.FogSettings settings = feature.settings;

            if (settings == null)
                continue;

            // 배율의 기준은 에셋에 저장된 값이다 — FogConstraint.Initialize 가
            // configuredDistanceIntensity 로 캡처하는 값과 같으므로 런타임과 결과가 일치한다.
            float distance = settings.distanceFogIntensity * multiplier;
            float height = settings.heightFogIntensity * multiplier;

            // 되돌릴 때는 원래 들어 있던 것을 그대로 넣는다 — null 이었다면 null 로.
            object originalField = FogEffectMaterialField?.GetValue(feature);
            Material shared = originalField as Material ?? settings.effectMaterial;

            if (shared == null)
                continue;

            if (FogEffectMaterialField != null)
            {
                var temp = new Material(shared)
                {
                    name = $"{shared.name} (KeyArt Temp)",
                    hideFlags = HideFlags.HideAndDontSave,
                };

                // USE_DISTANCE_FOG / USE_HEIGHT_FOG 가 빠지면 안개가 아예 나오지 않는다.
                temp.shaderKeywords = shared.shaderKeywords;
                temp.SetFloat(DistanceFogIntensityId, distance);
                temp.SetFloat(HeightFogIntensityId, height);

                FogEffectMaterialField.SetValue(feature, temp);

                restore.Add(() =>
                {
                    if (feature != null)
                        FogEffectMaterialField.SetValue(feature, originalField);

                    if (temp != null)
                        DestroyImmediate(temp);
                });

                continue;
            }

            float originalDistance = shared.GetFloat(DistanceFogIntensityId);
            float originalHeight = shared.GetFloat(HeightFogIntensityId);
            bool wasDirty = EditorUtility.IsDirty(shared);

            shared.SetFloat(DistanceFogIntensityId, distance);
            shared.SetFloat(HeightFogIntensityId, height);

            restore.Add(() =>
            {
                if (shared == null)
                    return;

                shared.SetFloat(DistanceFogIntensityId, originalDistance);
                shared.SetFloat(HeightFogIntensityId, originalHeight);

                if (!wasDirty)
                    EditorUtility.ClearDirty(shared);
            });
        }
    }

    /// <summary>활성 URP 파이프라인에 등록된 <c>FlatKitFog</c> 피처를 모두 모은다.</summary>
    private static List<FogFeatureRef> FindFogFeatures()
    {
        var found = new List<FogFeatureRef>();

        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
            is not UniversalRenderPipelineAsset pipeline)
            return found;

        ReadOnlySpan<ScriptableRendererData> dataList = pipeline.rendererDataList;

        for (int i = 0; i < dataList.Length; i++)
        {
            ScriptableRendererData data = dataList[i];

            if (data == null)
                continue;

            foreach (ScriptableRendererFeature feature in data.rendererFeatures)
            {
                if (feature is FlatKit.FlatKitFog fog)
                    found.Add(new FogFeatureRef { Feature = fog, Owner = data });
            }
        }

        return found;
    }

    private static bool TryGetFogSettings(out FlatKit.FogSettings result)
    {
        foreach (FogFeatureRef entry in FindFogFeatures())
        {
            if (entry.Feature == null || entry.Feature.settings == null)
                continue;

            result = entry.Feature.settings;
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>
    /// 게임이 실제로 쓰는 <c>activeIntensity</c>·<c>maxConstraintRelief</c> 를
    /// <c>ResonanceSystem.prefab</c> 에서 읽는다. <c>FogConstraint</c> 의 필드가 private 이라
    /// <see cref="SerializedObject"/> 로 읽는다(읽기만 하고 쓰지 않는다).
    /// </summary>
    private static FogConstraintValues ReadFogConstraintValues()
    {
        var values = new FogConstraintValues
        {
            ActiveIntensity = FallbackActiveIntensity,
            MaxConstraintRelief = FallbackMaxConstraintRelief,
            FromPrefab = false,
        };

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResonancePrefabPath);

        if (prefab == null)
            return values;

        var controller = prefab.GetComponent<ResonanceController>();

        if (controller == null)
            return values;

        var serialized = new SerializedObject(controller);
        SerializedProperty active = serialized.FindProperty("fog.activeIntensity");
        SerializedProperty relief = serialized.FindProperty("fog.maxConstraintRelief");

        if (active == null || relief == null)
            return values;

        values.ActiveIntensity = active.floatValue;
        values.MaxConstraintRelief = relief.floatValue;
        values.FromPrefab = true;
        return values;
    }

    /// <summary>
    /// <c>FogConstraint.Tick</c> 의 식을 그대로 옮긴 것.
    /// <code>
    /// float relief = maxConstraintRelief * proximity;
    /// float target = inactive ? 0f : activeIntensity * (1f - relief);
    /// </code>
    /// </summary>
    private static float MultiplierOf(ResonanceFogState state, FogConstraintValues values) => state switch
    {
        // proximity = 0 → relief 없음
        ResonanceFogState.MaxConstraint => values.ActiveIntensity * (1f - values.MaxConstraintRelief * 0f),

        // proximity = 1 → relief 최대
        ResonanceFogState.Relieved => values.ActiveIntensity * (1f - values.MaxConstraintRelief * 1f),

        // inactive = true
        ResonanceFogState.Released => 0f,

        _ => values.ActiveIntensity,
    };

    // ───────────────────────────── 공명 포즈 ─────────────────────────────

    /// <summary>엔딩씬에서 두 사람의 중점(발밑). 쌍을 강체로 옮길 때의 기준점이다.</summary>
    private static Vector3 Stage3PairCenter =>
        (PoseActors[0].Stage3Position + PoseActors[1].Stage3Position) * 0.5f;

    private Quaternion PoseGroupRotation => Quaternion.Euler(0f, posePairYaw, 0f);

    /// <summary>엔딩씬 월드 좌표를 촬영 씬 좌표로 옮긴다. 상대 배치는 그대로 유지된다.</summary>
    private Vector3 MapPosePoint(Vector3 stage3World) =>
        posePairCenter + PoseGroupRotation * (stage3World - Stage3PairCenter);

    private Quaternion MapPoseRotation(Quaternion stage3World) => PoseGroupRotation * stage3World;

    /// <summary>
    /// <c>StepActorsCloser</c> 를 적용한 뒤의 두 사람 간격.
    /// 간격은 <c>Mathf.Min(closeGap, gap * 0.5f)</c> 만큼 줄어든다(양쪽이 절반씩 움직인다).
    /// </summary>
    private static float PosePairGap
    {
        get
        {
            float gap = Vector3.ProjectOnPlane(
                PoseActors[1].Stage3Position - PoseActors[0].Stage3Position, Vector3.up).magnitude;

            return gap - Mathf.Min(EndingCloseGap, gap * 0.5f);
        }
    }

    /// <summary>
    /// 엔딩씬의 공명 포즈를 임시 인스턴스로 재현한다. 되돌리는 방법은 <paramref name="restore"/> 에 쌓는다.
    /// </summary>
    /// <remarks>
    /// 순서는 엔딩씬과 같다 —
    /// <list type="number">
    /// <item>실제 프리팹을 임시로 만든다. 메시·머티리얼은 원본 에셋을 그대로 참조한다.</item>
    /// <item>엔딩씬의 상대 위치·회전을 강체로 옮긴다. 중점과 Yaw 만 바뀐다.</item>
    /// <item><c>StepActorsCloser</c> — 두 사람을 서로에게 당긴다(각자 0.05 m).</item>
    /// <item><c>PrepareActor</c> — 모든 리그 제약 가중치 0, 팔꿈치 힌트를 위팔 밖으로.</item>
    /// <item><c>ApplyHandReach(1)</c> — IK 타깃을 MeetPose 에 놓고 손 IK 가중치 1.</item>
    /// <item><c>AimElbow</c> — 팔꿈치 힌트를 어깨-손 중간의 아래·바깥에 둔다.</item>
    /// <item><c>RigBuilder.Build()</c> + <c>Evaluate(0)</c> — 여기서 실제로 팔이 움직인다.</item>
    /// </list>
    /// 마지막에 손끝(<c>tip</c>)이 목표에서 얼마나 떨어졌는지 재서 보고한다. Edit 모드에서는
    /// <c>RigBuilder</c> 가 스스로 그래프를 만들지 않으므로(<c>OnEnable</c> 이
    /// <c>Application.isPlaying</c> 일 때만 <c>Build</c> 한다 — RigBuilder.cs:51-57),
    /// <b>이 거리가 A안이 Edit 모드에서 동작하는지를 가리는 유일한 객관적 증거</b>다.
    ///
    /// 원본을 건드리지 않는 근거 — 만드는 것은 프리팹의 임시 복제본뿐이고
    /// <c>HideFlags.DontSave</c> 가 붙어 저장 대상에서 빠진다. 힌트 재부모화·가중치 변경은
    /// 모두 그 복제본 안에서만 일어나고, 복제본은 <paramref name="restore"/> 에서 지워진다.
    /// 프리팹 에셋·Stage3.unity·애니메이션 파일에는 아무것도 쓰지 않는다.
    /// </remarks>
    private void ApplyResonancePose(Scene scene, List<Action> restore)
    {
        poseReport = string.Empty;
        poseReportType = MessageType.Info;

        if (!showResonancePose)
            return;

        var log = new StringBuilder();
        int count = PoseActors.Length;
        var roots = new GameObject[count];
        var hands = new TwoBoneIKConstraint[count];
        var goals = new Vector3[count];
        bool failed = false;

        // 1) 실제 모델을 임시로 만들고 엔딩씬의 배치를 옮긴다
        for (int i = 0; i < count; i++)
        {
            PoseActorSpec spec = PoseActors[i];
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath);

            if (asset == null)
            {
                log.AppendLine($"{spec.Label}: 프리팹을 찾지 못했습니다 — {spec.PrefabPath}");
                failed = true;
                continue;
            }

            GameObject instance = Instantiate(asset);
            instance.name = $"KeyArtPose_{spec.Label}";

            if (instance.scene != scene)
                SceneManager.MoveGameObjectToScene(instance, scene);

            MarkPoseTemporary(instance);

            GameObject captured = instance;
            restore.Add(() =>
            {
                if (captured != null)
                    DestroyImmediate(captured);
            });

            instance.transform.SetPositionAndRotation(
                MapPosePoint(spec.Stage3Position), MapPoseRotation(spec.Stage3Rotation));

            roots[i] = instance;
        }

        if (failed || count < 2 || roots[0] == null || roots[1] == null)
        {
            poseReport = log.Length > 0 ? log.ToString().TrimEnd() : "공명 포즈를 만들지 못했습니다.";
            poseReportType = MessageType.Error;
            return;
        }

        // 2) StepActorsCloser — 엔딩씬이 화면이 검을 때 하는 보정
        if (poseApplyCloseGap)
            StepPoseActorsCloser(roots[0].transform, roots[1].transform);

        // 3) PrepareActor + ApplyHandReach(1) + AimElbow
        for (int i = 0; i < count; i++)
        {
            PoseActorSpec spec = PoseActors[i];
            hands[i] = PreparePoseActor(roots[i], spec.HandIkName, spec.Label, log);

            if (hands[i] == null)
            {
                failed = true;
                continue;
            }

            goals[i] = MapPosePoint(spec.Stage3MeetPosition);
            ApplyPoseHandReach(roots[i], hands[i], goals[i], MapPoseRotation(spec.Stage3MeetRotation));
        }

        // 4) RigBuilder 로 실제로 IK 를 푼다
        //
        // 진단이 켜져 있으면 Build() 이전 · Build() 직후 · Evaluate(0f) 직후 세 시점의 본과 Bounds 를
        // 떠 둔다. 떠 두는 일은 전부 읽기이므로 포즈 적용 순서와 렌더 동작은 달라지지 않는다.
        Plane[] frustum = poseDiagnostics ? CalculateCaptureFrustum() : null;
        var snapshots = new List<PoseSnapshot>[count];

        for (int i = 0; i < count; i++)
        {
            if (roots[i] == null)
                continue;

            PoseActorSpec spec = PoseActors[i];
            var builder = roots[i].GetComponentInChildren<RigBuilder>(true);

            if (poseDiagnostics)
            {
                snapshots[i] = new List<PoseSnapshot>(3)
                {
                    TakePoseSnapshot("BEFORE Build()", roots[i], hands[i], goals[i], frustum),
                };
            }

            if (builder == null)
            {
                log.AppendLine($"{spec.Label}: RigBuilder 가 없습니다 — IK 를 풀 수 없습니다.");
                failed = true;
                continue;
            }

            if (!poseEvaluateRig)
            {
                log.AppendLine($"{spec.Label}: IK 해석이 꺼져 있어 타깃·힌트만 놓았습니다.");
                continue;
            }

            PoseRigGraph rig = TryBuildPoseRigGraph(builder, spec, restore, log);

            if (poseDiagnostics)
                snapshots[i].Add(TakePoseSnapshot("AFTER  Build(graph)", roots[i], hands[i], goals[i], frustum));

            if (!rig.Valid)
            {
                failed = true;
                continue;
            }

            EvaluatePoseRigGraph(rig);

            if (poseDiagnostics)
                snapshots[i].Add(TakePoseSnapshot("AFTER  Evaluate(0f)", roots[i], hands[i], goals[i], frustum));

            log.AppendLine($"{spec.Label}: {rig.Note}");
        }

        // 4b) 진단을 캐릭터별로 한 덩어리씩 찍는다 — 미리보기 1회에 캐릭터당 Debug.Log 1번이다.
        if (poseDiagnostics)
        {
            for (int i = 0; i < count; i++)
            {
                if (snapshots[i] == null || snapshots[i].Count == 0)
                    continue;

                Debug.Log(BuildPoseDiagnostics(PoseActors[i], snapshots[i]));
                log.AppendLine(SummarizePoseDiagnostics(PoseActors[i], snapshots[i]));
            }
        }

        // 5) 결과를 숫자로 보고한다 — 손끝이 목표에 닿았는지가 판정 기준이다
        for (int i = 0; i < count; i++)
        {
            PoseActorSpec spec = PoseActors[i];

            if (hands[i] == null)
                continue;

            Transform tip = hands[i].data.tip;

            if (tip == null)
            {
                log.AppendLine($"{spec.Label}: IK 의 tip(손 뼈)이 비어 있습니다.");
                failed = true;
                continue;
            }

            float distance = Vector3.Distance(tip.position, goals[i]);
            bool solved = distance <= PoseSolvedTolerance;

            string verdict;

            if (!poseEvaluateRig)
                verdict = "IK 해석을 끈 상태입니다(이 거리는 정상).";
            else if (solved)
                verdict = "IK 가 풀렸습니다.";
            else
                verdict = "팔이 움직이지 않았거나 목표에 닿지 않았습니다.";

            log.AppendLine($"{spec.Label}: 손끝 → 목표 {distance:0.000} m — {verdict}");

            if (poseEvaluateRig && !solved)
                failed = true;
        }

        // 두 손바닥이 실제로 맞닿았는지도 같이 본다 — 포즈가 그림으로 맞는지의 최종 확인이다
        if (hands[0] != null && hands[1] != null &&
            hands[0].data.tip != null && hands[1].data.tip != null)
        {
            float touched = Vector3.Distance(hands[0].data.tip.position, hands[1].data.tip.position);
            float authored = Vector3.Distance(
                PoseActors[0].Stage3MeetPosition, PoseActors[1].Stage3MeetPosition);

            log.AppendLine($"두 손끝 사이 {touched:0.000} m (엔딩씬 MeetPose 간격 {authored:0.000} m)");
        }

        poseReport = log.ToString().TrimEnd();
        poseReportType = failed ? MessageType.Warning : MessageType.Info;
    }

    // ───────────────────────────── 로고 합성 ─────────────────────────────

    /// <summary>
    /// 렌더가 끝난 이미지 위에 로고를 얹는다. 끄면 아무것도 하지 않는다.
    /// </summary>
    /// <remarks>
    /// 배경의 색감·명암을 건드리지 않기 위해 <b>로고 사각형 안쪽 픽셀만</b> 읽고 쓴다
    /// (<c>GetPixels(x, y, w, h)</c> / <c>SetPixels(x, y, w, h, ...)</c>). 바깥은 손대지 않으므로
    /// 로고가 덮지 않은 영역은 끄고 찍은 것과 바이트 단위로 같다.
    ///
    /// 합성식은 표준 over 연산이다. 로고를 프리멀티플라이드로 들고 있으므로
    /// <code>
    /// a   = logo.a * opacity
    /// dst = logo.rgb * opacity + dst * (1 - a)
    /// </code>
    /// 가 된다. 두 텍스처를 같은 생성자 기본값(<c>linear: false</c>)으로 만들었으므로
    /// <c>GetPixels</c> 가 양쪽에 같은 색공간 변환을 적용한다 — 한쪽만 감마가 어긋나지 않는다.
    /// </remarks>
    private void CompositeLogo(Texture2D image)
    {
        logoReport = string.Empty;
        logoReportType = MessageType.Info;

        if (!showLogo)
            return;

        LogoSource source = LoadLogoSource(out string note);

        if (!source.IsValid)
        {
            logoReport = note;
            logoReportType = MessageType.Error;
            Debug.LogError($"[KeyArt] 로고 합성 실패 — {note}");
            return;
        }

        logoSourceAspect = source.Aspect;

        int size = image.width;   // 촬영은 1:1 정사각이다

        // 가로만 받고 세로는 원본 비율로 유도한다 — 그래서 늘어나거나 찌그러질 수 없다.
        int destWidth = Mathf.Max(1, Mathf.RoundToInt(logoWidth * size));
        int destHeight = Mathf.Max(1, Mathf.RoundToInt(destWidth * source.Aspect));

        // 좌상단 원점 비율 -> 아래가 0 인 픽셀 좌표(ReadPixels·GetPixels 와 같은 방향).
        int left = Mathf.RoundToInt(logoCenter.x * size - destWidth * 0.5f);
        int topFromTop = Mathf.RoundToInt(logoCenter.y * size - destHeight * 0.5f);
        int bottom = size - (topFromTop + destHeight);

        // 화면 밖으로 삐져나간 부분은 잘라 낸다.
        int clampedLeft = Mathf.Max(0, left);
        int clampedBottom = Mathf.Max(0, bottom);
        int clampedRight = Mathf.Min(size, left + destWidth);
        int clampedTop = Mathf.Min(size, bottom + destHeight);

        int width = clampedRight - clampedLeft;
        int height = clampedTop - clampedBottom;

        if (width <= 0 || height <= 0)
        {
            logoReport = "로고가 화면 완전히 밖에 있습니다 — X·Y 위치를 확인하세요.";
            logoReportType = MessageType.Warning;
            return;
        }

        if (logoOpacity <= 0f)
        {
            logoReport = "불투명도가 0 이라 합성하지 않았습니다.";
            logoReportType = MessageType.Warning;
            return;
        }

        Color[] logo = ResampleLogo(source, destWidth, destHeight);
        Color[] block = image.GetPixels(clampedLeft, clampedBottom, width, height);

        int offsetX = clampedLeft - left;
        int offsetY = clampedBottom - bottom;

        for (int y = 0; y < height; y++)
        {
            int logoRow = (offsetY + y) * destWidth + offsetX;
            int blockRow = y * width;

            for (int x = 0; x < width; x++)
            {
                Color premultiplied = logo[logoRow + x];
                float alpha = premultiplied.a * logoOpacity;

                if (alpha <= 0f)
                    continue;   // 완전히 투명한 픽셀은 배경을 그대로 둔다

                Color destination = block[blockRow + x];
                float keep = 1f - alpha;

                block[blockRow + x] = new Color(
                    premultiplied.r * logoOpacity + destination.r * keep,
                    premultiplied.g * logoOpacity + destination.g * keep,
                    premultiplied.b * logoOpacity + destination.b * keep,
                    destination.a);
            }
        }

        image.SetPixels(clampedLeft, clampedBottom, width, height, block);
        image.Apply();

        bool clipped = width != destWidth || height != destHeight;

        logoReport =
            $"로고 {destWidth}×{destHeight} px 를 ({clampedLeft}, {clampedBottom}) 에 합성했습니다 " +
            $"— 원본 {source.Width}×{source.Height}, 불투명도 {logoOpacity:0.##}" +
            (clipped ? $"\n화면 밖으로 잘렸습니다 ({width}×{height} px 만 들어갔습니다)." : string.Empty);

        logoReportType = clipped ? MessageType.Warning : MessageType.Info;
    }

    /// <summary>
    /// PNG 바이트를 직접 디코드해 원본 픽셀을 가져온다. 에셋과 Import Settings 는 건드리지 않는다.
    /// </summary>
    /// <remarks>
    /// 캐시하지 않는다 — 2172×724 를 <c>Color</c> 로 들면 25 MB 쯤이라 창이 들고 있기엔 아깝고,
    /// 미리보기를 누를 때 한 번 읽는 비용(1.3 MB 디코드)은 무시할 만하다.
    /// </remarks>
    private static LogoSource LoadLogoSource(out string note)
    {
        var result = new LogoSource();

        if (!File.Exists(LogoAssetPath))
        {
            note = $"로고 파일이 없습니다 — {LogoAssetPath}";
            return result;
        }

        Texture2D decoded = null;

        try
        {
            byte[] bytes = File.ReadAllBytes(LogoAssetPath);

            // linear 인자를 주지 않아 기본값(false)을 쓴다 — 합성 대상 이미지와 같은 설정이다.
            decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            if (!decoded.LoadImage(bytes))
            {
                note = $"로고 PNG 를 디코드하지 못했습니다 — {LogoAssetPath}";
                return result;
            }

            Color[] raw = decoded.GetPixels();
            var premultiplied = new Color[raw.Length];

            for (int i = 0; i < raw.Length; i++)
            {
                Color pixel = raw[i];
                premultiplied[i] = new Color(
                    pixel.r * pixel.a, pixel.g * pixel.a, pixel.b * pixel.a, pixel.a);
            }

            result.Premultiplied = premultiplied;
            result.Width = decoded.width;
            result.Height = decoded.height;

            note = $"{decoded.width}×{decoded.height}";
            return result;
        }
        catch (Exception error)
        {
            note = $"로고를 읽는 중 오류 — {error.Message}";
            return new LogoSource();
        }
        finally
        {
            if (decoded != null)
                DestroyImmediate(decoded);
        }
    }

    /// <summary>
    /// 로고를 목표 픽셀 크기로 다시 샘플링한다. 축소면 박스 평균, 확대면 바이리니어를 쓴다.
    /// </summary>
    /// <remarks>
    /// 기본값(가로 0.60)이면 2048 에서 1229 px, 1024 에서 614 px 이므로 원본 2172 px 를 둘 다
    /// 축소한다. 축소를 바이리니어로 하면 글자 외곽이 끊기므로 박스 평균이 필요하다.
    /// </remarks>
    private static Color[] ResampleLogo(LogoSource source, int destWidth, int destHeight)
    {
        var result = new Color[destWidth * destHeight];

        float scaleX = (float)source.Width / destWidth;
        float scaleY = (float)source.Height / destHeight;
        bool shrinking = scaleX > 1f || scaleY > 1f;

        for (int y = 0; y < destHeight; y++)
        {
            int row = y * destWidth;

            for (int x = 0; x < destWidth; x++)
            {
                result[row + x] = shrinking
                    ? SampleLogoBox(source, x * scaleX, y * scaleY, (x + 1) * scaleX, (y + 1) * scaleY)
                    : SampleLogoBilinear(
                        source, (x + 0.5f) * scaleX - 0.5f, (y + 0.5f) * scaleY - 0.5f);
            }
        }

        return result;
    }

    /// <summary>목표 픽셀 하나가 덮는 원본 영역의 평균. 프리멀티플라이드라 그냥 평균해도 맞다.</summary>
    private static Color SampleLogoBox(LogoSource source, float x0, float y0, float x1, float y1)
    {
        int left = Mathf.Clamp(Mathf.FloorToInt(x0), 0, source.Width - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(x1) - 1, left, source.Width - 1);
        int bottom = Mathf.Clamp(Mathf.FloorToInt(y0), 0, source.Height - 1);
        int top = Mathf.Clamp(Mathf.CeilToInt(y1) - 1, bottom, source.Height - 1);

        float r = 0f;
        float g = 0f;
        float b = 0f;
        float a = 0f;
        int count = 0;

        for (int y = bottom; y <= top; y++)
        {
            int row = y * source.Width;

            for (int x = left; x <= right; x++)
            {
                Color pixel = source.Premultiplied[row + x];
                r += pixel.r;
                g += pixel.g;
                b += pixel.b;
                a += pixel.a;
                count++;
            }
        }

        float inverse = 1f / Mathf.Max(1, count);
        return new Color(r * inverse, g * inverse, b * inverse, a * inverse);
    }

    private static Color SampleLogoBilinear(LogoSource source, float x, float y)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, source.Width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, source.Height - 1);
        int x1 = Mathf.Min(x0 + 1, source.Width - 1);
        int y1 = Mathf.Min(y0 + 1, source.Height - 1);

        float tx = Mathf.Clamp01(x - x0);
        float ty = Mathf.Clamp01(y - y0);

        Color bottomLeft = source.Premultiplied[y0 * source.Width + x0];
        Color bottomRight = source.Premultiplied[y0 * source.Width + x1];
        Color topLeft = source.Premultiplied[y1 * source.Width + x0];
        Color topRight = source.Premultiplied[y1 * source.Width + x1];

        return Color.Lerp(
            Color.Lerp(bottomLeft, bottomRight, tx),
            Color.Lerp(topLeft, topRight, tx),
            ty);
    }

    // ── Edit 모드 리그 평가 (A′) ──────────────────────────────────────────
    //
    // 왜 RigBuilder.Build() + Evaluate(0f) 로는 안 되는가 —
    //
    // RigBuilder.Build() 가 만드는 그래프에는 <b>애니메이션 입력 노드가 하나도 없다</b>.
    // 리그 노드와 출력만 있고, 그 출력은
    //     output.SetAnimationStreamSource(AnimationStreamSource.PreviousInputs);
    //     output.SetSortingOrder(1000);
    // 로 설정된다(RigBuilderUtils.cs:91-96). 즉 "이 Animator 에 먼저 평가된 다른 출력이
    // 스트림에 남긴 포즈 위에 얹는다"는 뜻이다. 런타임에는 Animator 컨트롤러 출력(정렬 0)이
    // 먼저 돌아 Breathing Idle 이 스트림에 들어 있으므로 정상 동작한다.
    //
    // Edit 모드에는 그 컨트롤러 출력이 없다. 그래서 스트림에 휴먼 포즈가 비어 있고,
    // 평가하면 스켈레톤이 Animator 원점으로 주저앉는다. 2026-11-01 실측 —
    //     root.x localPos  (0, -0.064, 0.828) -> (-0.001, 0.004, -0.054)
    //     IK Root 높이     1.169 m -> 0.287 m
    // localScale 과 Bounds 크기는 멀쩡하고 프러스텀도 IN 이었다. 컬링이 아니라 포즈 붕괴다.
    //
    // 해법 — Unity 자신의 Edit 모드 경로와 같은 모양으로 만든다.
    // Animation Rigging 의 Bake To Skeleton/Constraint 는 Edit 모드에서 리그를 평가하는
    // 공식 구현인데, 거기서도 그래프에 포즈 입력(AnimationClipPlayable)을 반드시 꽂는다
    // (BakeUtils.cs:78-116). 그래서 여기서도 같은 그래프 안에
    //
    //     [Breathing Idle 클립] -> 출력(정렬 0)        <- 스트림에 포즈 씨앗을 공급
    //     [리그 노드들]         -> 출력(정렬 1000)      <- RigBuilder.Build(graph) 가 심는다
    //
    // 두 출력을 두고 Manual 모드로 한 번 평가한다. 런타임의 배치와 같다.
    //
    // RigBuilder.Build() (무인수)가 아니라 public 오버로드 Build(PlayableGraph) 를 쓴다 —
    // 무인수 쪽은 제 그래프를 GameTime 으로 따로 만들어 버려 입력을 끼울 수 없다.

    /// <summary>Animator 컨트롤러의 기본 상태 클립 이름(CharacterAnimator.controller 에서 확인).</summary>
    private const string PoseIdleClipName = "Breathing Idle";

    /// <summary>
    /// Idle 을 샘플링할 시각(초). 0 = 클립 첫 프레임.
    /// 엔딩은 호흡 주기 중 임의 시점이지만, 촬영은 결정적인 한 장이어야 하므로 고정한다.
    /// </summary>
    private const float PoseIdleSampleTime = 0f;

    /// <summary>한 캐릭터용 Edit 모드 평가 그래프. <c>Valid</c> 가 false 면 구성에 실패한 것이다.</summary>
    private struct PoseRigGraph
    {
        public bool Valid;
        public PlayableGraph Graph;
        public AnimationClipPlayable Clip;
        public RigBuilder Builder;
        public string Note;
    }

    /// <summary>
    /// Idle 입력 + 리그 노드를 한 그래프에 구성한다. 평가는 <see cref="EvaluatePoseRigGraph"/> 가 한다
    /// (그 사이에 진단 스냅샷을 떠야 하므로 둘로 나눠 두었다).
    /// </summary>
    /// <remarks>
    /// 정리는 <paramref name="restore"/> 에 쌓는다. <c>Render</c> 가 역순으로 실행하므로
    /// <b>먼저 등록한 <c>Clear()</c> 가 나중에 돈다</b> — 그래프의 플레이어블이 리그 잡 데이터를
    /// 참조하므로 반드시 "그래프 파괴 → <c>Clear()</c>" 순이어야 하고, 그래서 이 순서로 등록한다.
    /// </remarks>
    private static PoseRigGraph TryBuildPoseRigGraph(
        RigBuilder builder, PoseActorSpec spec, List<Action> restore, StringBuilder log)
    {
        var result = new PoseRigGraph { Builder = builder };

        RigBuilder capturedBuilder = builder;
        restore.Add(() =>
        {
            if (capturedBuilder != null)
                capturedBuilder.Clear();
        });

        // RigBuilder 는 [RequireComponent(typeof(Animator))] 라 같은 오브젝트에 있어야 한다.
        Animator animator = builder.GetComponent<Animator>();

        if (animator == null)
        {
            result.Note = ReportPoseFailure(
                log, $"{spec.Label}: RigBuilder 와 같은 오브젝트에 Animator 가 없습니다.");
            return result;
        }

        AnimationClip idle = FindPoseIdleClip(animator, out string clipNote);

        if (idle == null)
        {
            result.Note = ReportPoseFailure(log, $"{spec.Label}: Idle 클립을 찾지 못했습니다 — {clipNote}");
            return result;
        }

        // Manual — 에디터 플레이 루프가 그래프를 제멋대로 틱하지 않게 한다(BakeUtils 와 동일).
        PlayableGraph graph = PlayableGraph.Create($"KeyArtPose_{spec.Label}");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);

        PlayableGraph capturedGraph = graph;
        restore.Add(() =>
        {
            if (capturedGraph.IsValid())
                capturedGraph.Destroy();
        });

        var clip = AnimationClipPlayable.Create(graph, idle);
        clip.SetApplyFootIK(false);   // 발 IK 보정이 포즈를 흔들지 않게 한다(BakeUtils 와 동일)

        // 정렬순서를 주지 않으므로 0 — 리그 출력(1000)보다 먼저 평가되어 스트림을 채운다.
        AnimationPlayableOutput idleOutput =
            AnimationPlayableOutput.Create(graph, $"{spec.Label} Idle", animator);
        idleOutput.SetSourcePlayable(clip);

        // 리그 노드를 같은 그래프에 심는다. 실패하면 조용히 넘기지 않는다.
        if (!builder.Build(graph))
        {
            result.Note = ReportPoseFailure(
                log,
                $"{spec.Label}: RigBuilder.Build(graph) 가 false 를 돌려줬습니다 " +
                "(리그 레이어가 비었거나 이미 Preview 중입니다).");
            return result;
        }

        result.Valid = true;
        result.Graph = graph;
        result.Clip = clip;
        result.Note =
            $"Idle 입력 '{idle.name}' ({clipNote}) 를 {PoseIdleSampleTime:0.##}s 로 샘플링하고 IK 를 얹었습니다.";

        return result;
    }

    /// <summary>구성된 그래프를 한 번 평가한다. 여기서 몸은 Idle 이 되고 손은 IK 가 끌어간다.</summary>
    private static void EvaluatePoseRigGraph(PoseRigGraph rig)
    {
        // 같은 시각을 두 번 넣는다 — 플레이어블의 "이전 시각"까지 맞춰 보간 없이 그 프레임만 뽑는다.
        rig.Clip.SetTime(PoseIdleSampleTime);
        rig.Clip.SetTime(PoseIdleSampleTime);

        rig.Graph.Play();

        // RigBuilder.Evaluate 가 내부에서 하는 일을 직접 한다 — 제약의 씬 값을 스트림에 동기화한다.
        rig.Builder.SyncLayers();

        rig.Graph.Evaluate(0f);
    }

    /// <summary>
    /// 컨트롤러에서 Idle 클립을 찾는다. 못 찾으면 <c>null</c> 과 함께 이유를 돌려준다.
    /// </summary>
    private static AnimationClip FindPoseIdleClip(Animator animator, out string note)
    {
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;

        if (controller == null)
        {
            note = $"'{animator.name}' 의 Animator 에 컨트롤러가 없습니다";
            return null;
        }

        AnimationClip[] clips = controller.animationClips;

        if (clips == null || clips.Length == 0)
        {
            note = $"컨트롤러 '{controller.name}' 에 클립이 없습니다";
            return null;
        }

        foreach (AnimationClip clip in clips)
        {
            if (clip != null && clip.name == PoseIdleClipName)
            {
                note = $"컨트롤러 '{controller.name}' 의 기본 상태 클립";
                return clip;
            }
        }

        // 이름이 바뀌었을 때를 위한 보조 경로. 썼다는 사실을 반드시 알린다.
        foreach (AnimationClip clip in clips)
        {
            if (clip != null && clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                note = $"'{PoseIdleClipName}' 이 없어 이름에 idle 이 든 '{clip.name}' 을 대신 썼습니다";
                return clip;
            }
        }

        var names = new List<string>(clips.Length);

        foreach (AnimationClip clip in clips)
            names.Add(clip != null ? clip.name : "(null)");

        note =
            $"'{PoseIdleClipName}' 도, 이름에 idle 이 든 클립도 없습니다. " +
            $"컨트롤러 '{controller.name}' 의 클립: {string.Join(", ", names)}";

        return null;
    }

    /// <summary>창과 Console 양쪽에 남긴다 — 포즈가 조용히 틀어지는 것보다 시끄러운 쪽이 낫다.</summary>
    private static string ReportPoseFailure(StringBuilder log, string message)
    {
        log.AppendLine(message);
        Debug.LogError($"[KeyArtPose] {message}");

        return message;
    }

    // ── 포즈 진단 ─────────────────────────────────────────────────────────
    //
    // 알고 싶은 것은 하나다 — <c>Evaluate(0f)</c> 직후에 스켈레톤과 렌더링 Bounds 가
    // 실제로 붕괴하는가. 그래서 같은 값을 세 시점에 떠서 나란히 찍는다.
    //
    // 왜 Bounds 를 보는가 — 이 캐릭터의 SkinnedMeshRenderer 는 모두
    // <c>m_UpdateWhenOffscreen: 0</c> 이고 <c>m_RootBone</c> 이 root.x 다. 즉 컬링 Bounds 는
    // "프리팹에 구워 둔 m_AABB × root.x 의 월드 행렬"이다. 본이 망가지면 Bounds 가 먼저
    // 무너지고, 그러면 프러스텀 컬링에 걸려 오류 하나 없이 그려지지 않는다.

    /// <summary>한 시점의 본·Bounds 를 그대로 떠 놓는다. 읽기만 하고 아무것도 쓰지 않는다.</summary>
    private struct PoseSnapshot
    {
        public string Stage;

        public bool HasSkinRoot;
        public Vector3 SkinRootLocalPosition;
        public Vector3 SkinRootLocalScale;

        public bool HasIkBones;
        public string IkBoneNames;
        public Vector3 IkRootPosition;
        public Vector3 IkRootScale;
        public Vector3 IkMidPosition;
        public Vector3 IkMidScale;
        public Vector3 IkTipPosition;
        public Vector3 IkTipScale;

        public int RendererCount;
        public int EnabledRendererCount;

        public bool HasBounds;
        public Bounds Bounds;
        public bool InFrustum;

        public bool HasTarget;
        public float TipToTarget;

        /// <summary>어느 축이든 사람 메시라고 보기 어려울 만큼 작아졌는가.</summary>
        public bool BoundsCollapsed =>
            HasBounds && (Bounds.size.x < PoseCollapsedBoundsSize ||
                          Bounds.size.y < PoseCollapsedBoundsSize ||
                          Bounds.size.z < PoseCollapsedBoundsSize);
    }

    /// <summary>
    /// 지금 구도의 프러스텀 평면. 임시 카메라는 이 시점에 아직 없으므로(렌더 직전에 만든다)
    /// 창의 값으로 직접 만든다. 촬영이 1:1 정사각이라 aspect 는 1 이다.
    /// </summary>
    /// <remarks>
    /// <c>Matrix4x4.TRS</c> 의 스케일 z 를 -1 로 주는 것은 Unity 뷰 행렬이 Z 를 뒤집기 때문이다
    /// (<c>Camera.worldToCameraMatrix</c> 와 같은 형태를 손으로 만드는 표준적인 방법).
    /// </remarks>
    private Plane[] CalculateCaptureFrustum()
    {
        Matrix4x4 projection = Matrix4x4.Perspective(
            fieldOfView, 1f, nearClip, Mathf.Max(farClip, 1000f));

        Matrix4x4 view = Matrix4x4.TRS(
            cameraPosition, Quaternion.Euler(cameraEuler), new Vector3(1f, 1f, -1f)).inverse;

        return GeometryUtility.CalculateFrustumPlanes(projection * view);
    }

    private static PoseSnapshot TakePoseSnapshot(
        string stage, GameObject root, TwoBoneIKConstraint hand, Vector3 goal, Plane[] frustum)
    {
        var snapshot = new PoseSnapshot { Stage = stage };

        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
        {
            if (node.name != PoseSkinRootBoneName)
                continue;

            snapshot.HasSkinRoot = true;
            snapshot.SkinRootLocalPosition = node.localPosition;
            snapshot.SkinRootLocalScale = node.localScale;
            break;
        }

        if (hand != null)
        {
            Transform ikRoot = hand.data.root;
            Transform ikMid = hand.data.mid;
            Transform ikTip = hand.data.tip;

            if (ikRoot != null && ikMid != null && ikTip != null)
            {
                snapshot.HasIkBones = true;
                snapshot.IkBoneNames = $"{ikRoot.name} / {ikMid.name} / {ikTip.name}";
                snapshot.IkRootPosition = ikRoot.position;
                snapshot.IkRootScale = ikRoot.localScale;
                snapshot.IkMidPosition = ikMid.position;
                snapshot.IkMidScale = ikMid.localScale;
                snapshot.IkTipPosition = ikTip.position;
                snapshot.IkTipScale = ikTip.localScale;

                snapshot.HasTarget = true;
                snapshot.TipToTarget = Vector3.Distance(ikTip.position, goal);
            }
        }

        // 렌더러 Bounds 는 월드 공간이다. 활성인 것만 합쳐야 실제 컬링 대상과 같아진다.
        foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            snapshot.RendererCount++;

            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            snapshot.EnabledRendererCount++;

            if (snapshot.HasBounds)
            {
                Bounds merged = snapshot.Bounds;
                merged.Encapsulate(renderer.bounds);
                snapshot.Bounds = merged;
            }
            else
            {
                snapshot.Bounds = renderer.bounds;
                snapshot.HasBounds = true;
            }
        }

        if (snapshot.HasBounds && frustum != null)
            snapshot.InFrustum = GeometryUtility.TestPlanesAABB(frustum, snapshot.Bounds);

        return snapshot;
    }

    /// <summary>Console 에 찍을 표. 단계 이름을 ASCII 로 두어 열이 어긋나지 않게 한다.</summary>
    private static string BuildPoseDiagnostics(PoseActorSpec spec, List<PoseSnapshot> snapshots)
    {
        PoseSnapshot first = snapshots[0];

        var text = new StringBuilder();
        text.AppendLine($"[KeyArtPose] {spec.Label} — IK 평가 전후 비교 ({spec.HandIkName})");
        text.AppendLine(
            $"  SkinnedMeshRenderer {first.RendererCount}개 / 활성 {first.EnabledRendererCount}개      " +
            (first.HasIkBones
                ? $"IK Root/Mid/Tip = {first.IkBoneNames}"
                : "IK 본을 읽지 못했습니다"));
        text.AppendLine();

        text.AppendLine(
            "  stage                " +
            $"{PoseColumn($"{PoseSkinRootBoneName} localPos", 28)}" +
            $"{PoseColumn($"{PoseSkinRootBoneName} localScale", 28)}" +
            $"{PoseColumn("Bounds center", 28)}{PoseColumn("Bounds size", 28)}" +
            $"{PoseColumn("frustum", 10)}tip->target");

        foreach (PoseSnapshot s in snapshots)
        {
            text.AppendLine(
                $"  {PoseColumn(s.Stage, 21)}" +
                $"{PoseColumn(PoseVector(s.HasSkinRoot, s.SkinRootLocalPosition), 28)}" +
                $"{PoseColumn(PoseVector(s.HasSkinRoot, s.SkinRootLocalScale), 28)}" +
                $"{PoseColumn(PoseVector(s.HasBounds, s.Bounds.center), 28)}" +
                $"{PoseColumn(PoseVector(s.HasBounds, s.Bounds.size), 28)}" +
                $"{PoseColumn(s.HasBounds ? (s.InFrustum ? "IN" : "OUT") : "-", 10)}" +
                (s.HasTarget ? $"{s.TipToTarget:0.000} m" : "-"));
        }

        text.AppendLine();
        text.AppendLine("  IK bones — world position / localScale");

        foreach (PoseSnapshot s in snapshots)
        {
            if (!s.HasIkBones)
            {
                text.AppendLine($"  {PoseColumn(s.Stage, 21)}(읽지 못했습니다)");
                continue;
            }

            text.AppendLine(
                $"  {PoseColumn(s.Stage, 21)}Root {PoseVector(true, s.IkRootPosition)}  " +
                $"scale {PoseVector(true, s.IkRootScale)}");
            text.AppendLine(
                $"  {PoseColumn(string.Empty, 21)}Mid  {PoseVector(true, s.IkMidPosition)}  " +
                $"scale {PoseVector(true, s.IkMidScale)}");
            text.AppendLine(
                $"  {PoseColumn(string.Empty, 21)}Tip  {PoseVector(true, s.IkTipPosition)}  " +
                $"scale {PoseVector(true, s.IkTipScale)}");
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>창의 정보 박스에 넣을 한 줄 요약. 상세는 Console 에 있다.</summary>
    private static string SummarizePoseDiagnostics(PoseActorSpec spec, List<PoseSnapshot> snapshots)
    {
        PoseSnapshot last = snapshots[snapshots.Count - 1];

        string verdict;

        if (!last.HasBounds)
            verdict = "활성 렌더러가 없습니다";
        else if (last.BoundsCollapsed)
            verdict = $"Bounds 가 붕괴했습니다 (크기 {PoseVector(true, last.Bounds.size)})";
        else if (!last.InFrustum)
            verdict = "Bounds 는 정상이지만 카메라 프러스텀 밖입니다";
        else
            verdict = $"Bounds 정상 · 프러스텀 안 (크기 {PoseVector(true, last.Bounds.size)})";

        return $"{spec.Label} [{last.Stage.Trim()}]: {verdict} — 상세는 Console 참조";
    }

    private static string PoseVector(bool has, Vector3 value) =>
        has ? $"({value.x,7:0.000},{value.y,7:0.000},{value.z,7:0.000})" : "(읽지 못함)";

    /// <summary>표의 열을 맞춘다. 내용이 열보다 길면 자르지 않고 공백 하나만 붙여 넘긴다.</summary>
    private static string PoseColumn(string value, int width) =>
        value.Length >= width ? value + " " : value.PadRight(width);

    /// <summary>
    /// 임시 인스턴스임을 못박는다. <c>DontSave</c> 가 붙으면 씬을 저장해도 들어가지 않는다
    /// (자식까지 붙여야 한쪽만 남는 일이 없다).
    /// </summary>
    private static void MarkPoseTemporary(GameObject root)
    {
        foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
            node.gameObject.hideFlags = HideFlags.DontSave;
    }

    /// <summary>
    /// <c>Stage3EndingCutscene.StepActorsCloser</c> 와 같은 식. 두 사람을 수평으로 서로에게 당긴다.
    /// MeetPose 는 두 사람의 가운데라 그대로 두므로 여기서 옮기지 않는다.
    /// </summary>
    private static void StepPoseActorsCloser(Transform a, Transform b)
    {
        Vector3 axis = Vector3.ProjectOnPlane(b.position - a.position, Vector3.up);
        float gap = axis.magnitude;

        if (gap < 0.0001f)
            return;

        float step = Mathf.Min(EndingCloseGap, gap * 0.5f) * 0.5f;
        axis /= gap;
        a.position += axis * step;
        b.position -= axis * step;
    }

    /// <summary>
    /// <c>Stage3EndingCutscene.PrepareActor</c> 와 같은 준비. 손 IK 제약을 돌려준다.
    /// </summary>
    /// <remarks>
    /// 게임플레이 스크립트(XR·네트워크·BodyFollow 등)는 꺼 둔다. Edit 모드에서는 보통 돌지 않지만
    /// 포즈를 건드릴 여지를 아예 차단하는 쪽이 안전하고, 꺼진 채로 통째로 지워지므로 되돌릴 것도 없다.
    /// </remarks>
    private static TwoBoneIKConstraint PreparePoseActor(
        GameObject root, string handIkName, string label, StringBuilder log)
    {
        foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null)
                continue;   // 스크립트가 빠진 컴포넌트

            if (behaviour is IRigConstraint constraint)
            {
                constraint.weight = 0f;   // 몸은 Animator 에 맡긴다. 손 IK 만 아래에서 다시 켠다.
                continue;
            }

            if (behaviour is RigBuilder || behaviour is Rig)
                continue;

            behaviour.enabled = false;
        }

        TwoBoneIKConstraint hand = null;

        foreach (TwoBoneIKConstraint candidate in root.GetComponentsInChildren<TwoBoneIKConstraint>(true))
        {
            if (candidate.gameObject.name != handIkName)
                continue;

            hand = candidate;
            break;
        }

        if (hand == null)
        {
            log.AppendLine($"{label}: {handIkName} 제약을 찾지 못했습니다.");
            return null;
        }

        if (hand.data.target == null)
        {
            log.AppendLine($"{label}: {handIkName} 의 Target 이 비어 있습니다.");
            return null;
        }

        // 팔꿈치 힌트가 위팔(arm_stretch)의 자식이면 IK 와 서로 밀어 팔이 떨린다.
        // 엔딩씬과 똑같이 캐릭터 루트 아래로 꺼낸다(월드 위치는 유지).
        Transform hint = hand.data.hint;
        Transform upperArm = hand.data.root;

        if (hint != null && upperArm != null && hint.IsChildOf(upperArm))
        {
            hint.SetParent(root.transform, true);
            log.AppendLine($"{label}: 팔꿈치 힌트를 {upperArm.name} 밖으로 꺼냈습니다.");
        }

        return hand;
    }

    /// <summary>
    /// <c>ApplyHandReach(1)</c> + <c>AimElbow</c> 와 같은 계산. 타깃·힌트만 놓고 IK 는 풀지 않는다.
    /// </summary>
    private static void ApplyPoseHandReach(
        GameObject root, TwoBoneIKConstraint hand, Vector3 meetPosition, Quaternion meetRotation)
    {
        hand.data.target.SetPositionAndRotation(meetPosition, meetRotation);

        hand.weight = 1f;
        hand.data.targetPositionWeight = 1f;
        hand.data.targetRotationWeight = EndingHandRotationWeight;

        Transform hint = hand.data.hint;
        Transform shoulderBone = hand.data.root;

        if (hint == null || shoulderBone == null)
            return;

        Vector3 shoulder = shoulderBone.position;
        Vector3 outward = Vector3.ProjectOnPlane(shoulder - root.transform.position, Vector3.up);
        outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.zero;

        hand.data.hintWeight = 1f;
        hint.position = Vector3.Lerp(shoulder, meetPosition, 0.5f)
                        + Vector3.down * EndingElbowDrop
                        + outward * EndingElbowOut;
    }

    /// <summary>촬영용 임시 상태. 되돌리는 방법을 <paramref name="restore"/> 에 쌓아 둔다.</summary>
    private void ApplyTemporaryState(Scene scene, List<Action> restore, bool includePose)
    {
        // 공명 포즈는 가장 먼저 만든다 — 아래의 Outline 끄기가 이 모델에도 같이 적용되어야 한다.
        // 레터링 배경은 캐릭터를 쓰지 않으므로 아예 만들지 않는다.
        if (includePose)
            ApplyResonancePose(scene, restore);

        if (enableWeather)
        {
            foreach (string name in WeatherRootNames)
            {
                foreach (GameObject root in FindRoots(scene, name))
                {
                    GameObject captured = root;
                    bool wasActive = captured.activeSelf;

                    if (!wasActive)
                        captured.SetActive(true);

                    restore.Add(() =>
                    {
                        if (captured != null)
                            captured.SetActive(wasActive);
                    });

                    WarmUpParticles(captured, restore);
                }
            }
        }

        if (hideCanvas)
        {
            foreach (string name in HideRootNames)
            {
                foreach (GameObject root in FindRoots(scene, name))
                {
                    GameObject captured = root;
                    bool wasActive = captured.activeSelf;

                    if (!wasActive)
                        continue;

                    captured.SetActive(false);
                    restore.Add(() =>
                    {
                        if (captured != null)
                            captured.SetActive(true);
                    });
                }
            }
        }

        if (hideOutlines)
        {
            foreach (Outline outline in
                     UnityEngine.Object.FindObjectsByType<Outline>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Outline captured = outline;

                if (!captured.enabled)
                    continue;

                captured.enabled = false;
                restore.Add(() =>
                {
                    if (captured != null)
                        captured.enabled = true;
                });
            }
        }
    }

    /// <summary>
    /// 파티클을 미리 돌려 비가 화면을 채운 상태로 만든다.
    /// </summary>
    /// <remarks>
    /// Edit 모드에서는 파티클이 스스로 돌지 않으므로 그냥 찍으면 비가 없다.
    /// <c>withChildren: true</c> 로 한 번에 돌리기 위해, 위에 다른 ParticleSystem 이 없는
    /// <b>최상위 시스템만</b> 골라 호출한다(자식까지 또 부르면 이중으로 돌아간다).
    /// 끝나면 <c>Clear</c> 로 비워 원래의 "돌지 않는 상태"로 되돌린다.
    /// </remarks>
    private void WarmUpParticles(GameObject root, List<Action> restore)
    {
        foreach (ParticleSystem system in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (!IsTopLevelSystem(system))
                continue;

            ParticleSystem captured = system;
            captured.Simulate(weatherWarmupSeconds, true, true);

            restore.Add(() =>
            {
                if (captured == null)
                    return;

                captured.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                captured.Clear(true);
            });
        }
    }

    private static bool IsTopLevelSystem(ParticleSystem system)
    {
        Transform parent = system.transform.parent;
        return parent == null || parent.GetComponentInParent<ParticleSystem>() == null;
    }

    // ───────────────────────────── 보조 ─────────────────────────────

    private static bool TryGetScene(out Scene result)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);

            if (!scene.isLoaded || scene.name != SceneName)
                continue;

            result = scene;
            return true;
        }

        result = default;
        return false;
    }

    private static List<GameObject> FindRoots(Scene scene, string name)
    {
        var found = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                found.Add(root);
        }

        return found;
    }

    private void DestroyPreview()
    {
        if (preview == null)
            return;

        DestroyImmediate(preview);
        preview = null;
    }

    private void SetStatus(string message, MessageType type)
    {
        status = message;
        statusType = type;
        Repaint();
    }
}
