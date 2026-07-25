using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// AliveStump2 미션 : 떨어지는 노트(LeafNoteButton)를 ClearRow에 맞춰 터치하는 리듬게임.
/// (AliveStump2 하위 MissionPanel에 붙는다. RootActivation이 StartMission()을 호출.)
/// </summary>
public class FallingNoteMission : ActivationMission
{
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

    [Header("터치 피드백")]
    [SerializeField] private Color touchedColor = new Color32(0x87, 0x87, 0x87, 0xFF);
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioClip touchClip;

    // 각 노트 상태
    private class Note
    {
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

        if (notesRoot == null) return;
        notesStartY = notesRoot.anchoredPosition.y;

        foreach (Transform child in notesRoot)
        {
            Button b = child.GetComponent<Button>();
            if (b == null) continue;

            Image img = b.targetGraphic as Image;
            if (img == null) img = child.GetComponent<Image>();

            Note n = new Note();
            n.go = child.gameObject;
            n.rect = child as RectTransform;
            n.button = b;
            n.image = img;
            n.originalColor = (img != null) ? img.color : Color.white;

            Note captured = n;
            b.onClick.AddListener(() => OnNoteClicked(captured));
            notes.Add(n);
        }
    }

    private IEnumerator RunMission()
    {
        // 실패하면 (2)로 돌아오도록 전체 반복
        while (true)
        {
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

            bool failed = false;

            while (true)
            {
                // Notes 아래로 이동
                Vector2 pos = notesRoot.anchoredPosition;
                pos.y -= scrollSpeed * Time.deltaTime;
                notesRoot.anchoredPosition = pos;

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

                    float y = n.rect.position.y;

                    // 가시성 : GamePanel 범위 안에서만 보이고 클릭 가능 (밖이면 숨김)
                    if (n.image != null) n.image.enabled = (y >= panelMin && y <= panelMax);

                    // 미스 : ClearRow 아래로 지나가면 실패
                    if (y < zoneMin) { n.missed = true; failed = true; }
                }

                if (failed) break;
                if (touchedCount >= notes.Count) break;   // 전부 터치 → 클리어

                yield return null;
            }

            // (4) 결과 처리
            if (gamePanel != null) gamePanel.SetActive(false);
            if (notesRoot != null) notesRoot.gameObject.SetActive(false);
            if (firstText != null) firstText.gameObject.SetActive(true);

            if (!failed && touchedCount >= notes.Count)
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
        if (n.touched || n.missed) return;

        // ClearRow 존 안에서만 유효
        float zoneMin, zoneMax;
        GetWorldY(clearRow, out zoneMin, out zoneMax);
        float y = n.rect.position.y;
        if (y < zoneMin || y > zoneMax) return;   // 존 밖 터치 무시

        n.touched = true;
        touchedCount++;

        if (sfxSource != null && touchClip != null) sfxSource.PlayOneShot(touchClip);
        if (n.image != null) n.image.color = touchedColor;
        if (n.button != null) n.button.interactable = false;
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
