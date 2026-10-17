using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ReBloom.Water;

/// <summary>
/// 물길 타이밍 미션의 StoolMissionPanel 두 개를 진행 상황에 맞게 보여준다.
/// 각 클라이언트가 RiverbedFlowController 이벤트를 받아 로컬로만 갱신한다 (네트워크 추가 없음).
///   미션 시작        : 패널 활성화, 물방울 전부 회색
///   물결 시작        : 결과 아이콘 숨김, "물결 오는 중." 점 애니메이션
///   회차 성공        : 물방울 하나 원래 색, Ring 아이콘 (마지막 회차는 "성공!" 텍스트)
///   실패             : X 아이콘
///   미션 완료        : "성공!"을 잠깐 보여준 뒤 컷씬 시작, 컷씬 끝나면 패널 비활성화
/// </summary>
public class RiverbedMissionPanel : MonoBehaviour
{
    [System.Serializable]
    public class PanelRefs
    {
        public GameObject root;
        public TMP_Text missionText;
        public Image contentImage;
        public Image[] droplets;
    }

    [Header("연결")]
    public RiverbedFlowController controller;
    public Stage3RiverCutscene cutscene;
    public PanelRefs[] panels;

    [Header("아이콘")]
    public Sprite ringSprite;
    public Sprite xSprite;
    [Tooltip("아직 못 채운 물방울에 쓸 회색 스프라이트. 비우면 grayColor로 틴트만 한다")]
    public Sprite grayDropletSprite;
    public Color grayColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    [Header("텍스트")]
    public string waveText = "물결 오는 중";
    [Min(0.02f)] public float dotInterval = 0.1f;
    public string clearText = "성공!";

    [Header("클리어 연출")]
    [Tooltip("\"성공!\"을 보여준 뒤 컷씬을 시작하기까지 기다리는 시간")]
    public float cutsceneDelay = 1.5f;

    int totalRounds = 3;
    bool cleared;
    Coroutine dotRoutine;
    Coroutine cutsceneRoutine;
    Sprite[][] originalDropletSprites;

    void Awake()
    {
        CacheDroplets();
        SetPanelsActive(false);
    }

    void OnEnable()
    {
        if (controller != null)
        {
            controller.onMissionStarted.AddListener(OnMissionStarted);
            controller.onWaveStarted.AddListener(OnWaveStarted);
            controller.onWaveFailed.AddListener(OnWaveFailed);
            controller.onRoundCleared.AddListener(OnRoundCleared);
            controller.onMissionComplete.AddListener(OnMissionComplete);
        }
        if (cutscene != null)
            cutscene.onCutsceneFinished.AddListener(OnCutsceneFinished);
    }

    void OnDisable()
    {
        if (controller != null)
        {
            controller.onMissionStarted.RemoveListener(OnMissionStarted);
            controller.onWaveStarted.RemoveListener(OnWaveStarted);
            controller.onWaveFailed.RemoveListener(OnWaveFailed);
            controller.onRoundCleared.RemoveListener(OnRoundCleared);
            controller.onMissionComplete.RemoveListener(OnMissionComplete);
        }
        if (cutscene != null)
            cutscene.onCutsceneFinished.RemoveListener(OnCutsceneFinished);
    }

    // ---------------------------------------------------------------
    void OnMissionStarted()
    {
        if (cleared) return;
        if (controller != null && controller.rounds != null && controller.rounds.Count > 0)
            totalRounds = controller.rounds.Count;

        SetPanelsActive(true);
        for (int i = 0; i < 99; i++)
            if (!SetDroplet(i, false)) break;
        ShowText(string.Empty);
    }

    void OnWaveStarted()
    {
        if (cleared) return;
        if (!AnyPanelActive()) SetPanelsActive(true);
        StopDots();
        dotRoutine = StartCoroutine(DotRoutine());
    }

    void OnWaveFailed()
    {
        if (cleared) return;
        ShowIcon(xSprite);
    }

    void OnRoundCleared(int count)
    {
        if (cleared) return;
        SetDroplet(count - 1, true);

        if (count >= totalRounds)
        {
            cleared = true;
            ShowText(clearText);
        }
        else
        {
            ShowIcon(ringSprite);
        }
    }

    void OnMissionComplete()
    {
        cleared = true;
        ShowText(clearText);
        if (cutsceneRoutine == null)
            cutsceneRoutine = StartCoroutine(StartCutsceneAfterDelay());
    }

    IEnumerator StartCutsceneAfterDelay()
    {
        if (cutsceneDelay > 0f)
            yield return new WaitForSeconds(cutsceneDelay);
        if (cutscene != null) cutscene.Play();
        else OnCutsceneFinished();
    }

    void OnCutsceneFinished()
    {
        StopDots();
        SetPanelsActive(false);
    }

    // ---------------------------------------------------------------
    IEnumerator DotRoutine()
    {
        int dots = 1;
        var wait = new WaitForSeconds(dotInterval);
        while (true)
        {
            SetTextOnly(waveText + new string('.', dots));
            dots = dots % 3 + 1;
            yield return wait;
        }
    }

    void StopDots()
    {
        if (dotRoutine != null) StopCoroutine(dotRoutine);
        dotRoutine = null;
    }

    void ShowIcon(Sprite sprite)
    {
        StopDots();
        foreach (var p in panels)
        {
            if (p == null) continue;
            if (p.missionText != null) p.missionText.gameObject.SetActive(false);
            if (p.contentImage != null)
            {
                p.contentImage.sprite = sprite;
                p.contentImage.enabled = sprite != null;
                p.contentImage.gameObject.SetActive(true);
            }
        }
    }

    void ShowText(string text)
    {
        StopDots();
        SetTextOnly(text);
    }

    void SetTextOnly(string text)
    {
        foreach (var p in panels)
        {
            if (p == null) continue;
            if (p.contentImage != null) p.contentImage.enabled = false;
            if (p.missionText != null)
            {
                p.missionText.gameObject.SetActive(true);
                p.missionText.text = text;
            }
        }
    }

    void CacheDroplets()
    {
        if (panels == null) return;
        originalDropletSprites = new Sprite[panels.Length][];
        for (int i = 0; i < panels.Length; i++)
        {
            var d = panels[i] != null ? panels[i].droplets : null;
            originalDropletSprites[i] = new Sprite[d != null ? d.Length : 0];
            for (int j = 0; j < originalDropletSprites[i].Length; j++)
                originalDropletSprites[i][j] = d[j] != null ? d[j].sprite : null;
        }
    }

    // index 번째 물방울을 채우거나(원래 색) 비운다(회색). 범위를 벗어나면 false
    bool SetDroplet(int index, bool filled)
    {
        bool any = false;
        for (int i = 0; i < panels.Length; i++)
        {
            var p = panels[i];
            if (p == null || p.droplets == null || index < 0 || index >= p.droplets.Length) continue;
            var img = p.droplets[index];
            if (img == null) continue;
            any = true;

            if (filled)
            {
                img.sprite = originalDropletSprites[i][index];
                img.color = Color.white;
            }
            else if (grayDropletSprite != null)
            {
                img.sprite = grayDropletSprite;
                img.color = Color.white;
            }
            else
            {
                img.color = grayColor;
            }
        }
        return any;
    }

    void SetPanelsActive(bool active)
    {
        if (panels == null) return;
        foreach (var p in panels)
            if (p != null && p.root != null) p.root.SetActive(active);
    }

    bool AnyPanelActive()
    {
        foreach (var p in panels)
            if (p != null && p.root != null && p.root.activeSelf) return true;
        return false;
    }
}
