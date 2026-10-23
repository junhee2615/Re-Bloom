using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

/// <summary>
/// Stage3 씬에 엔딩 컷씬(<see cref="Stage3EndingCutscene"/>)을 구성하는 셋업 도구.
///
/// 만들어지는 계층 —
/// <code>
/// Stage3Ending                 ← Stage3EndingCutscene + PlayableDirector
/// ├── CameraRig                ← 엔딩 전용 CinemachineBrain (카메라 없음, XR Origin이 이 위치를 따라감)
/// ├── Cameras  (꺼짐)          ← CinemachineCamera 4대 (Output Channel 01)
/// ├── Actors
/// │   ├── Ending_mental (꺼짐) ← 씬에 배치된 Player_mental 복제본. 네트워크·XR 컴포넌트를 떼어 낸 인형
/// │   ├── Ending_ear    (꺼짐) ← 같은 방식의 Player_ear 복제본
/// │   ├── MeetPose_mental      ← 손바닥이 닿았을 때 손 위치·방향 (mental 왼손)
/// │   ├── MeetPose_ear         ← (ear 오른손)
/// │   ├── WalkTarget_mental    ← 온실 밖으로 걸어 나와 멈출 자리·바라볼 방향
/// │   ├── WalkTarget_ear
/// │   └── WalkCenter           ← 걷는 두 사람의 가운데. 따라가는 카메라의 대상
/// ├── HandGlow                 ← Point Light + 빛 파티클
/// └── Credits                  ← 엔딩 크레딧(EndingCredits). 장면 이미지·노래·폰트를 여기에 넣는다
/// </code>
/// 타임라인은 <see cref="TimelinePath"/>에 만든다.
///
/// 왜 원본 Player_mental / Player_ear를 그대로 쓰지 않는가:
/// 플레이어 프리팹이라 NetworkObject·NetworkTransform·TrackedPoseDriver가 붙어 있다.
/// 스폰되지 않은 채 켜면 Networked 프로퍼티 접근에서 예외가 나고, TrackedPoseDriver가
/// 손 타깃을 내 컨트롤러 위치로 끌고 간다. 그래서 배치·포즈는 그대로 복제하고 그런 컴포넌트만 뗀다.
///
/// 안전 규칙 —
///  - 이미 있는 오브젝트의 <b>Transform 값은 덮어쓰지 않는다</b>. 카메라·MeetPose·WalkTarget 위치는
///    처음 만들 때만 계산해 넣고, 그 뒤에는 씬에서 손으로 맞춘 값을 존중한다.
///  - 단, 엔딩 구성(<see cref="LayoutVersion"/>)이 바뀌었으면 카메라·타임라인·MeetPose·WalkTarget을
///    새 구성으로 다시 만든다. 캐릭터와 HandGlow는 그대로 둔다.
///  - 원본 Player_mental / Player_ear는 읽기만 한다.
///
/// Dry Run으로 무엇이 바뀔지 먼저 확인하고, Apply로 실제 반영한다(씬은 직접 저장).
/// </summary>
public static class Stage3EndingSetup
{
    // 엔딩 구성이 바뀔 때 올린다. 씬의 구성 버전과 다르면 Apply가 카메라·타임라인·마커를 다시 만든다.
    //  1: 공명 → 전경
    //  2: 손 올려 맞대기 → 걸어 나오기 → 캐릭터 뒤 시점에서 식생 그룹 성장
    //  3: 엔딩코어 쪽에서 공명, 걷는 동안 따라가는 카메라, 절벽 앞까지 걷기, 손바닥 간격 넓힘
    //  4: 공명도 따라가는 카메라 시점으로, 절벽에서 더 떨어져 멈춤, 멈춘 뒤 수변을 내려다보며 둘러봄
    //  5: 20걸음만 걷기, 멈추면 위로 올라가 수변+Building을 보며 식생 성장, 5초 페이드
    //  6: 멈춘 뒤 5m 올라감, 자라는 동안 좌우로 둘러봄
    //  7: 올라가며 바로 왼쪽을 보고, 왼쪽 → 오른쪽 한 번의 파노라마. 살짝 내려다보는 각도
    //  8: 둘러본 뒤 2초 머물렀다가 페이드 아웃
    //  9: 손바닥이 평행하게 마주 보도록(손가락 수직, 손목 회전 100%, 간격 5cm), 페이드 2.5초
    //  10: 효과음(WorldRestore) — 손바닥이 닿을 때, 식생이 자라기 시작할 때
    //  11: 효과음은 엔딩 시작 때 한 번만
    //  12: 크레딧 장면 하나 4초(녹화 4초)에 맞춰 크레딧 타이밍 조정
    //  13: 크레딧 사진 확대 없음·전환 1.5초·글씨 뒤 번짐 진하게, 마지막에 캐릭터 사진
    //  14: v13에서 다른 자리의 Player_* 기준으로 잘못 계산된 MeetPose·WalkTarget을 Ending_* 기준으로 다시 만든다
    //  15: 크레딧 마지막을 온실 앞 손 흔들기 실시간 컷으로(캐릭터 사진 대신), 글씨 뒤 번짐 왼쪽 끝 완전 검정
    //  16: 손 흔들기를 팔꿈치 굽혀 인사하듯, 손 흔들기 장면에서는 글씨 뒤 번짐 없음
    //  17: 손 흔들기를 손목이 아니라 팔꿈치 축으로 아래팔 전체가 흔들리게
    //  18: 크레딧 높이(Height Offset 0.92)
    //  19: 크레딧 높이(Height Offset 1.1)
    //  20: 공명 장면 3초 길게(손 맞댄 채 7.5초), 크레딧 The End 이미지
    //  21: 공명 때 손 쪽으로 다가갔다가 물러나는 카메라, 손 흔들 때 두 사람 간격 벌림
    //  22: 손 흔들기 전에 양옆에서 걸어 들어오기(컷 8초), The End 로고 크기 650
    private const int LayoutVersion = 22;

    private const string SceneName = "Stage3";
    private const string TimelinePath = "Assets/ReBloom/CutScenes/Stage3/Stage3EndingTimeline.playable";
    private const string GlowMaterialPath = "Assets/ReBloom/CutScenes/Stage3/EndingGlowParticle.mat";
    private const string RestoreSoundPath = "Assets/ReBloom/Sounds/Basic/WorldRestore.mp3";
    private const string FootstepSoundPath = "Assets/ReBloom/AssetStore/Rain Forest SFX/Footstep/Grass Footstep Long.wav";

    // 엔딩 크레딧
    private const string CreditsName = "Credits";
    private const string CreditsMusicPath = "Assets/ReBloom/Sounds/Ending/After Sunset.mp3";
    private const string CreditsFontPath = "Assets/ReBloom/UI/Fonts/KoPub Dotum Medium SDF.asset";
    private const string TheEndImagePath = "Assets/ReBloom/CutScenes/Credits/TheEnd.png";

    private static readonly (string role, string name)[] CreditLines =
    {
        ("기획", "강서현"),
        ("개발", "김성희"),
        ("개발", "윤신혜"),
        ("배경 모델링", "배준희"),
        ("캐릭터 모델링", "류현지"),
    };

    private const string RootName = "Stage3Ending";
    private const string CameraRigName = "CameraRig";
    private const string CamerasName = "Cameras";
    private const string ActorsName = "Actors";
    private const string GlowName = "HandGlow";
    private const string GreenhouseName = "Greenhouse";
    private const string GrowGroupName = "Flower&Tree&Building&Ivy";
    private const string BuildingName = "Building";
    private const string WalkCenterName = "WalkCenter";
    private const string WaveCameraName = "WaveCamera";

    // 역할마다 올릴 손. 마주 선 두 사람의 mental 왼손과 ear 오른손은 같은 쪽에 있어 거울처럼 맞닿는다.
    private static readonly string[] RoleNames = { "mental", "ear" };
    private static readonly bool[] RaiseRightHand = { false, true };

    private const OutputChannels EndingChannel = OutputChannels.Channel01;

    // 손 뼈(손목)가 만남 지점에서 자기 쪽으로 물러서는 거리(m). 손 두께의 절반쯤이어야
    // 손바닥 겉면끼리 닿고 손이 서로 파고들지 않는다.
    private const float PalmGap = 0.05f;

    // 손목을 MeetPose 방향으로 돌리는 정도. 1보다 작으면 손바닥이 비스듬히 남아
    // 손가락끼리 엇갈려 깍지를 낀 것처럼 보인다.
    private const float HandRotationWeight = 1f;

    // 어깨 높이에서 이만큼 올린 곳(얼굴 높이 즈음)에서 손바닥이 만난다(m).
    private const float MeetAboveShoulder = 0.12f;

    // 엔딩 시작 때 두 사람이 좁히는 간격(m). 너무 좁히면 맞댄 손이 서로 파고든다.
    private const float CloseGap = 0.1f;

    // 걸어 나갈 거리: 20걸음(보폭 0.7m). 그 전에 절벽이 있으면 절벽 끝에서 StopBeforeCliff 앞에 멈춘다.
    private const int WalkSteps = 20;
    private const float StepLength = 0.7f;
    private const float MaxWalkDistance = 40f;   // 절벽을 찾아볼 범위(m)
    private const float CliffDrop = 0.8f;        // 0.5m 사이에 이만큼 이상 꺼지면(급경사) 절벽으로 본다(m)
    private const float StopBeforeCliff = 5f;    // 절벽 끝에서 이만큼 앞에 멈춘다(m). 너무 붙으면 떨어질 것처럼 보인다
    private const float StandApart = 1.1f;       // 멈춘 두 사람 사이 간격(m)

    // 멈춘 뒤 카메라가 두 사람 뒤에서 이만큼(m) 올라가 내려다본다
    private const float RiseHeight = 5f;

    // 올라간 뒤 식생이 자라는 동안 가운데에서 좌우로 이만큼(도) 둘러본다
    private const float LookAroundYaw = 35f;

    // 둘러볼 때 내려다보는 각도(도). 살짝 위에서 아래를 보는 정도.
    private const float LookDownPitch = 15f;

    // 걷는 속도(m/s). 걷는 클립 길이를 정할 때 쓴다.
    private const float WalkSpeed = 1.2f;

    [MenuItem("Re_Bloom/Stage3/Ending Cutscene Setup - Dry Run", priority = 400)]
    private static void DryRun() => Run(false);

    [MenuItem("Re_Bloom/Stage3/Ending Cutscene Setup - Apply", priority = 401)]
    private static void Apply() => Run(true);

    private static void Run(bool apply)
    {
        string mode = apply ? "APPLY" : "DRY RUN";
        Debug.Log($"===== Stage3 엔딩 컷씬 셋업 [{mode}] 구성 v{LayoutVersion} =====");

        // Play 모드에서 바꾼 씬은 Play를 끄면 전부 되돌아간다
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[엔딩 셋업] Play 모드에서는 실행할 수 없습니다. Play를 끄고 다시 실행하세요.");
            EditorUtility.DisplayDialog("엔딩 컷씬 셋업",
                "Play 모드에서는 실행할 수 없습니다.\nPlay 모드에서 바꾼 씬은 Play를 끄면 모두 되돌아갑니다.\n\nPlay를 끄고 다시 실행하세요.", "확인");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != SceneName)
        {
            Debug.LogError($"[엔딩 셋업] {SceneName} 씬을 열고 실행하세요. (지금: {scene.name})");
            return;
        }

        // ── 1. 재료 찾기 ───────────────────────────────────────────────
        Transform existingRoot = FindInScene(scene, RootName, null);

        var sources = new Transform[RoleNames.Length];
        for (int i = 0; i < RoleNames.Length; i++)
        {
            // 이미 만든 엔딩용 복제본(실제로 엔딩에 나오는 캐릭터)이 있으면 늘 그것을 기준으로 삼는다.
            // 씬에 같은 이름의 Player_* 가 다른 자리에 또 놓여 있어도 손 위치가 엉뚱하게 계산되지 않도록.
            if (existingRoot != null)
                sources[i] = existingRoot.Find($"{ActorsName}/Ending_{RoleNames[i]}");

            // 처음 만들 때만 씬에 배치된 원본을 복제한다
            if (sources[i] == null)
                sources[i] = FindInScene(scene, "Player_" + RoleNames[i], existingRoot);

            if (sources[i] == null)
            {
                Debug.LogError($"[엔딩 셋업] 씬에서 Player_{RoleNames[i]}도 Ending_{RoleNames[i]}도 찾지 못했습니다.");
                return;
            }
        }

        Transform greenhouse = FindInScene(scene, GreenhouseName, existingRoot);
        Transform growGroup = FindInScene(scene, GrowGroupName, existingRoot);
        VegetationGrowth vegetation = Object
            .FindObjectsByType<VegetationGrowth>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault();

        Debug.Log($"[엔딩 셋업] 캐릭터 기준: {Path(sources[0])}, {Path(sources[1])}");
        Debug.Log($"[엔딩 셋업] 온실: {(greenhouse != null ? Path(greenhouse) : "없음 — 걸어 나갈 방향을 캐릭터 기준으로 정합니다")}");
        Debug.Log($"[엔딩 셋업] 자라는 그룹: {(growGroup != null ? Path(growGroup) + $" (자식 {growGroup.childCount}개)" : "없음 — 그룹 성장 연출이 빠집니다")}");
        Debug.Log($"[엔딩 셋업] 식생 매니저: {(vegetation != null ? Path(vegetation.transform) : "없음")}");

        // 올릴 팔: mental 왼손, ear 오른손
        var reachIKs = new TwoBoneIKConstraint[RoleNames.Length];
        for (int i = 0; i < RoleNames.Length; i++)
        {
            reachIKs[i] = PickHandIK(sources[i], RaiseRightHand[i]);
            if (reachIKs[i] == null)
            {
                Debug.LogError($"[엔딩 셋업] {RoleNames[i]}에서 {(RaiseRightHand[i] ? "오른" : "왼")}손 IK(TwoBoneIKConstraint)를 찾지 못했습니다.");
                return;
            }

            Debug.Log($"[엔딩 셋업] {RoleNames[i]}가 올릴 손: {reachIKs[i].name} (손 {reachIKs[i].data.tip.name})");

            if (sources[i].GetComponentInChildren<RigBuilder>(true) == null)
                Debug.LogWarning($"[엔딩 셋업] {RoleNames[i]}에 RigBuilder가 없습니다. 손 IK가 움직이지 않습니다.");
        }

        // 손바닥이 만나는 지점: 올리는 두 어깨의 가운데, 어깨보다 조금 위
        Vector3 shoulderA = reachIKs[0].data.root.position;
        Vector3 shoulderB = reachIKs[1].data.root.position;
        Vector3 meet = (shoulderA + shoulderB) * 0.5f + Vector3.up * MeetAboveShoulder;

        float halfGap = (Flat(shoulderB - shoulderA).magnitude - CloseGap) * 0.5f;
        for (int i = 0; i < RoleNames.Length; i++)
        {
            float reach = ArmLength(reachIKs[i]);
            float need = new Vector2(halfGap, MeetAboveShoulder).magnitude;
            Debug.Log($"[엔딩 셋업] {RoleNames[i]} 팔 길이 {reach:F2}m, 맞댈 때 뻗는 정도 {need / Mathf.Max(reach, 0.01f) * 100f:F0}%");
            if (need > reach * 0.95f)
                Debug.LogWarning($"[엔딩 셋업] {RoleNames[i]}의 팔이 거의 일자로 펴집니다. 두 캐릭터를 더 가깝게 배치하세요.");
        }

        // 방향: axis = mental → ear, out = 온실 밖(온실 중심에서 멀어지는 쪽)
        Vector3 rootA = sources[0].position;
        Vector3 rootB = sources[1].position;
        Vector3 axis = Flat(rootB - rootA).normalized;
        Vector3 outDir = Vector3.Cross(Vector3.up, axis).normalized;
        Vector3 ground = (rootA + rootB) * 0.5f;
        if (greenhouse != null && Vector3.Dot(outDir, Flat(RendererBounds(greenhouse).center - ground)) > 0f)
            outDir = -outDir;

        // 멈출 자리: 온실 밖으로 20걸음. 그 전에 절벽이 있으면 절벽에서 떨어져 멈춘다.
        float walkDistance = WalkSteps * StepLength;
        float safeDistance = FindWalkDistance(ground, outDir, out bool cliffFound);
        if (cliffFound && safeDistance < walkDistance)
            walkDistance = safeDistance;
        Vector3 stopCenter = ground + outDir * walkDistance;
        stopCenter.y = GroundHeight(stopCenter, ground.y);
        Debug.Log(cliffFound
            ? $"[엔딩 셋업] 절벽은 온실 밖 {safeDistance + StopBeforeCliff:F1}m. {walkDistance:F1}m({walkDistance / StepLength:F0}걸음) 걸어가 멈춥니다."
            : $"[엔딩 셋업] 절벽을 찾지 못했습니다. {walkDistance:F1}m({WalkSteps}걸음) 걸어가 멈춥니다.");

        // 바라볼 곳: 수변 식생(식생 매니저)과 Building. 없으면 자라는 그룹.
        Transform building = FindInScene(scene, BuildingName, existingRoot);
        bool hasView = false;
        Bounds viewBounds = default;
        if (building != null)
        {
            viewBounds = RendererBounds(building);
            hasView = true;
        }
        if (vegetation != null)
        {
            foreach (Transform plant in vegetation.vegetationList)
            {
                if (plant == null) continue;
                if (!hasView) viewBounds = new Bounds(plant.position, Vector3.zero);
                viewBounds.Encapsulate(plant.position);
                hasView = true;
            }
        }
        if (!hasView)
            viewBounds = growGroup != null ? RendererBounds(growGroup) : new Bounds(stopCenter + outDir * 15f, Vector3.one * 10f);

        Debug.Log($"[엔딩 셋업] 바라볼 곳(수변+Building): {(building != null ? Path(building) : "Building 없음")}, 중심 {viewBounds.center}");

        Vector3 viewDir = Flat(viewBounds.center - stopCenter);
        viewDir = viewDir.sqrMagnitude > 0.01f ? viewDir.normalized : outDir;
        Vector3 viewSide = Vector3.Cross(Vector3.up, viewDir).normalized;

        // 두 사람이 멈춰 선 순서를 걸어 나오기 전 좌우 순서와 맞춘다(서로 엇갈려 걷지 않도록)
        float sideOfA = Vector3.Dot(Flat(rootA - ground), viewSide) >= 0f ? 1f : -1f;
        var walkPositions = new[]
        {
            stopCenter + viewSide * (StandApart * 0.5f * sideOfA),
            stopCenter - viewSide * (StandApart * 0.5f * sideOfA),
        };
        Quaternion viewRotation = Quaternion.LookRotation(viewDir, Vector3.up);

        walkPositions[0].y = GroundHeight(walkPositions[0], stopCenter.y);
        walkPositions[1].y = GroundHeight(walkPositions[1], stopCenter.y);

        float walkDuration = Mathf.Clamp(walkDistance / WalkSpeed + 1.6f, 4.5f, 30f);
        float eyeHeight = (shoulderA.y + shoulderB.y) * 0.5f - ground.y + 0.25f;

        Debug.Log($"[엔딩 셋업] 손바닥이 만나는 지점: {meet}, 멈출 자리: {stopCenter} (걷기 {walkDuration:F1}초)");

        Stage3EndingCutscene existingCutscene = existingRoot != null ? existingRoot.GetComponent<Stage3EndingCutscene>() : null;
        int sceneLayout = existingCutscene != null ? new SerializedObject(existingCutscene).FindProperty("setupLayoutVersion").intValue : 0;
        bool relayout = existingRoot != null && sceneLayout != LayoutVersion;

        if (!apply)
        {
            Debug.Log(existingRoot == null
                ? $"[엔딩 셋업] {RootName}을 새로 만듭니다."
                : relayout
                    ? $"[엔딩 셋업] 엔딩 구성이 바뀌어(v{sceneLayout} → v{LayoutVersion}) 카메라·타임라인·MeetPose·WalkTarget을 다시 만듭니다. 캐릭터·HandGlow는 그대로 둡니다."
                    : $"[엔딩 셋업] 기존 {RootName}을 보완합니다(이미 있는 위치는 그대로).");
            Debug.Log("===== Dry Run 끝. 변경 없음 =====");
            return;
        }

        // ── 2. 계층 ────────────────────────────────────────────────────
        Transform root = existingRoot != null ? existingRoot : CreateChild(null, RootName);
        Stage3EndingCutscene cutscene = GetOrAdd<Stage3EndingCutscene>(root.gameObject);
        PlayableDirector director = GetOrAdd<PlayableDirector>(root.gameObject);

        if (relayout)
        {
            Debug.Log($"[엔딩 셋업] 엔딩 구성 v{sceneLayout} → v{LayoutVersion}: 카메라·타임라인·마커를 다시 만듭니다.");
            DestroyChild(root, CamerasName);
            DestroyChild(root, $"{ActorsName}/{WalkCenterName}");
            DestroyChild(root, $"{ActorsName}/{WaveCameraName}");
            foreach (string role in RoleNames)
            {
                DestroyChild(root, $"{ActorsName}/MeetPose_{role}");
                DestroyChild(root, $"{ActorsName}/WalkTarget_{role}");
            }
            if (AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath) != null)
                AssetDatabase.DeleteAsset(TimelinePath);
        }

        Transform rig = GetOrCreateChild(root, CameraRigName, out _);
        CinemachineBrain brain = GetOrAdd<CinemachineBrain>(rig.gameObject);
        brain.ChannelMask = EndingChannel;
        brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
        brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
        brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2f);

        // ── 3. 카메라 ──────────────────────────────────────────────────
        Transform cameras = GetOrCreateChild(root, CamerasName, out _);
        Transform actorsRoot = GetOrCreateChild(root, ActorsName, out _);

        // 공명부터 걷기까지: 두 사람 가운데(WalkCenter)를 뒤에서 본다. 손이 만나는 가운데가 잘 보이고,
        // 걸어 나가면 같은 시점 그대로 따라간다.
        Transform walkCenter = GetOrCreateChild(actorsRoot, WalkCenterName, out bool centerCreated);
        if (centerCreated || relayout)
            walkCenter.position = ground + Vector3.up * 1.3f;

        Vector3 followOffset = -outDir * 3.5f + Vector3.up * 0.9f;
        CinemachineCamera camFollow = GetOrCreateCamera(cameras, "Cam_01_Follow",
            walkCenter.position + followOffset, walkCenter.position);
        ConfigureFollow(camFollow, walkCenter, followOffset);

        // 공명: 따라가는 카메라와 같은 쪽(뒤)에서 맞대는 두 손 가까이로 다가간다
        CinemachineCamera camHands = GetOrCreateCamera(cameras, "Cam_00_HandsClose",
            meet - outDir * 1.4f + Vector3.up * 0.2f, meet);

        // 크레딧 마지막 손 흔들기 컷: 온실 밖에서 문 앞의 두 사람을 정면으로 본다(온실이 뒤로 보인다)
        Transform waveCamera = GetOrCreateChild(actorsRoot, WaveCameraName, out bool waveCreated);
        if (waveCreated)
        {
            Vector3 wavePos = ground + outDir * 4.2f + Vector3.up * 1.5f;
            Vector3 waveLook = ground + Vector3.up * 1.1f;
            waveCamera.SetPositionAndRotation(wavePos, Quaternion.LookRotation(waveLook - wavePos, Vector3.up));
        }

        // 멈춘 뒤: 두 사람 뒤에서 5m 올라가며 왼쪽을 향하고, 식생이 자라는 동안
        // 같은 자리에서 오른쪽까지 일정한 속도로 파노라마처럼 둘러본다. 시선은 살짝 내려다본다.
        Vector3 riseEye = stopCenter - viewDir * 2.2f + Vector3.up * (eyeHeight + RiseHeight);
        Vector3 toView = Flat(viewBounds.center - riseEye);
        CinemachineCamera camLeft = GetOrCreateCamera(cameras, "Cam_02_PanoramaLeft",
            riseEye, riseEye + LookDirection(toView, -LookAroundYaw, LookDownPitch));
        CinemachineCamera camRight = GetOrCreateCamera(cameras, "Cam_03_PanoramaRight",
            riseEye, riseEye + LookDirection(toView, LookAroundYaw, LookDownPitch));
        cameras.gameObject.SetActive(false);

        // ── 4. 캐릭터와 마커 ───────────────────────────────────────────
        var actors = new Stage3EndingCutscene.EndingActor[RoleNames.Length];
        for (int i = 0; i < RoleNames.Length; i++)
        {
            string role = RoleNames[i];
            GameObject actor = GetOrCloneActor(actorsRoot, "Ending_" + role, sources[i].gameObject);
            TwoBoneIKConstraint ik = PickHandIK(actor.transform, RaiseRightHand[i]);

            Vector3 towardPartner = Flat(sources[1 - i].position - sources[i].position).normalized;
            Transform meetMarker = GetOrCreateChild(actorsRoot, "MeetPose_" + role, out bool meetCreated);
            if (meetCreated && ik != null && ik.data.tip != null)
                meetMarker.SetPositionAndRotation(
                    meet - towardPartner * PalmGap,
                    PalmFacing(ik.data.tip, RaiseRightHand[i], towardPartner));

            Transform walkMarker = GetOrCreateChild(actorsRoot, "WalkTarget_" + role, out bool walkCreated);
            if (walkCreated)
                walkMarker.SetPositionAndRotation(walkPositions[i], viewRotation);

            actors[i] = new Stage3EndingCutscene.EndingActor
            {
                root = actor, reachIK = ik, meetPose = meetMarker, walkTarget = walkMarker,
            };
        }

        // ── 5. 손의 빛 ─────────────────────────────────────────────────
        Transform glow = GetOrCreateChild(root, GlowName, out bool glowCreated);
        if (glowCreated || relayout)
            glow.position = meet;

        Light light = GetOrAdd<Light>(glow.gameObject);
        if (glowCreated)
        {
            light.type = LightType.Point;
            light.color = new Color(1f, 0.95f, 0.85f);
            light.range = 6f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
        }

        ParticleSystem particles = GetOrAdd<ParticleSystem>(glow.gameObject);
        if (glowCreated)
            ConfigureGlowParticles(particles);

        // ── 5-1. 엔딩 크레딧 ───────────────────────────────────────────
        EndingCredits credits = SetupCredits(root, relayout, cutscene);

        // ── 6. 효과음 ──────────────────────────────────────────────────
        // 다른 컷씬(Stage1 클리어, Stage2 정화)이 세상이 되살아나는 순간에 쓰는 소리. VR이라 2D로 듣는다.
        AudioSource sfx = GetOrAdd<AudioSource>(root.gameObject);
        sfx.playOnAwake = false;
        sfx.spatialBlend = 0f;
        AudioClip restoreClip = AssetDatabase.LoadAssetAtPath<AudioClip>(RestoreSoundPath);
        if (restoreClip == null)
            Debug.LogWarning($"[엔딩 셋업] 효과음 {RestoreSoundPath}를 찾지 못해 소리 없이 만듭니다.");

        // ── 7. 타임라인 ────────────────────────────────────────────────
        TimelineAsset timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePath);
        bool timelineCreated = timeline == null;
        if (timelineCreated)
            timeline = CreateTimeline(director, walkDuration, camHands, camFollow, camLeft, camRight, restoreClip);

        director.playableAsset = timeline;
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.Hold;
        BindTracks(director, timeline, brain, cutscene, cameras, sfx);

        // ── 7. 컨트롤러 연결 ───────────────────────────────────────────
        var so = new SerializedObject(cutscene);
        so.FindProperty("director").objectReferenceValue = director;
        so.FindProperty("cutsceneBrain").objectReferenceValue = brain;
        so.FindProperty("cameraRoot").objectReferenceValue = cameras.gameObject;
        so.FindProperty("vegetation").objectReferenceValue = vegetation;
        so.FindProperty("growGroup").objectReferenceValue = growGroup != null ? growGroup.gameObject : null;
        so.FindProperty("walkCenter").objectReferenceValue = walkCenter;
        so.FindProperty("waveCameraPose").objectReferenceValue = waveCamera;

        // 걷는 동안 발이 땅에 닿을 때마다 이 긴 발소리 클립에서 잘라 낸 걸음 하나씩을 튼다
        AudioClip footstep = AssetDatabase.LoadAssetAtPath<AudioClip>(FootstepSoundPath);
        so.FindProperty("footstepClip").objectReferenceValue = footstep;
        if (footstep == null)
            Debug.LogWarning($"[엔딩 셋업] 발소리 {FootstepSoundPath}를 찾지 못했습니다.");

        // 손 흔들기: 팔꿈치를 축으로 아래팔 전체가 좌우로 흔들린다. 구성이 바뀔 때만 넣는다.
        if (relayout || existingRoot == null)
        {
            so.FindProperty("waveSpeed").floatValue = 1.4f;
            so.FindProperty("waveAngle").floatValue = 25f;
        }
        so.FindProperty("credits").objectReferenceValue = credits;
        so.FindProperty("glowLight").objectReferenceValue = light;
        so.FindProperty("glowParticles").objectReferenceValue = particles;
        so.FindProperty("closeGap").floatValue = CloseGap;
        so.FindProperty("handRotationWeight").floatValue = HandRotationWeight;
        so.FindProperty("setupLayoutVersion").intValue = LayoutVersion;

        SerializedProperty actorsProp = so.FindProperty("actors");
        actorsProp.arraySize = actors.Length;
        for (int i = 0; i < actors.Length; i++)
        {
            SerializedProperty element = actorsProp.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("root").objectReferenceValue = actors[i].root;
            element.FindPropertyRelative("reachIK").objectReferenceValue = actors[i].reachIK;
            element.FindPropertyRelative("meetPose").objectReferenceValue = actors[i].meetPose;
            element.FindPropertyRelative("walkTarget").objectReferenceValue = actors[i].walkTarget;
        }
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root.gameObject;

        Debug.Log($"[엔딩 셋업] 완료. 타임라인 {(timelineCreated ? "생성" : "기존 것 재사용")}: {TimelinePath}");
        Debug.Log("[엔딩 셋업] 씬을 저장하세요. MeetPose_* 로 손바닥, WalkTarget_* 로 멈출 자리, Cam_* 로 구도를 맞출 수 있습니다.");
    }

    // ── 엔딩 크레딧 ───────────────────────────────────────────────────────

    // 크레딧 오브젝트를 만들고 폰트·노래·장면 이미지를 넣는다. 크레딧 문구는 처음 만들 때만 넣는다
    // (Inspector에서 고친 이름을 덮어쓰지 않도록). 장면 영상·이미지는 Apply할 때마다 다시 넣어
    // 새로 녹화·캡처한 것이 바로 반영된다.
    private static EndingCredits SetupCredits(Transform root, bool relayout, Stage3EndingCutscene cutscene)
    {
        Transform creditsObject = GetOrCreateChild(root, CreditsName, out bool created);
        EndingCredits credits = GetOrAdd<EndingCredits>(creditsObject.gameObject);
        var so = new SerializedObject(credits);

        // 마지막 장면: 온실 앞에서 손 흔드는 두 사람(엔딩이 실시간으로 그린다)
        so.FindProperty("liveShotSource").objectReferenceValue = cutscene;

        // 마지막 "The End": 배경이 투명한 이미지. 가장자리가 지저분하지 않게 투명도 처리·밉맵 없음으로 맞춘다.
        if (AssetImporter.GetAtPath(TheEndImagePath) is TextureImporter endImporter
            && (!endImporter.alphaIsTransparency || endImporter.mipmapEnabled))
        {
            endImporter.alphaIsTransparency = true;
            endImporter.mipmapEnabled = false;
            endImporter.SaveAndReimport();
        }
        Texture2D endImage = AssetDatabase.LoadAssetAtPath<Texture2D>(TheEndImagePath);
        so.FindProperty("endImage").objectReferenceValue = endImage;
        if (endImage == null)
            Debug.LogWarning($"[엔딩 셋업] The End 이미지 {TheEndImagePath}를 찾지 못해 글씨로 보여 줍니다.");

        so.FindProperty("font").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(CreditsFontPath);
        AudioClip music = AssetDatabase.LoadAssetAtPath<AudioClip>(CreditsMusicPath);
        so.FindProperty("music").objectReferenceValue = music;
        if (music == null)
            Debug.LogWarning($"[엔딩 셋업] 크레딧 노래 {CreditsMusicPath}를 찾지 못했습니다.");

        // 크레딧 장면은 사진(캡처)만 쓴다. 영상 칸은 비워 둔다. 칸 순서를 지키도록 빈 칸은 null로 둔다.
        int count = EndingCreditShotCapture.ShotNames.Length;
        so.FindProperty("clips").arraySize = 0;
        SerializedProperty shotsProp = so.FindProperty("shots");
        shotsProp.arraySize = count;

        var images = new List<string>();
        var missing = new List<string>();
        for (int i = 0; i < count; i++)
        {
            string shotName = EndingCreditShotCapture.ShotNames[i];
            var image = AssetDatabase.LoadAssetAtPath<Texture2D>(EndingCreditShotCapture.ShotPath(i));
            shotsProp.GetArrayElementAtIndex(i).objectReferenceValue = image;

            if (image != null) images.Add(shotName);
            else missing.Add(shotName);
        }

        Debug.Log($"[엔딩 셋업] 크레딧 사진 {images.Count}장을 넣었습니다" +
                  (missing.Count == 0
                      ? "."
                      : $". 아직 없는 사진: {string.Join(", ", missing)} — 메뉴 Re_Bloom/Credits/Capture 로 찍은 뒤 Apply를 다시 실행하세요."));

        if (created)
        {
            SerializedProperty linesProp = so.FindProperty("lines");
            linesProp.arraySize = CreditLines.Length;
            for (int i = 0; i < CreditLines.Length; i++)
            {
                SerializedProperty line = linesProp.GetArrayElementAtIndex(i);
                line.FindPropertyRelative("role").stringValue = CreditLines[i].role;
                line.FindPropertyRelative("name").stringValue = CreditLines[i].name;
            }
        }

        // 장면 하나 4초. 크레딧 5줄이 사진 7장(28초) 안에 다 나오도록 글자 타이밍을 맞춘다(마지막 캐릭터 사진은 글자 없이):
        // 1.5 + 5 × (0.8 + 2.4 + 0.8 + 0.5) = 24초. 구성이 바뀔 때만 넣어 Inspector에서 고친 값을 덮어쓰지 않는다.
        // 사진은 확대하지 않고(1), 복원 전 → 후가 1.5초 동안 부드럽게 겹쳐 바뀐다. 글씨 뒤 검은 번짐은 진하게.
        if (created || relayout)
        {
            so.FindProperty("shotDuration").floatValue = 4f;
            so.FindProperty("shotCrossfade").floatValue = 1.5f;
            so.FindProperty("shotZoom").floatValue = 1f;
            so.FindProperty("leftShadeOpacity").floatValue = 1f;     // 왼쪽 끝은 완전한 검정
            so.FindProperty("textShadeOpacity").floatValue = 0.95f;
            so.FindProperty("liveShotDuration").floatValue = 8f;    // 걸어 들어오기 3초 + 돌아서기 + 손 흔들기
            so.FindProperty("endImageWidth").floatValue = 650f;
            so.FindProperty("heightOffset").floatValue = 1.1f;    // 기기에서 맞춘 높이
            so.FindProperty("firstLineDelay").floatValue = 1.5f;
            so.FindProperty("lineFade").floatValue = 0.8f;
            so.FindProperty("lineHold").floatValue = 2.4f;
            so.FindProperty("lineGap").floatValue = 0.5f;
        }

        so.ApplyModifiedProperties();
        return credits;
    }

    // ── 타임라인 ──────────────────────────────────────────────────────────

    private static TimelineAsset CreateTimeline(PlayableDirector director, float walkDuration,
        CinemachineCamera hands, CinemachineCamera follow, CinemachineCamera left, CinemachineCamera right, AudioClip restoreClip)
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(TimelinePath)?.Replace('\\', '/'));

        TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, TimelinePath);

        const double reachStart = 1.5, touch = 6.5;   // 5초에 걸쳐 서서히 올린다
        const double walkStart = touch + 10.0;        // 손을 맞댄 채 공명하는 시간을 넉넉히(약 7.5초)
        double walkEnd = walkStart + walkDuration;
        double growStart = walkEnd + 4.0;             // 카메라가 다 올라간 뒤 자라기 시작
        const double growLength = 10.0;
        const double panLength = growLength + 2.0;    // 왼쪽 → 오른쪽 둘러보기
        const double holdAfterPan = 2.0;              // 다 둘러본 뒤 잠시 머문다
        const double fadeLength = 2.5;                // 서서히 닫는다
        double end = growStart + panLength + holdAfterPan + fadeLength;

        // 카메라: 겹치는 구간이 곧 블렌드다(한 번에 두 클립까지만 겹친다)
        CinemachineTrack cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "Cameras");
        AddShot(cameraTrack, director, follow, 0.0, 4.0);                      // 뒤에서 두 사람을 보다가
        AddShot(cameraTrack, director, hands, 2.0, touch + 6.0);               // 손을 올리는 동안 손 쪽으로 다가가 맞댄 손에 머문다
        AddShot(cameraTrack, director, follow, touch + 3.5, walkEnd + 4.0);    // 다시 뒤로 물러났다가, 걸어 나가면 따라간다
        AddShot(cameraTrack, director, left, walkEnd + 0.5, growStart + panLength); // 멈추면 5m 올라가며 왼쪽을 본다 (3.5초)
        AddShot(cameraTrack, director, right, growStart, end);                 // 자라는 동안 왼쪽 → 오른쪽으로 한 번에 둘러보고, 머물다 페이드

        AddAction(timeline, "Hands",
            (EndingAction.HandReach, reachStart, touch),
            (EndingAction.HandRelease, touch + 7.5, touch + 9.5));
        AddAction(timeline, "Glow", (EndingAction.HandGlow, touch, touch + 8.0));
        AddAction(timeline, "Fog", (EndingAction.FogClear, touch + 0.3, touch + 5.3));
        AddAction(timeline, "Walk", (EndingAction.Walk, walkStart, walkEnd));
        AddAction(timeline, "Grow", (EndingAction.GroupGrow, growStart, growStart + growLength));
        AddAction(timeline, "Vegetation", (EndingAction.VegetationGrow, growStart, growStart + growLength));
        AddAction(timeline, "Fade", (EndingAction.FadeOut, end - fadeLength, end));

        // 효과음: 엔딩이 시작하자마자 한 번
        if (restoreClip != null)
        {
            AudioTrack soundTrack = timeline.CreateTrack<AudioTrack>(null, "Sound");
            TimelineClip clip = soundTrack.CreateClip(restoreClip);
            clip.start = 0.0;
            clip.displayName = restoreClip.name;
        }

        EditorUtility.SetDirty(timeline);
        return timeline;
    }

    private static void AddShot(CinemachineTrack track, PlayableDirector director,
        CinemachineCamera cam, double start, double end)
    {
        TimelineClip clip = track.CreateClip<CinemachineShot>();
        clip.start = start;
        clip.duration = end - start;
        clip.displayName = cam.name;

        // 앞 클립과 겹치는 만큼 블렌드
        TimelineClip previous = track.GetClips().Where(c => c != clip && c.start < start).OrderBy(c => c.start).LastOrDefault();
        if (previous != null && previous.end > start)
        {
            clip.blendInDuration = previous.end - start;
            previous.blendOutDuration = previous.end - start;
        }

        var shot = (CinemachineShot)clip.asset;
        string id = GUID.Generate().ToString();
        shot.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = id };
        director.SetReferenceValue(id, cam);
    }

    private static void AddAction(TimelineAsset timeline, string trackName,
        params (EndingAction action, double start, double end)[] clips)
    {
        EndingActionTrack track = timeline.CreateTrack<EndingActionTrack>(null, trackName);
        foreach ((EndingAction action, double start, double end) in clips)
        {
            TimelineClip clip = track.CreateClip<EndingActionClip>();
            clip.start = start;
            clip.duration = end - start;
            clip.displayName = action.ToString();
            ((EndingActionClip)clip.asset).action = action;
        }
    }

    private static void BindTracks(PlayableDirector director, TimelineAsset timeline,
        CinemachineBrain brain, Stage3EndingCutscene cutscene, Transform cameras, AudioSource sfx)
    {
        foreach (TrackAsset track in timeline.GetOutputTracks())
        {
            if (track is CinemachineTrack)
            {
                director.SetGenericBinding(track, brain);
                RebindShots(director, track, cameras);
            }
            else if (track is EndingActionTrack)
            {
                director.SetGenericBinding(track, cutscene);
            }
            else if (track is AudioTrack)
            {
                director.SetGenericBinding(track, sfx);
            }
        }
    }

    // 샷의 카메라 참조는 Director에 저장된다. Stage3Ending을 지우고 다시 만들면 끊기므로
    // 클립 이름과 같은 이름의 카메라에 다시 잇는다.
    private static void RebindShots(PlayableDirector director, TrackAsset track, Transform cameras)
    {
        foreach (TimelineClip clip in track.GetClips())
        {
            if (!(clip.asset is CinemachineShot shot)) continue;

            Transform camTransform = cameras.Find(clip.displayName);
            CinemachineCamera cam = camTransform != null ? camTransform.GetComponent<CinemachineCamera>() : null;
            if (cam == null)
            {
                Debug.LogWarning($"[엔딩 셋업] 샷 '{clip.displayName}'에 맞는 카메라가 {CamerasName} 아래에 없습니다.");
                continue;
            }

            PropertyName id = shot.VirtualCamera.exposedName;
            if (PropertyName.IsNullOrEmpty(id))
            {
                id = GUID.Generate().ToString();
                shot.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = id };
                EditorUtility.SetDirty(shot);
            }

            director.SetReferenceValue(id, cam);
        }
    }

    // ── 캐릭터 ────────────────────────────────────────────────────────────

    // 배치·포즈는 그대로 복제하고, 인형에 필요 없는 컴포넌트(네트워크·XR 입력·게임 로직)는 뗀다.
    private static GameObject GetOrCloneActor(Transform parent, string name, GameObject source)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing.gameObject;

        GameObject clone = Object.Instantiate(source, parent);
        clone.name = name;
        clone.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        clone.transform.localScale = source.transform.lossyScale;
        Undo.RegisterCreatedObjectUndo(clone, "Create ending actor");

        int removed = StripComponents(clone);
        clone.SetActive(false);

        Debug.Log($"[엔딩 셋업] {source.name} 복제 → {name} (컴포넌트 {removed}개 제거)");
        return clone;
    }

    private static int StripComponents(GameObject root)
    {
        var pending = root.GetComponentsInChildren<Component>(true).Where(c => c != null && !KeepOnActor(c)).ToList();
        int removed = 0;

        // RequireComponent로 묶인 것은 의존하는 쪽을 먼저 떼야 하므로 여러 번 돈다
        for (int pass = 0; pass < 8 && pending.Count > 0; pass++)
        {
            foreach (Component c in pending.ToList())
            {
                if (IsRequiredByOthers(c, pending)) continue;

                Object.DestroyImmediate(c);
                pending.Remove(c);
                removed++;
            }
        }

        foreach (Component c in pending)
            Debug.LogWarning($"[엔딩 셋업] {c.GetType().Name}({c.name})를 떼지 못했습니다.");

        return removed;
    }

    private static bool KeepOnActor(Component c)
    {
        Type t = c.GetType();
        return c is Transform || c is Animator || c is Renderer || c is MeshFilter || c is Light || c is LODGroup
               || t.Assembly.GetName().Name == "Unity.Animation.Rigging";
    }

    private static bool IsRequiredByOthers(Component target, List<Component> pending)
    {
        foreach (Component other in target.GetComponents<Component>())
        {
            if (other == null || other == target) continue;

            foreach (RequireComponent req in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
            {
                Type[] required = { req.m_Type0, req.m_Type1, req.m_Type2 };
                if (required.Any(r => r != null && r.IsInstanceOfType(target)))
                    return true;
            }
        }

        return false;
    }

    // 왼손 / 오른손 IK. 이름(RightHandIK 등)이나 손 뼈 이름(hand.r 등)으로 가린다.
    private static TwoBoneIKConstraint PickHandIK(Transform actor, bool right)
    {
        return actor.GetComponentsInChildren<TwoBoneIKConstraint>(true)
            .Where(ik => ik.data.tip != null && ik.data.root != null && ik.data.target != null)
            .FirstOrDefault(ik => IsHand(ik) && IsRightSide(ik) == right);
    }

    private static bool IsHand(TwoBoneIKConstraint ik) =>
        ik.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0
        || ik.data.tip.name.IndexOf("hand", StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool IsRightSide(TwoBoneIKConstraint ik)
    {
        if (ik.name.IndexOf("right", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (ik.name.IndexOf("left", StringComparison.OrdinalIgnoreCase) >= 0) return false;

        string tip = ik.data.tip.name.ToLowerInvariant();
        return tip.EndsWith(".r") || tip.EndsWith("_r") || tip.Contains("right");
    }

    private static float ArmLength(TwoBoneIKConstraint ik)
    {
        if (ik.data.mid == null) return 0f;
        return Vector3.Distance(ik.data.root.position, ik.data.mid.position)
               + Vector3.Distance(ik.data.mid.position, ik.data.tip.position);
    }

    // 손가락이 위를 향하고 손바닥이 상대를 향하는 손 회전.
    // 손 뼈의 축 방향은 리그마다 다르므로 손가락 뼈 위치로 지금의 손가락·손바닥 방향을 재고,
    // 그 방향을 원하는 방향으로 돌리는 회전을 지금 회전에 곱한다.
    private static Quaternion PalmFacing(Transform hand, bool right, Vector3 towardPartner)
    {
        Transform middle = FindDescendant(hand, "middle1");
        Transform index = FindDescendant(hand, "index1");
        Transform pinky = FindDescendant(hand, "pinky1");
        if (middle == null || index == null || pinky == null)
        {
            Debug.LogWarning($"[엔딩 셋업] {hand.name}의 손가락 뼈를 찾지 못해 손바닥 방향을 계산하지 못했습니다. MeetPose를 직접 돌려 주세요.");
            return hand.rotation;
        }

        Vector3 fingers = (middle.position - hand.position).normalized;
        Vector3 across = (index.position - pinky.position).normalized;
        Vector3 palm = Vector3.Cross(fingers, across).normalized * (right ? 1f : -1f);

        // 손가락을 상대 쪽으로 기울이면 손끝이 가운데 면을 넘어가 서로 엇갈린다. 똑바로 위로 세운다.
        Vector3 wantFingers = Vector3.up;
        Vector3 wantPalm = Vector3.ProjectOnPlane(towardPartner, wantFingers).normalized;

        Quaternion now = Quaternion.LookRotation(fingers, palm);
        Quaternion want = Quaternion.LookRotation(wantFingers, wantPalm);
        return want * Quaternion.Inverse(now) * hand.rotation;
    }

    private static Transform FindDescendant(Transform root, string contains) =>
        root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t != root && t.name.IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0);

    // ── 빛 파티클 ─────────────────────────────────────────────────────────

    private static void ConfigureGlowParticles(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 3f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white, new Color(1f, 0.9f, 0.7f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 25f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.08f;

        ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
        col.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = fade;

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetOrCreateGlowMaterial();
    }

    // URP 파티클 Unlit, 가산 블렌드
    private static Material GetOrCreateGlowMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
        if (mat != null) return mat;

        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("[엔딩 셋업] URP 파티클 셰이더를 찾지 못했습니다. HandGlow 파티클 머티리얼을 직접 넣어 주세요.");
            return null;
        }

        mat = new Material(shader) { name = "EndingGlowParticle" };
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 2f);     // Additive
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;

        var texture = AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd");
        if (texture != null)
            mat.SetTexture("_BaseMap", texture);

        EnsureFolder(System.IO.Path.GetDirectoryName(GlowMaterialPath)?.Replace('\\', '/'));
        AssetDatabase.CreateAsset(mat, GlowMaterialPath);
        return mat;
    }

    // ── 공용 ──────────────────────────────────────────────────────────────

    private static CinemachineCamera GetOrCreateCamera(Transform parent, string name, Vector3 position, Vector3 lookAt)
    {
        Transform t = GetOrCreateChild(parent, name, out bool created);
        CinemachineCamera cam = GetOrAdd<CinemachineCamera>(t.gameObject);
        cam.OutputChannel = EndingChannel;

        if (created)
        {
            Vector3 forward = lookAt - position;
            t.SetPositionAndRotation(position,
                forward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity);
        }

        return cam;
    }

    private static Transform GetOrCreateChild(Transform parent, string name, out bool created)
    {
        Transform existing = parent != null ? parent.Find(name) : null;
        created = existing == null;
        return existing != null ? existing : CreateChild(parent, name);
    }

    private static Transform CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static void DestroyChild(Transform parent, string path)
    {
        Transform child = parent.Find(path);
        if (child != null)
            Undo.DestroyObjectImmediate(child.gameObject);
    }

    private static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    // 이름으로 찾는다(꺼진 것 포함). exclude 아래(이미 만든 엔딩 오브젝트)는 건너뛴다.
    private static Transform FindInScene(Scene scene, string name, Transform exclude)
    {
        foreach (GameObject go in scene.GetRootGameObjects())
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != name) continue;
            if (exclude != null && t.IsChildOf(exclude)) continue;
            return t;
        }

        return null;
    }

    private static Bounds RendererBounds(Transform root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(root.position, Vector3.one * 16f);

        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers)
            b.Encapsulate(r.bounds);
        return b;
    }

    private static void EnsureFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

        string parent = System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }

    // 온실 밖으로 걸어가며 지면을 0.5m 간격으로 짚어 본다. 지면이 CliffDrop 이상 꺼지거나
    // 아예 없으면 절벽으로 보고, 그보다 StopBeforeCliff 앞을 멈출 거리로 돌려준다.
    // 레이는 지난 지면 1.5m 위에서 쏜다 — 문틀·지붕처럼 머리 위에 있는 것을 지면으로 잡지 않도록.
    private static float FindWalkDistance(Vector3 start, Vector3 dir, out bool cliffFound)
    {
        const float step = 0.5f;
        const float probeHeight = 1.5f;
        float last = start.y;

        for (float d = step; d <= MaxWalkDistance; d += step)
        {
            Vector3 probe = start + dir * d;
            probe.y = last + probeHeight;

            bool hasGround = TryGroundHeight(probe, probeHeight + CliffDrop * 4f, out float y);
            if (!hasGround || y < last - CliffDrop)
            {
                cliffFound = true;
                return Mathf.Max(step, d - StopBeforeCliff);
            }

            // 난간·화분처럼 갑자기 솟은 것은 지면으로 치지 않는다
            if (y <= last + 0.6f)
                last = y;
        }

        cliffFound = false;
        return MaxWalkDistance;
    }

    private static float GroundHeight(Vector3 around, float fallback) =>
        TryGroundHeight(new Vector3(around.x, fallback + 3f, around.z), 8f, out float y) ? y : fallback;

    private static bool TryGroundHeight(Vector3 from, float distance, out float y)
    {
        if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }

        y = 0f;
        return false;
    }

    // 대상을 월드 기준 offset만큼 떨어져 부드럽게 따라가고, 대상을 화면 가운데에 둔다.
    private static void ConfigureFollow(CinemachineCamera cam, Transform target, Vector3 offset)
    {
        cam.Follow = target;
        cam.LookAt = target;

        CinemachineFollow follow = GetOrAdd<CinemachineFollow>(cam.gameObject);
        follow.FollowOffset = offset;
        Unity.Cinemachine.TargetTracking.TrackerSettings tracker = follow.TrackerSettings;
        tracker.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
        tracker.PositionDamping = new Vector3(1.2f, 1.2f, 1.2f);
        follow.TrackerSettings = tracker;

        CinemachineRotationComposer composer = GetOrAdd<CinemachineRotationComposer>(cam.gameObject);
        composer.Damping = new Vector2(1f, 1f);
    }

    // 수평 방향 forward를 yaw만큼 돌리고 pitch만큼 내려다보는 방향(길이 20m). pitch는 아래가 +.
    private static Vector3 LookDirection(Vector3 forward, float yaw, float pitch) =>
        Quaternion.LookRotation(Flat(forward).normalized) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * 20f;

    private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private static string Path(Transform t) => t.parent == null ? t.name : Path(t.parent) + "/" + t.name;
}
