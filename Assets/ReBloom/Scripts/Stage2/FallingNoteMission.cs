using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// AliveStump2 미션 : 떨어지는 노트(LeafNoteButton)를 ClearRow에 맞춰 터치하는 리듬게임.
/// (AliveStump2 하위 MissionPanel에 붙는다. RootActivation이 StartMission()을 호출.)
///
/// 네트워크 모드에서는 두 머신 모두에서 이 코루틴이 돌아 안내 문구·노트 낙하·결과를 함께 본다.
/// 터치 판정은 requiredRole(mental)만 하고, 터치/성공·실패는 RootMissionNet으로 중계되어
/// 관전하는 쪽 화면에서도 같은 노트가 눌린 색으로 바뀐다.
/// </summary>
public class FallingNoteMission : ActivationMission
{
    // 진행 상황 키에 쓰는 페이즈 번호 (이 미션은 단일 페이즈)
    private const int PhaseNotes = 2;

    [Header("UI (MissionPanel 하위)")]
    [SerializeField] private TextMeshProUGUI firstText;
    [SerializeField] private GameObject leafImage;
    [SerializeField] private GameObject gamePanel;      // 배경 (하위에 ClearRow)
    [SerializeField] private RectTransform notesRoot;   // Notes (아래로 이동시키는 컨테이너)
    [SerializeField] private RectTransform clearRow;    // GamePanel/ClearRow (히트 존)

    [Header("문구")]
    [SerializeField] private string msgIntro = "잎이 선에 닿는 순간 터치하세요!";
    [SerializeField] private string msgClear = "게임 클리어!";
    [SerializeField] private string msgWrong = "틀렸습니다!";

    [Header("타이밍 / 속도")]
    [SerializeField] private float introDuration = 2f;      // (2) FirstText+LeafImage 유지
    [SerializeField] private float scrollSpeed = 150f;      // Notes 하강 속도 (px/초)
    [SerializeField] private float noteHideDelay = 1f;      // 터치 후 사라지기까지
    [SerializeField] private float clearTextDuration = 1.5f;
    [SerializeField] private float wrongTextDuration = 1.5f;

    [Header("판정 여유")]
    [Tooltip("ClearRow 위아래로 이만큼(월드 단위)까지 터치 인정. 클수록 관대 (ClearRow 높이가 거의 0이라 필요)")]
    [SerializeField] private float hitTolerance = 0.1f;

    [Header("터치 피드백")]
    [SerializeField] private Color touchedColor = new Color32(0x87, 0x87, 0x87, 0xFF);
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip touchClip;

    // 각 노트 상태
    private class Note
    {
        public int index;
        public GameObject go;
        public RectTransform rect;
        public Button button;
        public Image image;
        public Color originalColor;
        public bool touched;
        public bool missed;
        public float hideTime;
    }

    private readonly List<Note> notes = new List<Note>();
    private float notesStartY;
    private int touchedCount;
    private bool gathered;
    private Coroutine running;

    // 현재 시도 번호 (터치 신호 키 계산에 쓰인다)
    private int currentAttempt;

    public override void StartMission()
    {
        if (running != null) StopCoroutine(running);
        GatherNotes();
        running = StartCoroutine(RunMission());
    }

    public override void StopMission()
    {
        if (running != null) { StopCoroutine(running); running = null; }
    }

    // Notes 하위의 LeafNoteButton들을 한 번만 수집하고 클릭 이벤트를 연결한다.
    private void GatherNotes()
    {
        if (gathered) return;
        gathered = true;

        if (notesRoot == null) { Debug.LogWarning("[FallingNote] notesRoot 미할당"); return; }
        notesStartY = notesRoot.anchoredPosition.y;

        foreach (Transform child in notesRoot)
        {
            Button b = child.GetComponent<Button>();
            if (b == null) continue;

            Image img = b.targetGraphic as Image;
            if (img == null) img = child.GetComponent<Image>();

            Note n = new Note();
            n.index = notes.Count;           // 두 머신이 같은 순서로 수집하므로 인덱스가 곧 공통 식별자
            n.go = child.gameObject;
            n.rect = child as RectTransform;
            n.button = b;
            n.image = img;
            n.originalColor = (img != null) ? img.color : Color.white;

            // 누르는 순간(PointerDown)에 바로 반응하도록 LeafNote 연결 (움직이는 노트에 유리)
            LeafNote ln = child.GetComponent<LeafNote>();
            if (ln == null) ln = child.gameObject.AddComponent<LeafNote>();
            Note captured = n;
            ln.onPressed = () => OnNoteClicked(captured);

            notes.Add(n);
        }

        Debug.Log($"[FallingNote] 노트 {notes.Count}개 수집 (clearRow 할당={clearRow != null}, gamePanel 할당={gamePanel != null})");
    }

    private IEnumerator RunMission()
    {
        // 실패하면 (2)로 돌아오도록 전체 반복
        int attempt = 0;

        for (;;)
        {
            currentAttempt = attempt;

            // (2) 안내 : FirstText + LeafImage, 2초 대기
            ResetRound();
            if (leafImage != null) leafImage.SetActive(true);
            if (firstText != null) { firstText.gameObject.SetActive(true); firstText.text = msgIntro; }
            if (gamePanel != null) gamePanel.SetActive(false);
            if (notesRoot != null) notesRoot.gameObject.SetActive(false);

            yield return new WaitForSeconds(introDuration);

            // (3) 게임 시작 : GamePanel + Notes 켜고 내려보냄
            if (leafImage != null) leafImage.SetActive(false);
            if (firstText != null) firstText.gameObject.SetActive(false);
            if (gamePanel != null) gamePanel.SetActive(true);
            if (notesRoot != null) notesRoot.gameObject.SetActive(true);

            // 판정은 수행 역할만. 관전자는 노트를 같이 흘려보며 중계된 결과를 기다린다.
            bool judge = IsJudge;
            int key = ResultKey(PhaseNotes, attempt);
            bool success = false;
            bool failed = false;

            for (;;)
            {
                // Notes 아래로 이동
                if (notesRoot != null)
                {
                    Vector2 pos = notesRoot.anchoredPosition;
                    pos.y -= scrollSpeed * Time.deltaTime;
                    notesRoot.anchoredPosition = pos;
                }

                float zoneMin, zoneMax, panelMin, panelMax;
                GetWorldY(clearRow, out zoneMin, out zoneMax);
                GetWorldY(gamePanel != null ? gamePanel.transform as RectTransform : null, out panelMin, out panelMax);

                foreach (Note n in notes)
                {
                    if (n.touched)
                    {
                        // 터치된 노트는 hideDelay 뒤 사라짐
                        if (n.go.activeSelf && Time.time >= n.hideTime)
                            n.go.SetActive(false);
                        continue;
                    }
                    if (n.missed) continue;

                    // 관전자: 상대가 터치한 노트를 같은 색으로 표시
                    if (!judge && HasSignal(SignalKey(PhaseNotes, attempt, n.index)))
                    {
                        MarkTouched(n, false);
                        continue;
                    }

                    // 노트의 월드 Y 범위
                    float nMin, nMax;
                    GetWorldY(n.rect, out nMin, out nMax);

                    // 가시성 : 노트가 GamePanel과 겹치면 보임 (밖이면 숨김/클릭불가)
                    bool vis = (nMax >= panelMin && nMin <= panelMax);
                    if (n.image != null)
                        n.image.enabled = vis;

                    // 미스 : 노트가 ClearRow 아래로 완전히 지나가면 실패 (판정자만)
                    if (judge && nMax < zoneMin - hitTolerance)
                    {
                        n.missed = true;
                        failed = true;
                        Debug.Log($"[FallingNote] {n.go.name} MISS (nMax={nMax:F2} < zoneMin={zoneMin:F2})");
                    }
                }

                if (judge)
                {
                    if (failed) { success = false; break; }
                    if (touchedCount >= notes.Count) { success = true; break; }   // 전부 터치 → 클리어
                }
                else
                {
                    if (TryGetResult(key, out success)) break;
                }

                yield return null;
            }

            if (judge) SubmitResult(key, success);
            ClearRoundKeys(PhaseNotes, attempt, notes.Count);

            // (4) 결과 처리
            if (gamePanel != null) gamePanel.SetActive(false);
            if (notesRoot != null) notesRoot.gameObject.SetActive(false);
            if (firstText != null) firstText.gameObject.SetActive(true);

            if (success)
            {
                // (4-1) 클리어
                SetText(msgClear);
                yield return new WaitForSeconds(clearTextDuration);
                running = null;
                Clear();     // RootActivation.CompleteActivation() → 다음 뿌리로
                yield break;
            }
            else
            {
                // (4-2) 실패 → (2)로
                SetText(msgWrong);
                yield return new WaitForSeconds(wrongTextDuration);
                attempt++;
            }
        }
    }

    // 라운드 시작 상태로 초기화
    private void ResetRound()
    {
        touchedCount = 0;

        if (notesRoot != null)
        {
            Vector2 p = notesRoot.anchoredPosition;
            p.y = notesStartY;
            notesRoot.anchoredPosition = p;
        }

        foreach (Note n in notes)
        {
            n.touched = false;
            n.missed = false;
            n.hideTime = 0f;
            if (n.go != null) n.go.SetActive(true);
            if (n.button != null) n.button.interactable = true;
            if (n.image != null)
            {
                n.image.color = n.originalColor;
                n.image.enabled = false;   // 시작엔 패널 밖이므로 숨김
            }
        }
    }

    // 노트를 터치(클릭)했을 때
    private void OnNoteClicked(Note n)
    {
        // 역할 제한: 이 미션을 할 수 없는 플레이어의 터치는 카운트하지 않음
        if (!CanLocalPlayerPlay()) return;

        float zMin, zMax, nMin, nMax;
        GetWorldY(clearRow, out zMin, out zMax);
        GetWorldY(n.rect, out nMin, out nMax);
        bool inZone = (nMax >= zMin - hitTolerance && nMin <= zMax + hitTolerance);

        if (n.touched || n.missed) return;

        if (!inZone)
        {
            Debug.Log($"[FallingNote] {n.go.name} 존 밖이라 무시");
            return;
        }

        MarkTouched(n, true);
        Debug.Log($"[FallingNote] {n.go.name} 터치 성공! ({touchedCount}/{notes.Count})");

        // 상대 화면에도 같은 노트가 눌린 것으로 보이게 중계
        SubmitSignal(SignalKey(PhaseNotes, currentAttempt, n.index));
    }

    // 노트를 '터치됨' 상태로 바꾼다. countIt=false면 화면 표시만(관전자용).
    private void MarkTouched(Note n, bool countIt)
    {
        n.touched = true;
        if (countIt) touchedCount++;

        if (touchClip != null) AudioSource.PlayClipAtPoint(touchClip, transform.position);
        if (n.image != null)
        {
            n.image.enabled = true;
            n.image.color = touchedColor;
        }
        n.hideTime = Time.time + noteHideDelay;
    }

    // RectTransform의 월드 Y 범위(min/max)
    private void GetWorldY(RectTransform rt, out float minY, out float maxY)
    {
        if (rt == null) { minY = 0f; maxY = 0f; return; }
        Vector3[] c = new Vector3[4];
        rt.GetWorldCorners(c);   // 0=BL, 1=TL, 2=TR, 3=BR
        minY = c[0].y;
        maxY = c[1].y;
    }

    private void SetText(string msg)
    {
        if (firstText != null) firstText.text = msg;
    }
}
