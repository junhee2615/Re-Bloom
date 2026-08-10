using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using TMPro;

/// <summary>
/// AliveStump3 미션 : 두 플레이어가 협동하는 리듬 미션.
///  - StartButton은 누구나 누를 수 있고(requiredRole = Any),
///  - 리듬 소리는 soundRole(기본 Host)만 들리며,
///  - Grab 입력은 grabRole(기본 Client)만 카운트된다.
/// (Host가 들은 박자를 Client에게 알려주고, Client가 그 박자대로 Grab을 누른다.)
/// </summary>
public class CombinedMission : ActivationMission
{
    [Header("UI (MissionPanel 하위)")]
    [SerializeField] private TextMeshProUGUI firstText;
    [SerializeField] private GameObject earImage;
    [SerializeField] private GameObject buttonImage;

    [Header("플레이 버튼 색")]
    [SerializeField] private Color buttonNormalColor = Color.white;
    [SerializeField] private Color buttonPressedColor = new Color32(0x87, 0x87, 0x87, 0xFF);

    [Header("안내 문구 (기억하기 1~3)")]
    [TextArea]
    [SerializeField] private string msgRemember1 = "체력이 고갈되었습니다!\n청각 제약 캐릭터는 잠시 동안 소리를 들을 수 없고,\n정신 제약 캐릭터는\n떨어지는 노트가 이상하게 보이게 됩니다.";
    [TextArea]
    [SerializeField] private string msgRemember2 = "두 캐릭터는 협동하며 미션을 해결해야 합니다.";
    [TextArea]
    [SerializeField] private string msgRemember3 = "정신 제약 캐릭터는 들리는 소리의 박자를\n청각 제약 캐릭터에게 전달해주세요.";
    [SerializeField] private string msgListen = "듣기";
    [SerializeField] private string msgReady = "준비";
    [SerializeField] private string msgPlay = "플레이";
    [SerializeField] private string msgClear = "게임 클리어!";
    [SerializeField] private string msgWrong = "틀렸습니다!";

    [Header("타이밍(초)")]
    [SerializeField] private float introMsgDuration = 3f;        // (1~3) 안내 문구 각각 표시 시간
    [SerializeField] private float listenDelayAfterContact = 1f; // (4)
    [SerializeField] private float waitAfterVibration = 1f;      // (6)
    [SerializeField] private float readyDuration = 2f;           // (8)
    [SerializeField] private float clearTextDuration = 1.5f;
    [SerializeField] private float wrongTextDuration = 1.5f;

    [Header("리듬 패턴 (소리)")]
    [SerializeField] private string vibrationPattern = "●●● ● ● ";
    [SerializeField] private float pulseDuration = 0.12f;
    [SerializeField] private float shortGap = 0.12f;
    [SerializeField] private float longGap = 0.4f;

    [Header("효과음")]
    [SerializeField] private AudioSource pulseAudioSource;
    [SerializeField] private AudioClip pulseClip;

    [Header("협동 역할")]
    [Tooltip("리듬 소리를 듣는 역할 (기본 Host)")]
    [SerializeField] private MissionRole soundRole = MissionRole.HostOnly;
    [Tooltip("Grab 입력이 카운트되는 역할 (기본 Client)")]
    [SerializeField] private MissionRole grabRole = MissionRole.ClientOnly;

    [Header("Grab 판정 (넉넉하게)")]
    [SerializeField] private float beatTolerance = 0.35f;
    [SerializeField] private float relativeTolerance = 0.6f;
    [Range(0f, 1f)]
    [SerializeField] private float requiredMatchRatio = 0.6f;
    [SerializeField] private float firstInputTimeout = 6f;
    [SerializeField] private float betweenInputTimeout = 2.5f;

    [Header("2부: 노트 (FallingNote)")]
    [SerializeField] private GameObject leafImage;
    [SerializeField] private GameObject gameNotes;      // GameNotes 래퍼
    [SerializeField] private GameObject gamePanel;      // 배경
    [SerializeField] private RectTransform clearRow;    // 히트 존
    [SerializeField] private RectTransform clientNote;  // Client용 노트 (보임)
    [SerializeField] private RectTransform hostNote;    // Host용 노트 (안 보이지만 클릭)
    [Tooltip("노트 터치를 카운트하는 역할 (기본 Host)")]
    [SerializeField] private MissionRole noteRole = MissionRole.HostOnly;
    [TextArea]
    [SerializeField] private string msgNoteIntro = "청각 제약 캐릭터는 노트가 빨간 선에 닿을 때\n노트가 몇 번 레인에 있는지 알려주세요.";
    [SerializeField] private float phaseGap = 3f;          // 1부 클리어 후 대기
    [SerializeField] private float noteIntroDuration = 2f;
    [SerializeField] private float scrollSpeed = 150f;
    [SerializeField] private float noteHideDelay = 1f;
    [SerializeField] private float hitTolerance = 0.1f;
    [SerializeField] private Color noteTouchedColor = new Color32(0x87, 0x87, 0x87, 0xFF);
    [SerializeField] private AudioClip noteTouchClip;

    private class NoteObj
    {
        public GameObject go;
        public RectTransform rect;
        public Image image;
        public Color originalColor;
        public bool touched;
        public bool missed;
        public float hideTime;
    }
    private readonly List<NoteObj> noteList = new List<NoteObj>();
    private RectTransform activeNoteRoot;
    private float noteStartY;
    private int noteTouchedCount;
    private bool notesGathered;

    private bool playSucceeded;
    private Coroutine runningRoutine;

    public override void StartMission()
    {
        if (runningRoutine != null) StopCoroutine(runningRoutine);
        runningRoutine = StartCoroutine(RunMission());
    }

    public override void StopMission()
    {
        if (runningRoutine != null) { StopCoroutine(runningRoutine); runningRoutine = null; }
    }

private IEnumerator RunMission()
    {
        // 1부(소리) → phaseGap 대기 → 2부(노트) → 전체 완료
        yield return RunSoundPhase();
        yield return new WaitForSeconds(phaseGap);
        yield return RunNotePhase();

        runningRoutine = null;
        Clear();          // RootActivation.CompleteActivation() → 다음 뿌리로
    }

    // (5) 리듬 패턴 재생 — 타이밍은 모두 진행, 소리는 soundRole만 재생
    protected virtual IEnumerator PlayPattern()
    {
        bool first = true;
        bool pendingLongGap = false;

        foreach (char c in vibrationPattern)
        {
            if (c == '●')
            {
                if (!first)
                    yield return new WaitForSeconds(pendingLongGap ? longGap : shortGap);

                SendPulse();
                yield return new WaitForSeconds(pulseDuration);

                first = false;
                pendingLongGap = false;
            }
            else if (c == ' ')
            {
                pendingLongGap = true;
            }
        }
    }

    // 소리 펄스 1회 — soundRole(기본 Host)만 듣는다
    private void SendPulse()
    {
        if (!LocalMatches(soundRole)) return;
        if (pulseAudioSource != null && pulseClip != null)
            pulseAudioSource.PlayOneShot(pulseClip);
    }

    // (10) Grab 박자 판정 (grabRole 입력만 카운트)
    private IEnumerator WaitForPlayResult()
    {
        List<float> expected = BuildExpectedIntervals();
        int expectedCount = expected.Count + 1;

        List<float> pressTimes = new List<float>();
        bool prevPressed = IsGrabPressed();
        float startTime = Time.time;

        while (true)
        {
            bool pressed = IsGrabPressed();

            if (pressed && !prevPressed)
            {
                pressTimes.Add(Time.time);
                SetButtonColor(buttonPressedColor);
                if (pressTimes.Count >= expectedCount)
                    break;
            }
            else if (!pressed && prevPressed)
            {
                SetButtonColor(buttonNormalColor);
            }
            prevPressed = pressed;

            float since = (pressTimes.Count == 0)
                ? Time.time - startTime
                : Time.time - pressTimes[pressTimes.Count - 1];
            float limit = (pressTimes.Count == 0) ? firstInputTimeout : betweenInputTimeout;
            if (since > limit)
                break;

            yield return null;
        }

        SetButtonColor(buttonNormalColor);
        playSucceeded = Judge(expected, pressTimes);
    }

    // 그랩 입력 — grabRole(기본 Client)만 카운트
    private bool IsGrabPressed()
    {
        if (!LocalMatches(grabRole)) return false;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed))
            return pressed;
        return false;
    }

    private void SetButtonColor(Color color)
    {
        if (buttonImage == null) return;
        Image img = buttonImage.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    private bool Judge(List<float> expected, List<float> pressTimes)
    {
        if (pressTimes.Count != expected.Count + 1)
            return false;
        if (expected.Count == 0)
            return true;

        int ok = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            float playerGap = pressTimes[i + 1] - pressTimes[i];
            float allow = Mathf.Max(beatTolerance, expected[i] * relativeTolerance);
            if (Mathf.Abs(playerGap - expected[i]) <= allow)
                ok++;
        }
        return ok >= Mathf.CeilToInt(expected.Count * requiredMatchRatio);
    }

    private List<float> BuildExpectedIntervals()
    {
        List<float> intervals = new List<float>();
        bool first = true;
        bool pendingLongGap = false;

        foreach (char c in vibrationPattern)
        {
            if (c == '●')
            {
                if (!first)
                    intervals.Add(pulseDuration + (pendingLongGap ? longGap : shortGap));
                first = false;
                pendingLongGap = false;
            }
            else if (c == ' ')
            {
                pendingLongGap = true;
            }
        }
        return intervals;
    }

    // 주어진 역할이 로컬 플레이어와 맞는지 (미연결 = 단독 테스트는 허용)
    private bool LocalMatches(MissionRole role)
    {
        if (!PlayerRole.IsConnected) return true;
        switch (role)
        {
            case MissionRole.HostOnly:   return PlayerRole.LocalIsHost();
            case MissionRole.ClientOnly: return PlayerRole.LocalIsClient();
            default:                     return true;
        }
    }

    private void SetText(string msg)
    {
        if (firstText == null) return;
        if (!firstText.gameObject.activeSelf)
            firstText.gameObject.SetActive(true);
        firstText.text = msg;
    }


private void GetWorldY(RectTransform rt, out float minY, out float maxY)
    {
        if (rt == null) { minY = 0f; maxY = 0f; return; }
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c);
        minY = c[0].y;
        maxY = c[1].y;
    }


// 노트 터치 — noteRole(기본 Host)만 카운트
    private void OnNoteClicked(NoteObj n)
    {
        if (!LocalMatches(noteRole)) return;
        if (n.touched || n.missed) return;

        float zMin, zMax, nMin, nMax;
        GetWorldY(clearRow, out zMin, out zMax);
        GetWorldY(n.rect, out nMin, out nMax);
        if (nMax < zMin - hitTolerance || nMin > zMax + hitTolerance) return;   // ClearRow 밖

        n.touched = true;
        noteTouchedCount++;
        if (pulseAudioSource != null && noteTouchClip != null) pulseAudioSource.PlayOneShot(noteTouchClip);
        if (n.image != null) n.image.color = noteTouchedColor;
        n.hideTime = Time.time + noteHideDelay;
    }


private void ResetNoteRound()
    {
        noteTouchedCount = 0;

        if (activeNoteRoot != null)
        {
            Vector2 p = activeNoteRoot.anchoredPosition;
            p.y = noteStartY;
            activeNoteRoot.anchoredPosition = p;
        }

        foreach (NoteObj n in noteList)
        {
            n.touched = false;
            n.missed = false;
            n.hideTime = 0f;
            if (n.go != null) n.go.SetActive(true);
            if (n.image != null)
            {
                n.image.color = n.originalColor;
                n.image.enabled = false;
            }
        }
    }


// 역할에 맞는 노트 세트를 골라 수집 (한 번만). Host 세트는 안 보이지만 클릭 가능.
    private void GatherNotes()
    {
        if (notesGathered) return;
        notesGathered = true;

        bool useHost = PlayerRole.IsConnected && PlayerRole.LocalIsHost();
        activeNoteRoot = useHost ? hostNote : clientNote;
        if (activeNoteRoot == null) { Debug.LogWarning("[Combined] 노트 컨테이너 미할당"); return; }

        // Host 세트: 투명(alpha 0)하지만 클릭은 되도록 CanvasGroup 처리
        if (useHost)
        {
            CanvasGroup cg = activeNoteRoot.GetComponent<CanvasGroup>();
            if (cg == null) cg = activeNoteRoot.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            cg.blocksRaycasts = true;
            cg.interactable = true;
        }

        noteStartY = activeNoteRoot.anchoredPosition.y;

        foreach (Transform child in activeNoteRoot)
        {
            Button b = child.GetComponent<Button>();
            if (b == null) continue;

            Image img = b.targetGraphic as Image;
            if (img == null) img = child.GetComponent<Image>();

            NoteObj n = new NoteObj();
            n.go = child.gameObject;
            n.rect = child as RectTransform;
            n.image = img;
            n.originalColor = (img != null) ? img.color : Color.white;

            LeafNote ln = child.GetComponent<LeafNote>();
            if (ln == null) ln = child.gameObject.AddComponent<LeafNote>();
            NoteObj captured = n;
            ln.onPressed = () => OnNoteClicked(captured);

            noteList.Add(n);
        }

        Debug.Log("[Combined] 노트 " + noteList.Count + "개 수집 (Host세트=" + useHost + ")");
    }


// 2부: 떨어지는 노트 (클리어할 때까지 내부 반복)
// 2부: 떨어지는 노트 (클리어할 때까지 내부 반복). 클릭 판정=noteRole(Host), 상대는 노트를 보며 결과 대기.
    private IEnumerator RunNotePhase()
    {
        GatherNotes();

        int attempt = 0;
        while (true)
        {
            // (1) 안내
            ResetNoteRound();
            if (earImage != null) earImage.SetActive(false);
            if (buttonImage != null) buttonImage.SetActive(false);
            if (leafImage != null) leafImage.SetActive(true);
            if (gameNotes != null) gameNotes.SetActive(false);
            SetText(msgNoteIntro);
            yield return new WaitForSeconds(noteIntroDuration);

            // (2) 게임 시작
            if (leafImage != null) leafImage.SetActive(false);
            if (firstText != null) firstText.gameObject.SetActive(false);
            if (gameNotes != null) gameNotes.SetActive(true);
            if (clientNote != null) clientNote.gameObject.SetActive(activeNoteRoot == clientNote);
            if (hostNote != null) hostNote.gameObject.SetActive(activeNoteRoot == hostNote);

            RootMissionNet net = RootMissionNet.Instance;
            bool connected = net != null;
            bool iJudge = !connected || LocalMatches(noteRole);
            int key = NoteKey(attempt);

            bool success = false;
            bool failed = false;

            while (true)
            {
                if (activeNoteRoot != null)
                {
                    Vector2 pos = activeNoteRoot.anchoredPosition;
                    pos.y -= scrollSpeed * Time.deltaTime;
                    activeNoteRoot.anchoredPosition = pos;
                }

                float zoneMin, zoneMax, panelMin, panelMax;
                GetWorldY(clearRow, out zoneMin, out zoneMax);
                GetWorldY(gamePanel != null ? gamePanel.transform as RectTransform : null, out panelMin, out panelMax);

                foreach (NoteObj n in noteList)
                {
                    if (n.touched)
                    {
                        if (n.go.activeSelf && Time.time >= n.hideTime)
                            n.go.SetActive(false);
                        continue;
                    }
                    if (n.missed) continue;

                    float nMin, nMax;
                    GetWorldY(n.rect, out nMin, out nMax);

                    bool vis = (nMax >= panelMin && nMin <= panelMax);
                    if (n.image != null) n.image.enabled = vis;

                    // 판정자만 놓침(miss) 판정
                    if (iJudge && nMax < zoneMin - hitTolerance) { n.missed = true; failed = true; }
                }

                if (iJudge)
                {
                    if (failed) { success = false; break; }
                    if (noteTouchedCount >= noteList.Count) { success = true; break; }
                }
                else
                {
                    // 관전자: 노트를 시각적으로 흘리기만 하고, 판정자의 결과 도착 시 종료
                    if (net.TryGetResult(key, out success)) break;
                }

                yield return null;
            }

            if (iJudge && connected) net.SubmitResult(key, success);
            if (connected) net.ClearResult(key);

            // (3) 결과
            if (gameNotes != null) gameNotes.SetActive(false);
            if (firstText != null) firstText.gameObject.SetActive(true);

            if (success)
            {
                SetText(msgClear);
                yield return new WaitForSeconds(clearTextDuration);
                yield break;   // 2부 클리어
            }
            else
            {
                SetText(msgWrong);
                yield return new WaitForSeconds(wrongTextDuration);
            }
            attempt++;
        }
    }

// 페이즈별 재시도(attempt) 단위로 유일한 판정 결과 키 (두 머신이 동일하게 계산)
    private int SoundKey(int attempt) { return missionIndex * 10000 + 1000 + attempt; }
    private int NoteKey(int attempt) { return missionIndex * 10000 + 2000 + attempt; }



// 1부: 소리 리듬 (클리어할 때까지 내부 반복)
// 1부: 소리 리듬 (클리어할 때까지 내부 반복). 소리=soundRole, 판정=grabRole.
    private IEnumerator RunSoundPhase()
    {
        int attempt = 0;
        while (true)
        {
            // (1)(2)(3) 기억하기 안내
            if (earImage != null) earImage.SetActive(false);
            if (buttonImage != null) buttonImage.SetActive(false);
            if (leafImage != null) leafImage.SetActive(false);
            if (gameNotes != null) gameNotes.SetActive(false);
            SetText(msgRemember1);
            yield return new WaitForSeconds(introMsgDuration);
            SetText(msgRemember2);
            yield return new WaitForSeconds(introMsgDuration);
            SetText(msgRemember3);
            yield return new WaitForSeconds(introMsgDuration);

            // (4) 듣기
            yield return new WaitForSeconds(listenDelayAfterContact);
            if (earImage != null) earImage.SetActive(true);
            SetText(msgListen);

            // (5) 소리 재생 (soundRole만 들림)
            yield return PlayPattern();

            // (6)
            yield return new WaitForSeconds(waitAfterVibration);

            // (7)
            if (earImage != null) earImage.SetActive(false);
            SetText(msgReady);

            // (8)
            yield return new WaitForSeconds(readyDuration);

            // (9)
            if (buttonImage != null) { buttonImage.SetActive(true); SetButtonColor(buttonNormalColor); }
            SetText(msgPlay);

            // (10) 판정 — grabRole 담당(Client)이 판정, 그 결과를 상대(Host)에게 중계
            bool success = false;
            RootMissionNet net = RootMissionNet.Instance;
            bool connected = net != null;
            bool iJudge = !connected || LocalMatches(grabRole);
            int key = SoundKey(attempt);

            if (iJudge)
            {
                playSucceeded = false;
                yield return WaitForPlayResult();
                success = playSucceeded;
                if (connected) net.SubmitResult(key, success);
            }
            else
            {
                // 상대(Client)의 Grab 판정 결과를 기다린다
                while (!net.TryGetResult(key, out success))
                    yield return null;
            }
            if (connected) net.ClearResult(key);

            // (11)
            if (buttonImage != null) buttonImage.SetActive(false);
            if (success)
            {
                SetText(msgClear);
                yield return new WaitForSeconds(clearTextDuration);
                yield break;   // 1부 클리어
            }
            else
            {
                SetText(msgWrong);
                yield return new WaitForSeconds(wrongTextDuration);
            }
            attempt++;
        }
    }
}
