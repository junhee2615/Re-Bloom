using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// StartScene 타이틀 화면의 3D 배경과 시점을 담당한다.
///
/// 하는 일은 넷뿐이다.
///   ① 영속 XR Rig의 Locomotion을 꺼서 낙하·이동을 막는다
///   ② 리그를 촬영 시점으로 옮긴다 (수평 Head Offset 보정)
///   ③ TitleCanvas / MenuCanvas를 같은 양만큼 함께 옮겨 구도를 보존한다
///   ④ Stage1_TitleBackground를 Additive로 올린다
///
/// 그리고 StartScene을 떠날 때 ①②를 **원래대로 되돌린다.**
///
/// 왜 되돌려야 하는가:
/// XR Rig은 DontDestroyOnLoad라 Opening까지 살아서 넘어간다.
/// OpeningEnvironmentController.Awake()가 그 시점의 리그 포즈를
/// "연출 전 원래 포즈"로 저장했다가 타이틀이 끝나면 그 자리로 되돌린 뒤
/// EnterSession을 호출한다. 여기서 복구하지 않으면 Opening이 (1.5, 0, -18)을
/// 원래 포즈로 기억해 플레이어가 Lobby에 그 좌표로 도착한다.
///
/// 순서는 Unity가 보장한다 —
/// LoadScene(Single)은 구 씬 오브젝트 파괴(OnDestroy)를 끝낸 뒤
/// 새 씬 오브젝트를 생성(Awake)한다. 즉
///   StartScene.OnDestroy(복구)  →  OpeningEnvironmentController.Awake(저장)
/// 가 보장되므로 Opening은 올바른 원래 포즈를 집는다.
///
/// 배경 씬 언로드를 직접 하지 않는 이유:
/// LoadScene(Single)이 DontDestroyOnLoad를 뺀 모든 씬을 언로드하므로
/// 배경 씬도 자동으로 사라진다. 여기서 UnloadSceneAsync를 또 부르면
/// Unity가 Single 로드를 처리하는 중간에 경합이 생길 수 있다.
/// </summary>
public class TitleBackgroundController : MonoBehaviour
{
    private const string LocomotionRootName = "Locomotion";

    [Header("배경 씬")]
    [SerializeField, Tooltip("Additive로 올릴 배경 씬 이름. File > Build Profiles > Scene List에 등록되어 있어야 한다.")]
    private string backgroundSceneName = "Stage1_TitleBackground";

    [Header("시점")]
    [Tooltip("플레이어의 머리가 수평으로 놓일 월드 좌표. y는 바닥 높이를 쓰고 실제 눈높이는 HMD 트래킹에 맡긴다.")]
    [SerializeField] private Vector3 shotPosition = new Vector3(1.5f, 0f, -18f);

    [Tooltip("리그의 Yaw. VR이므로 Pitch/Roll은 강제하지 않는다.")]
    [SerializeField] private float shotYaw;

    [Header("함께 옮길 UI (비워 두면 이름으로 찾는다)")]
    [SerializeField] private Transform titleCanvas;
    [SerializeField] private Transform menuCanvas;

    [Header("Debug")]
    [Tooltip("HMD 트래킹이 유효해질 때까지 기다릴 최대 프레임. 헤드셋 없는 에디터에서 무한 대기를 막는 상한이다.")]
    [SerializeField, Min(1)] private int trackingWaitTimeoutFrames = 180;

    // ── 복구용 저장값 ────────────────────────────────────────────
    private Transform rigRoot;
    private Vector3 rigOriginalPosition;
    private Quaternion rigOriginalRotation;
    private bool rigPoseCaptured;

    private GameObject locomotionRoot;
    private bool locomotionWasActive;
    private bool locomotionStateCaptured;

    // 씬에 authoring된 "리그 원점 기준 UI 배치". 리그를 옮겨도 이 관계를 그대로 재현한다.
    private Vector3 titleOffsetFromRig;
    private Vector3 menuOffsetFromRig;
    private bool uiOffsetsCaptured;

    /// <summary>배경 씬의 XR 상호작용을 이미 차단했는지. 중복 실행을 막는다.</summary>
    private bool interactionDisabled;

    private bool destroyed;

    /// <summary>
    /// 첫 프레임이 그려지기 전에 Locomotion부터 끈다.
    ///
    /// Awake에서 끄는 이유는 Opening과 같다 — GravityProvider는 접지하지 못하면
    /// 종단속도까지 가속하고, 쌓인 낙하 속도는 Transform을 직접 옮겨도 초기화되지 않는다.
    /// 리그 이동보다 먼저 꺼 두어야 한 프레임도 떨어지지 않는다.
    /// </summary>
    private void Awake()
    {
        ResolveRig();
        CaptureUiOffsets();
        LockLocomotion();
    }

    private IEnumerator Start()
    {
        // HMD 트래킹이 유효해질 때까지 기다린다.
        // Awake~첫 프레임의 Main Camera는 아직 프리팹에 적힌 위치라
        // Yaw도 Head Offset도 의미가 없다. Quest에서는 첫 유효 포즈가
        // 한 프레임 안에 들어오지 않는 경우가 있어 명시적으로 기다린다.
        yield return WaitForTracking();

        if (destroyed)
            yield break;

        MoveRigAndUi();

        yield return LoadBackgroundScene();
    }

    /// <summary>
    /// 씬에 배치된 그대로의 "리그 원점 → 각 캔버스" 벡터를 기억한다.
    /// 거리·높이·두 캔버스의 상대 간격이 전부 이 벡터에 담기므로,
    /// 나중에 이 벡터를 그대로 재사용하면 구도가 바뀌지 않는다.
    /// </summary>
    private void CaptureUiOffsets()
    {
        if (!rigPoseCaptured)
            return;

        Transform title = ResolveCanvas(ref titleCanvas, "TitleCanvas");
        Transform menu = ResolveCanvas(ref menuCanvas, "MenuCanvas");

        if (title != null)
            titleOffsetFromRig = title.position - rigOriginalPosition;

        if (menu != null)
            menuOffsetFromRig = menu.position - rigOriginalPosition;

        uiOffsetsCaptured = true;
    }

    /// <summary>
    /// HMD가 실제 포즈를 보고하기 시작할 때까지 기다린다.
    /// 헤드셋이 없는 에디터에서는 상한 프레임까지만 기다리고 그대로 진행한다.
    /// </summary>
    private IEnumerator WaitForTracking()
    {
        for (int frame = 0; frame < trackingWaitTimeoutFrames; frame++)
        {
            InputDevice hmd = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);

            if (hmd.isValid &&
                hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 eye) &&
                eye != Vector3.zero)
            {
                // TrackedPoseDriver가 이 포즈를 Transform에 반영할 한 프레임을 더 준다.
                yield return null;
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning(
            "[TitleBackground] HMD 트래킹을 확인하지 못해 현재 카메라 포즈로 진행합니다. " +
            "에디터에서 헤드셋 없이 실행한 경우라면 정상입니다.", this);
    }

    /// <summary>
    /// 리그를 촬영 시점으로 옮기고, UI를 같은 양만큼 평행이동한다.
    ///
    /// 리그 이동은 HardwareRig.TeleportTo 와 같은 규약을 쓴다 —
    /// 플레이 공간 안에서 플레이어가 어디에 서 있든 **머리의 수평 위치**가
    /// 촬영 기준점에 오도록 리그를 역보정하고, y는 기준 y를 그대로 쓴다.
    /// headOffset.y를 0으로 두므로 눈높이는 HMD 트래킹 값이 그대로 유지된다.
    ///
    /// UI는 보정 전 순수 이동량(Δ = shotPosition − 리그 원래 위치)으로 옮긴다.
    /// 그래야 "리그 원점 기준으로 UI가 어디에 있었는가"가 그대로 보존되어
    /// 로고·버튼의 크기, 거리, 상대 간격이 전혀 바뀌지 않는다.
    /// </summary>
    private void MoveRigAndUi()
    {
        if (!rigPoseCaptured || rigRoot == null)
        {
            Debug.LogWarning("[TitleBackground] XR Origin을 찾지 못해 시점을 옮기지 못했습니다.", this);
            return;
        }

        Camera head = Camera.main;

        if (head == null)
        {
            Debug.LogWarning("[TitleBackground] Camera.main을 찾지 못해 시점을 옮기지 못했습니다.", this);
            return;
        }

        // ── ① Yaw 리센터 ────────────────────────────────────────
        // 머리의 월드 yaw = 리그 yaw + HMD 자체 yaw 이다.
        // HMD 자체 yaw는 플레이어가 물리적으로 바라보는 방향이라 앱이 통제할 수 없다.
        // 그래서 리그 yaw를 (shotYaw − HMD yaw)로 두어 최종 머리 yaw가 shotYaw가 되게 한다.
        // Pitch/Roll은 손대지 않는다 — 리그는 y축으로만 회전한다.
        float hmdYaw = head.transform.eulerAngles.y - rigRoot.eulerAngles.y;
        Quaternion rigRotation = Quaternion.Euler(0f, shotYaw - hmdYaw, 0f);

        rigRoot.rotation = rigRotation;

        // ── ② 회전 뒤에 수평 Head Offset 재계산 ─────────────────
        // 리그를 돌리면 머리도 리그 원점을 중심으로 함께 돌아 위치가 바뀐다.
        // 따라서 Offset은 반드시 회전을 적용한 **뒤에** 다시 재야 한다.
        Vector3 headOffset = head.transform.position - rigRoot.position;
        headOffset.y = 0f;   // 수평 보정만. 눈높이는 HMD 트래킹에 맡긴다.

        rigRoot.position = shotPosition - headOffset;

        // ── ③ UI를 머리 정면에 배치 ─────────────────────────────
        // 이 시점에 머리의 수평 위치는 shotPosition, 월드 yaw는 shotYaw다.
        // authoring된 "리그 원점 → 캔버스" 벡터를 shotYaw로 돌려 그대로 붙이면
        // 거리·높이·두 캔버스의 상대 간격이 전혀 바뀌지 않은 채 정면에 온다.
        Quaternion faceYaw = Quaternion.Euler(0f, shotYaw, 0f);
        Vector3 headGround = new Vector3(shotPosition.x, rigRoot.position.y, shotPosition.z);

        PlaceUi(titleCanvas, headGround, faceYaw, titleOffsetFromRig, "TitleCanvas");
        PlaceUi(menuCanvas, headGround, faceYaw, menuOffsetFromRig, "MenuCanvas");

        Debug.Log(
            $"[TitleBackground] 시점 리센터 - HMD yaw {hmdYaw:F1}° → 리그 yaw {rigRotation.eulerAngles.y:F1}°, " +
            $"리그 {rigOriginalPosition} → {rigRoot.position}, 머리 수평 {shotPosition}", this);
    }

    /// <summary>
    /// 캔버스를 "머리 수평 위치 + (shotYaw로 돌린 authoring 오프셋)"에 놓는다.
    ///
    /// 크기(localScale) / sizeDelta / 자식 배치는 전혀 건드리지 않으므로
    /// 로고·버튼의 크기, 폰트, 두 캔버스의 상대 간격, UI Ray 판정이 그대로 유지된다.
    /// 회전은 shotYaw만 적용한다 — 캔버스가 머리를 정면으로 마주 본다.
    /// </summary>
    private void PlaceUi(Transform canvas, Vector3 headGround, Quaternion faceYaw, Vector3 offsetFromRig, string label)
    {
        if (canvas == null)
        {
            Debug.LogWarning($"[TitleBackground] {label}을(를) 찾지 못해 배치하지 못했습니다.", this);
            return;
        }

        if (!uiOffsetsCaptured)
        {
            Debug.LogWarning($"[TitleBackground] {label}의 원래 배치를 기억하지 못해 건너뜁니다.", this);
            return;
        }

        // 수평 성분만 yaw로 돌리고, 높이는 authoring 값을 그대로 쓴다.
        Vector3 horizontal = new Vector3(offsetFromRig.x, 0f, offsetFromRig.z);

        canvas.SetPositionAndRotation(
            headGround + faceYaw * horizontal + Vector3.up * offsetFromRig.y,
            faceYaw);
    }

    private Transform ResolveCanvas(ref Transform field, string objectName)
    {
        if (field != null)
            return field;

        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
        {
            if (root.name == objectName)
            {
                field = root.transform;
                return field;
            }
        }

        return null;
    }

    private IEnumerator LoadBackgroundScene()
    {
        string sceneName = backgroundSceneName != null ? backgroundSceneName.Trim() : string.Empty;

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[TitleBackground] 배경 씬 이름이 비어 있습니다.", this);
            yield break;
        }

        // ── 중복 로드 방지 ───────────────────────────────────────
        Scene existing = SceneManager.GetSceneByName(sceneName);

        if (existing.IsValid() && existing.isLoaded)
        {
            Debug.Log($"[TitleBackground] '{sceneName}'이(가) 이미 로드되어 있어 건너뜁니다.", this);
            yield break;
        }

        Debug.Log($"[TitleBackground] '{sceneName}' additive load 시작", this);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

        if (load == null)
        {
            Debug.LogError(
                $"[TitleBackground] '{sceneName}'을(를) 로드할 수 없습니다. " +
                "File > Build Profiles > Scene List에 등록되어 있는지 확인하세요.", this);
            yield break;
        }

        yield return load;

        if (destroyed)
            yield break;

        Scene loaded = SceneManager.GetSceneByName(sceneName);

        if (!loaded.IsValid() || !loaded.isLoaded)
        {
            Debug.LogError(
                $"[TitleBackground] '{sceneName}' 로드가 끝났지만 씬이 유효하지 않습니다. " +
                $"IsValid={loaded.IsValid()}, isLoaded={loaded.isLoaded}", this);
            yield break;
        }

        Debug.Log(
            $"[TitleBackground] '{sceneName}' 로드 완료 - " +
            $"루트 오브젝트 {loaded.rootCount}개, buildIndex {loaded.buildIndex}", this);

        // Active Scene은 StartScene 그대로 둔다.
        // 바꾸면 RenderSettings(Skybox / Fog / Ambient)가 배경 씬 것으로 넘어간다.

        DisableSceneInteraction(loaded);
    }

    /// <summary>
    /// 배경 씬 안의 XR 상호작용 컴포넌트만 꺼서, 플레이어가 미션 오브젝트를
    /// 잡거나 레이로 선택하지 못하게 한다.
    ///
    /// 왜 필요한가:
    /// DoorRotate / LeverSwitch / ValveRotate / GeneratorDoorRotate 는 잡히는 순간
    /// Debug.Log 안에서 Runner.LocalPlayer 를 참조한다. StartScene에는 Fusion Runner가
    /// 없으므로 그 순간부터 매 프레임 NullReferenceException이 발생한다.
    ///
    /// 왜 컴포넌트만 끄는가:
    /// Renderer / MeshFilter / Material / Collider / Rigidbody / NetworkObject 는
    /// 전혀 건드리지 않으므로 폐허 도시의 3D 모델은 그대로 보인다.
    ///
    /// 왜 씬 범위로 한정하는가:
    /// scene.GetRootGameObjects() 에서만 내려가므로 StartScene의 XR Origin,
    /// UI Ray(NearFarInteractor), Single/Multi 버튼은 탐색 대상에 들어가지 않는다.
    /// (영속 리그는 DontDestroyOnLoad 씬에 있어 애초에 별도 씬이다.)
    ///
    /// IXRInteractable / IXRInteractor 인터페이스로 찾는 이유:
    /// XRGrabInteractable·XRSocketInteractor·TeleportationArea 는 모두
    /// XRBaseInteractable / XRBaseInteractor 를 거쳐 이 인터페이스를 구현한다.
    /// 베이스 클래스 대신 인터페이스로 찾으면 직접 구현한 커스텀 컴포넌트까지 포함된다.
    /// </summary>
    private void DisableSceneInteraction(Scene scene)
    {
        if (interactionDisabled)
            return;

        if (!scene.IsValid() || !scene.isLoaded)
            return;

        interactionDisabled = true;

        var seen = new HashSet<int>();
        var byType = new Dictionary<string, int>();
        int disabled = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // includeInactive = true — 비활성 GameObject 안의 컴포넌트도 끈다.
            // 나중에 누군가 그 오브젝트를 켜도 상호작용이 되살아나지 않는다.
            disabled += DisableAll(root.GetComponentsInChildren<IXRInteractable>(true), seen, byType);
            disabled += DisableAll(root.GetComponentsInChildren<IXRInteractor>(true), seen, byType);
        }

        if (disabled == 0)
        {
            Debug.Log($"[TitleBackground] '{scene.name}'에 끌 XR 상호작용 컴포넌트가 없습니다.", this);
            return;
        }

        var summary = new System.Text.StringBuilder();

        foreach (KeyValuePair<string, int> pair in byType)
            summary.Append($" {pair.Key}×{pair.Value}");

        Debug.Log(
            $"[TitleBackground] '{scene.name}'의 XR 상호작용 {disabled}개 비활성화 -{summary}", this);
    }

    /// <summary>
    /// 컴포넌트 목록을 꺼서 실제로 끈 개수를 돌려준다.
    /// 한 컴포넌트가 두 인터페이스를 모두 구현할 수 있으므로 InstanceID로 중복을 거른다.
    /// 이미 꺼져 있던 것은 세지 않는다.
    /// </summary>
    private int DisableAll<T>(T[] components, HashSet<int> seen, Dictionary<string, int> byType)
    {
        int count = 0;

        foreach (T component in components)
        {
            if (component is not Behaviour behaviour)
                continue;

            if (!seen.Add(behaviour.GetInstanceID()))
                continue;

            if (!behaviour.enabled)
                continue;

            behaviour.enabled = false;
            count++;

            string typeName = behaviour.GetType().Name;
            byType.TryGetValue(typeName, out int n);
            byType[typeName] = n + 1;
        }

        return count;
    }

    /// <summary>
    /// StartScene이 어떤 경로로 끝나더라도 영속 리그를 원래대로 되돌린다.
    /// 코루틴 끝에서만 복구하면 예외로 코루틴이 조용히 멈출 때 영구히 어긋난 채 남는다.
    /// </summary>
    private void OnDestroy()
    {
        destroyed = true;

        RestoreRigPose();
        RestoreLocomotion();

        // 배경 씬은 의도적으로 직접 언로드하지 않는다. 클래스 주석 참고.
    }

    private void RestoreRigPose()
    {
        if (!rigPoseCaptured || rigRoot == null)
            return;

        rigRoot.SetPositionAndRotation(rigOriginalPosition, rigOriginalRotation);
        rigPoseCaptured = false;

        Debug.Log($"[TitleBackground] 리그 포즈 복구 - {rigOriginalPosition}");
    }

    /// <summary>
    /// Locomotion GameObject 전체를 끈다.
    ///
    /// GravityProvider만 끄지 않고 GameObject를 끄는 이유는 복구할 상태가 bool 하나로
    /// 끝나기 때문이다. InputActionManager는 XR Origin **루트**에 있고 Locomotion의
    /// 자식(Turn/Move/Grab/Teleportation/Climb/Gravity/Jump)에는 없으므로,
    /// 이 호출은 공유 XRI Input Actions를 건드리지 않는다.
    /// Main Camera / Controller / PhysicsHands도 그대로라 UI Ray와 손 추적은 유지된다.
    /// </summary>
    private void LockLocomotion()
    {
        if (locomotionStateCaptured || rigRoot == null)
            return;

        Transform found = null;

        foreach (Transform t in rigRoot.GetComponentsInChildren<Transform>(true))
        {
            if (t != rigRoot && t.name == LocomotionRootName)
            {
                found = t;
                break;
            }
        }

        if (found == null)
        {
            Debug.LogWarning(
                $"[TitleBackground] '{rigRoot.name}' 아래에서 '{LocomotionRootName}'을 찾지 못했습니다.", this);
            return;
        }

        locomotionRoot = found.gameObject;
        locomotionWasActive = locomotionRoot.activeSelf;
        locomotionStateCaptured = true;

        if (!locomotionWasActive)
            return;   // 이미 꺼져 있었다면 건드릴 것도 없다.

        locomotionRoot.SetActive(false);

        Debug.Log("[TitleBackground] Locomotion disabled for title");
    }

    /// <summary>저장했던 원래 activeSelf 값으로 되돌린다. 무조건 true로 켜지 않는다.</summary>
    private void RestoreLocomotion()
    {
        if (!locomotionStateCaptured)
            return;

        bool changed = false;

        if (locomotionRoot != null && locomotionRoot.activeSelf != locomotionWasActive)
        {
            locomotionRoot.SetActive(locomotionWasActive);
            changed = true;
        }

        locomotionRoot = null;
        locomotionWasActive = false;
        locomotionStateCaptured = false;

        if (changed)
            Debug.Log("[TitleBackground] Locomotion restored");
    }

    /// <summary>
    /// 영속 XR Origin 루트를 찾는다.
    /// HardwareRig가 루트에 붙어 있어 가장 확정적이고, 없으면 Camera.main에서 거슬러 올라간다.
    /// (위치 파악에만 쓰고 HardwareRig의 메서드는 호출하지 않는다.)
    /// </summary>
    private void ResolveRig()
    {
        HardwareRig rig = FindAnyObjectByType<HardwareRig>(FindObjectsInactive.Include);

        if (rig != null)
        {
            rigRoot = rig.transform;
        }
        else
        {
            Camera cam = Camera.main;

            for (Transform t = cam != null ? cam.transform : null; t != null; t = t.parent)
            {
                if (t.name.Contains("XR Origin"))
                {
                    rigRoot = t;
                    break;
                }
            }
        }

        if (rigRoot == null)
        {
            Debug.LogWarning(
                "[TitleBackground] XR Origin을 찾지 못했습니다. StartScene의 VRSystem을 확인하세요.", this);
            return;
        }

        rigOriginalPosition = rigRoot.position;
        rigOriginalRotation = rigRoot.rotation;
        rigPoseCaptured = true;
    }
}
