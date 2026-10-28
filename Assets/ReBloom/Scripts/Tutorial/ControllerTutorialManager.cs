using System.Collections;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Jump;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using InputDevice = UnityEngine.XR.InputDevice;

/// <summary>
/// VR이 처음인 플레이어를 위한 컨트롤러 조작 튜토리얼 코스.
/// 단계: 그랩 → 왼쪽 스틱 이동 → 오른쪽 스틱 텔레포트 → A 점프 → B Ray + UI 선택 → X 버튼 안내.
/// 각 단계는 실제로 해봐야 완료되며, 완료하면 효과음/진동 후 자동으로 다음 단계로 넘어간다.
/// 네트워크 구조와 무관하게 로컬 XR Rig만 사용한다.
/// </summary>
public class ControllerTutorialManager : MonoBehaviour
{
    public enum Step { Grab = 0, Walk = 1, Teleport = 2, Jump = 3, RayUI = 4, XButton = 5, Done = 6 }

    [System.Serializable]
    public class Station
    {
        public string title;
        [TextArea(2, 5)] public string body;
        [Tooltip("이 단계에서만 켜지는 오브젝트(안내판, 바닥 표시 등)")]
        public GameObject root;
        public TMP_Text progressText;
        public TMP_Text titleText;
        public TMP_Text bodyText;
        public TMP_Text statusText;

        [Tooltip("보드 오른쪽 하단에 표시할 이미지. 비워두면 플레이 중에는 칸이 숨겨진다.")]
        public Sprite[] images;
        public Image[] imageSlots;
    }

    [Header("Rig (비우면 자동 탐색)")]
    [SerializeField] private XROrigin xrOrigin;
    [Tooltip("'처음부터 다시' 시 플레이어를 옮길 위치. 파란 축(forward)이 바라볼 방향.")]
    [SerializeField] private Transform startPoint;

    [Header("Stations (Grab, Walk, Teleport, Jump, RayUI, XButton 순서)")]
    [SerializeField] private Station[] stations = new Station[6];
    [SerializeField] private GameObject finishedRoot;

    [Header("1. Grab")]
    [SerializeField] private XRGrabInteractable grabObject;
    [SerializeField] private Collider grabTargetZone;

    [Header("2. Walk")]
    [SerializeField] private Transform walkTarget;
    [SerializeField] private float walkRadius = 0.8f;
    [Tooltip("조이스틱으로 이만큼은 이동해야 완료로 인정한다(m).")]
    [SerializeField] private float walkMinStickDistance = 1.5f;

    [Header("3. Teleport")]
    [SerializeField] private Transform teleportTarget;
    [SerializeField] private float teleportRadius = 1.1f;

    [Header("4. Jump")]
    [Tooltip("점프해서 올라갈 물체 윗면 중앙에 둔 빈 오브젝트")]
    [SerializeField] private Transform jumpTargetTop;
    [SerializeField] private float jumpRadius = 0.9f;

    [Header("5. Ray UI")]
    [SerializeField] private Button rayNextButton;

    [Header("6. X Button / Finish")]
    [SerializeField] private Button finishButton;
    [SerializeField] private Button[] restartButtons;

    [Header("Feedback")]
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip stepClearClip;
    [SerializeField] private AudioClip finishClip;
    [SerializeField] private float nextStepDelay = 1.6f;
    [SerializeField] private Color successColor = new Color(0.55f, 1f, 0.55f);
    [SerializeField] private Color infoColor = new Color(1f, 0.9f, 0.55f);

    public Step CurrentStep { get; private set; } = Step.Grab;

    private CharacterController characterController;
    private TeleportationProvider teleportProvider;
    private JumpProvider jumpProvider;
    private RightRayModeToggle rayToggle;

    private bool stepCompleting;

    // 단계별 진행 상태
    private bool grabbedOnce;
    private Vector3 grabSpawnPos;
    private Quaternion grabSpawnRot;
    private Rigidbody grabBody;

    private float stickWalkDistance;
    private Vector3 lastOriginPos;
    private int teleportCount;
    private int lastTeleportCountSeen;

    private bool jumpedThisStep;

    private int raySubStep; // 0: B로 켜기, 1: 버튼 선택, 2: B로 끄기

    private bool prevLeftX;
    private float xFeedbackUntil;

    // ------------------------------------------------------------------

    private void Start()
    {
        ResolveRig();

        if (grabObject != null)
        {
            grabSpawnPos = grabObject.transform.position;
            grabSpawnRot = grabObject.transform.rotation;
            grabBody = grabObject.GetComponent<Rigidbody>();
            grabObject.selectEntered.AddListener(_ => OnGrabbed());
        }

        if (rayNextButton != null) rayNextButton.onClick.AddListener(OnRayNextClicked);
        if (finishButton != null) finishButton.onClick.AddListener(OnFinishClicked);
        if (restartButtons != null)
            foreach (Button b in restartButtons)
                if (b != null) b.onClick.AddListener(Restart);

        for (int i = 0; i < stations.Length; i++)
            ApplyStationTexts(i);

        EnterStep(Step.Grab);
    }

    private void OnDestroy()
    {
        if (teleportProvider != null) teleportProvider.locomotionEnded -= OnTeleportEnded;
        if (jumpProvider != null) jumpProvider.locomotionStarted -= OnJumpStarted;
    }

    private void ResolveRig()
    {
        if (xrOrigin == null)
            xrOrigin = FindFirstObjectByType<XROrigin>();

        if (xrOrigin == null)
        {
            Debug.LogError("[ControllerTutorial] XR Origin을 찾지 못했습니다.", this);
            return;
        }

        characterController = xrOrigin.GetComponent<CharacterController>();
        teleportProvider = xrOrigin.GetComponentInChildren<TeleportationProvider>(true);
        jumpProvider = xrOrigin.GetComponentInChildren<JumpProvider>(true);
        rayToggle = xrOrigin.GetComponentInChildren<RightRayModeToggle>(true);

        if (teleportProvider != null) teleportProvider.locomotionEnded += OnTeleportEnded;
        if (jumpProvider != null) jumpProvider.locomotionStarted += OnJumpStarted;

        lastOriginPos = xrOrigin.transform.position;
    }

    // ------------------------------------------------------------------
    // 단계 전환

    private void EnterStep(Step step)
    {
        CurrentStep = step;
        stepCompleting = false;

        for (int i = 0; i < stations.Length; i++)
        {
            if (stations[i] != null && stations[i].root != null)
                stations[i].root.SetActive(i == (int)step);
            SetStatus(i, "", infoColor);
        }

        if (finishedRoot != null)
            finishedRoot.SetActive(step == Step.Done);

        // 씨앗 아웃라인은 첫 단계(그랩)에서만 보인다. 처음부터 다시 하면 다시 켜진다.
        SetGrabOutline(step == Step.Grab);

        switch (step)
        {
            case Step.Grab:
                grabbedOnce = false;
                break;

            case Step.Walk:
                stickWalkDistance = 0f;
                break;

            case Step.Teleport:
                lastTeleportCountSeen = teleportCount;
                break;

            case Step.Jump:
                jumpedThisStep = false;
                break;

            case Step.RayUI:
                raySubStep = (rayToggle != null && rayToggle.IsUIMode) ? 1 : 0;
                UpdateRaySubStepUI();
                break;

            case Step.XButton:
                SetStatus((int)Step.XButton, "B 버튼으로 Ray를 켜고 [완료]를 선택하세요.", infoColor);
                break;

            case Step.Done:
                PlayClip(finishClip != null ? finishClip : stepClearClip);
                Haptic(0.6f, 0.4f);
                break;
        }
    }

    private void CompleteStep(string message = "잘했어요!")
    {
        if (stepCompleting)
            return;

        stepCompleting = true;
        if (CurrentStep == Step.Grab)
            SetGrabOutline(false);
        SetStatus((int)CurrentStep, message, successColor);
        PlayClip(stepClearClip);
        Haptic(0.5f, 0.2f);
        StartCoroutine(GoNextAfterDelay());
    }

    private void SetGrabOutline(bool on)
    {
        if (grabObject == null)
            return;

        foreach (Outline outline in grabObject.GetComponentsInChildren<Outline>(true))
            outline.enabled = on;
    }

    private IEnumerator GoNextAfterDelay()
    {
        Step finished = CurrentStep;
        yield return new WaitForSeconds(nextStepDelay);

        if (CurrentStep == finished)
            EnterStep(finished + 1);
    }

    // ------------------------------------------------------------------
    // 매 프레임 단계 판정

    private void Update()
    {
        if (xrOrigin == null)
            return;

        TrackStickWalk();
        RespawnGrabObjectIfLost();

        if (stepCompleting)
            return;

        switch (CurrentStep)
        {
            case Step.Grab: UpdateGrab(); break;
            case Step.Walk: UpdateWalk(); break;
            case Step.Teleport: UpdateTeleport(); break;
            case Step.Jump: UpdateJump(); break;
            case Step.RayUI: UpdateRay(); break;
            case Step.XButton: UpdateXButton(); break;
        }
    }

    private void UpdateGrab()
    {
        if (grabObject == null || grabTargetZone == null)
            return;

        if (grabbedOnce && grabTargetZone.bounds.Contains(grabObject.transform.position))
            CompleteStep("잘했어요! 물건을 옮겼어요.");
    }

    private void OnGrabbed()
    {
        if (CurrentStep != Step.Grab || grabbedOnce)
            return;

        grabbedOnce = true;
        SetStatus((int)Step.Grab, "좋아요! 그대로 빛나는 상자 위로 옮겨 놓아보세요.", infoColor);
        Haptic(0.3f, 0.1f);
    }

    private void TrackStickWalk()
    {
        Vector3 pos = xrOrigin.transform.position;
        Vector3 delta = pos - lastOriginPos;
        lastOriginPos = pos;

        delta.y = 0f;
        // 텔레포트처럼 한 번에 크게 이동한 건 걷기로 치지 않는다.
        if (delta.magnitude < 0.3f)
            stickWalkDistance += delta.magnitude;
    }

    private void UpdateWalk()
    {
        if (walkTarget == null)
            return;

        if (IsHeadInside(walkTarget.position, walkRadius))
        {
            if (stickWalkDistance >= walkMinStickDistance)
                CompleteStep("잘했어요! 조이스틱으로 걸어왔어요.");
            else
                SetStatus((int)Step.Walk, "왼쪽 조이스틱으로 걸어서 와보세요.", infoColor);
        }
    }

    private void OnTeleportEnded(LocomotionProvider _)
    {
        teleportCount++;
    }

    private void UpdateTeleport()
    {
        if (teleportTarget == null)
            return;

        bool inside = IsHeadInside(teleportTarget.position, teleportRadius);

        if (teleportCount != lastTeleportCountSeen)
        {
            lastTeleportCountSeen = teleportCount;

            if (inside)
            {
                CompleteStep("잘했어요! 순간이동 성공!");
                return;
            }

            SetStatus((int)Step.Teleport, "좋아요! 이번엔 빛나는 원 안을 겨냥해보세요.", infoColor);
        }
        else if (inside)
        {
            SetStatus((int)Step.Teleport, "걸어서 왔네요! 원 밖으로 나갔다가 텔레포트로 다시 와보세요.", infoColor);
        }
    }

    private void OnJumpStarted(LocomotionProvider _)
    {
        if (CurrentStep != Step.Jump)
            return;

        jumpedThisStep = true;
        SetStatus((int)Step.Jump, "좋아요! 실외기 위에 올라서 보세요.", infoColor);
    }

    private void UpdateJump()
    {
        if (jumpTargetTop == null)
            return;

        Vector3 feet = xrOrigin.transform.position;
        bool onTop = feet.y >= jumpTargetTop.position.y - 0.15f
                     && IsHeadInside(jumpTargetTop.position, jumpRadius);
        bool grounded = characterController == null || characterController.isGrounded;

        if (jumpedThisStep && onTop && grounded)
            CompleteStep("잘했어요! 점프 성공!");
    }

    private void UpdateRay()
    {
        bool uiMode = rayToggle != null && rayToggle.IsUIMode;

        switch (raySubStep)
        {
            case 0:
                if (uiMode) { raySubStep = 1; UpdateRaySubStepUI(); Haptic(0.3f, 0.1f); }
                break;
            case 1:
                if (!uiMode) { raySubStep = 0; UpdateRaySubStepUI(); }
                break;
            case 2:
                if (!uiMode) CompleteStep("잘했어요! Ray를 켜고, 고르고, 껐어요.");
                break;
        }
    }

    private void OnRayNextClicked()
    {
        if (CurrentStep != Step.RayUI || raySubStep != 1)
            return;

        raySubStep = 2;
        UpdateRaySubStepUI();
        PlayClip(stepClearClip);
        Haptic(0.3f, 0.1f);
    }

    private void UpdateRaySubStepUI()
    {
        int i = (int)Step.RayUI;
        switch (raySubStep)
        {
            case 0:
                SetStatus(i, "① 오른쪽 B 버튼을 눌러 Ray를 켜보세요.", infoColor);
                if (rayNextButton != null) rayNextButton.interactable = true;
                break;
            case 1:
                SetStatus(i, "② Ray로 [다음] 버튼을 가리키고 검지 트리거를 당기세요.", infoColor);
                break;
            case 2:
                SetStatus(i, "③ 좋아요! B 버튼을 한 번 더 눌러 Ray를 꺼보세요.", infoColor);
                if (rayNextButton != null) rayNextButton.interactable = false;
                break;
        }
    }

    private void UpdateXButton()
    {
        // X는 안내만 한다. 눌러보면 "그 버튼이에요" 정도로만 반응해 준다.
        InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (left.isValid && left.TryGetFeatureValue(CommonUsages.primaryButton, out bool x))
        {
            if (x && !prevLeftX)
            {
                SetStatus((int)Step.XButton, "맞아요, 그게 X 버튼이에요! 본게임에서 안내 패널을 열고 닫아요.", successColor);
                xFeedbackUntil = Time.time + 2.5f;
                Haptic(0.3f, 0.1f, XRNode.LeftHand);
            }
            prevLeftX = x;
        }

        if (xFeedbackUntil > 0f && Time.time > xFeedbackUntil)
        {
            xFeedbackUntil = 0f;
            SetStatus((int)Step.XButton, "B 버튼으로 Ray를 켜고 [완료]를 선택하세요.", infoColor);
        }
    }

    private void OnFinishClicked()
    {
        if (CurrentStep == Step.XButton)
            EnterStep(Step.Done);
    }

    // ------------------------------------------------------------------
    // 다시 시작

    public void Restart()
    {
        StopAllCoroutines();
        ResetGrabObject();
        MoveRigToStart();
        EnterStep(Step.Grab);
    }

    private void MoveRigToStart()
    {
        if (startPoint == null || xrOrigin == null)
            return;

        Transform origin = xrOrigin.transform;
        Transform head = xrOrigin.Camera != null ? xrOrigin.Camera.transform : origin;

        bool ccWasEnabled = characterController != null && characterController.enabled;
        if (characterController != null) characterController.enabled = false;

        // 머리가 바라보는 방향을 시작 지점 forward에 맞추고, 머리 위치를 시작 지점 위로 옮긴다.
        Vector3 headFwd = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        Vector3 targetFwd = Vector3.ProjectOnPlane(startPoint.forward, Vector3.up);
        if (headFwd.sqrMagnitude > 0.0001f && targetFwd.sqrMagnitude > 0.0001f)
            origin.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(headFwd, targetFwd, Vector3.up));

        Vector3 offset = startPoint.position - head.position;
        offset.y = startPoint.position.y - origin.position.y;
        origin.position += offset;

        if (characterController != null) characterController.enabled = ccWasEnabled;
        lastOriginPos = origin.position;
    }

    private void ResetGrabObject()
    {
        if (grabObject == null)
            return;

        if (grabObject.isSelected && grabObject.interactionManager != null)
            grabObject.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grabObject);

        // 보간(Interpolate)이 켜진 Rigidbody는 Transform만 옮기면 다음 프레임에 원래 자리로 되돌아간다.
        // Rigidbody 위치도 함께 옮겨 순간이동시킨다.
        if (grabBody != null)
        {
            grabBody.linearVelocity = Vector3.zero;
            grabBody.angularVelocity = Vector3.zero;
            grabBody.position = grabSpawnPos;
            grabBody.rotation = grabSpawnRot;
        }
        grabObject.transform.SetPositionAndRotation(grabSpawnPos, grabSpawnRot);
        Physics.SyncTransforms();
    }

    private void RespawnGrabObjectIfLost()
    {
        if (grabObject == null || grabObject.isSelected)
            return;

        Vector3 p = grabObject.transform.position;
        if (p.y < grabSpawnPos.y - 3f || Vector3.Distance(p, grabSpawnPos) > 15f)
            ResetGrabObject();
    }

    // ------------------------------------------------------------------
    // 유틸

    private bool IsHeadInside(Vector3 center, float radius)
    {
        Transform head = xrOrigin.Camera != null ? xrOrigin.Camera.transform : xrOrigin.transform;
        Vector2 a = new Vector2(head.position.x, head.position.z);
        Vector2 b = new Vector2(center.x, center.z);
        return Vector2.Distance(a, b) <= radius;
    }


    private void ApplyStationTexts(int index)
    {
        Station s = stations[index];
        if (s == null)
            return;

        if (s.progressText != null) s.progressText.text = $"{index + 1} / {stations.Length}";
        if (s.titleText != null) s.titleText.text = s.title;
        if (s.bodyText != null) s.bodyText.text = s.body;

        ApplyStationImages(s, Application.isPlaying);
    }

    private static void ApplyStationImages(Station s, bool hideEmpty)
    {
        if (s == null || s.imageSlots == null)
            return;

        for (int k = 0; k < s.imageSlots.Length; k++)
        {
            Image slot = s.imageSlots[k];
            if (slot == null)
                continue;

            Sprite sprite = s.images != null && k < s.images.Length ? s.images[k] : null;
            slot.sprite = sprite;
            slot.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0.12f);

            Transform label = slot.transform.Find("PlaceholderLabel");
            if (label != null)
                label.gameObject.SetActive(sprite == null);

            if (hideEmpty)
                slot.gameObject.SetActive(sprite != null);
        }
    }

#if UNITY_EDITOR
    // 인스펙터에서 이미지를 넣으면 에디터에서도 바로 보이게 한다.
    private void OnValidate()
    {
        // OnValidate 안에서 다른 오브젝트를 바로 바꾸면 경고가 나므로 한 프레임 미룬다.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null || stations == null || Application.isPlaying)
                return;

            foreach (Station s in stations)
                ApplyStationImages(s, false);
        };
    }
#endif

    private void SetStatus(int index, string message, Color color)
    {
        if (index < 0 || index >= stations.Length || stations[index] == null || stations[index].statusText == null)
            return;

        stations[index].statusText.text = message;
        stations[index].statusText.color = color;
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip == null)
            return;

        if (sfxSource != null)
            sfxSource.PlayOneShot(clip);
        else if (xrOrigin != null && xrOrigin.Camera != null)
            AudioSource.PlayClipAtPoint(clip, xrOrigin.Camera.transform.position);
    }

    private static void Haptic(float amplitude, float duration)
    {
        Haptic(amplitude, duration, XRNode.LeftHand);
        Haptic(amplitude, duration, XRNode.RightHand);
    }

    private static void Haptic(float amplitude, float duration, XRNode node)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid)
            device.SendHapticImpulse(0, amplitude, duration);
    }
}
