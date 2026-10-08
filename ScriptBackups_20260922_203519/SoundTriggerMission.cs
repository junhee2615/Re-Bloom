using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR;
using UnityEngine.UI;

/// <summary>
/// AliveStump 미션 : 뿌리에서 올라오는 리듬 패턴을 '소리'로 들려주고,
/// 그 패턴과 똑같이 컨트롤러 Grab 버튼을 눌러 맞히는 리듬 미션.

/// </summary>
public class SoundTriggerMission : ActivationMission
{
    [Header("UI (MissionPanel 하위)")]
    [SerializeField] private TextMeshProUGUI firstText;
    [SerializeField] private GameObject earImage;
    [SerializeField] private GameObject buttonImage;

    [Header("플레이 버튼 색 (클릭 피드백)")]
    [Tooltip("기본 색 (FFFFFF)")]
    [SerializeField] private Color buttonNormalColor = Color.white;
    [Tooltip("클릭 시 색 (878787)")]
    [SerializeField] private Color buttonPressedColor = new Color32(0x87, 0x87, 0x87, 0xFF);

    [Header("안내 문구")]
    [SerializeField] private string msgRemember = "잘 듣고 리듬을 기억하세요!";
    [SerializeField] private string msgListen = "듣기";
    [SerializeField] private string msgReady = "준비";
    [SerializeField] private string msgPlay = "플레이";
    [SerializeField] private string msgClear = "게임 클리어!";
    [SerializeField] private string msgWrong = "틀렸습니다!";

    [Header("타이밍(초)")]
    [SerializeField] private float listenDelayAfterContact = 1f; // (2) 시작 후 '듣기' 전 대기
    [SerializeField] private float waitAfterVibration = 1f;      // (4) 재생 후 대기
    [SerializeField] private float readyDuration = 2f;           // (6) '준비' 유지
    [SerializeField] private float clearTextDuration = 1.5f;     // (9-1) '게임 클리어!' 표시
    [SerializeField] private float wrongTextDuration = 1.5f;     // (9-2) '틀렸습니다!' 표시

    // 플레이 패정 결과 (성공 여부)
    private bool playSucceeded;

    [Header("리듬 패턴 (소리)")]
    [Tooltip("● = 소리 펄스, 공백 = 긴 쉼. 예: ● ● ●●●")]
    [SerializeField] private string vibrationPattern = "● ● ●●●";
    [Tooltip("펄스 하나의 길이(초)")]
    [SerializeField] private float pulseDuration = 0.12f;
    [Tooltip("붙어있는 펄스(●●) 사이 짧은 쉼(초)")]
    [SerializeField] private float shortGap = 0.12f;
    [Tooltip("공백으로 구분된 펄스 사이 긴 쉼(초)")]
    [SerializeField] private float longGap = 0.4f;

    [Header("효과음")]
    [Tooltip("각 펄스마다 재생할 '똑똑' 효과음")]
    [SerializeField] private AudioSource pulseAudioSource;
    [SerializeField] private AudioClip pulseClip;

    [Header("Grab 판정 (넉넉하게)")]
    [Tooltip("간격 허용 오차(초). 클수록 관대")]
    [SerializeField] private float beatTolerance = 0.35f;
    [Tooltip("각 간격의 이 비율만큼 추가 허용 (0.6 = 60%)")]
    [SerializeField] private float relativeTolerance = 0.6f;
    [Range(0f, 1f)]
    [Tooltip("간격 중 이 비율 이상 맞으면 성공 (0.6 = 60%)")]
    [SerializeField] private float requiredMatchRatio = 0.6f;
    [Tooltip("첫 입력을 기다리는 최대 시간(초)")]
    [SerializeField] private float firstInputTimeout = 6f;
    [Tooltip("입력 사이 무입력이 이만큼 지나면 입력 종료(초)")]
    [SerializeField] private float betweenInputTimeout = 2.5f;

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

    /// <summary>플레이 판정 결과를 전달 (성공/실패).</summary>
    public void ReportPlayResult(bool success) { playSucceeded = success; }

    private IEnumerator RunMission()
    {
        // 실패 시 (1)번으로 돌아오도록 전체를 반복
        while (true)
        {
            // (1) 기억하기 안내
            SetText(msgRemember);
            if (earImage != null) earImage.SetActive(false);
            if (buttonImage != null) buttonImage.SetActive(false);

            // (2) 잠깐 대기 후 → HandImage 켜고 '듣기' (접촉 감지 없이 바로 시작)
            yield return new WaitForSeconds(listenDelayAfterContact);
            if (earImage != null) earImage.SetActive(true);
            SetText(msgListen);

            // (3) 리듬 패턴을 '소리'로 재생
            yield return StartCoroutine(PlayVibrationPattern());

            // (4) 재생 끝나고 1초 대기
            yield return new WaitForSeconds(waitAfterVibration);

            // (5) HandImage 끄고 '준비'
            if (earImage != null) earImage.SetActive(false);
            SetText(msgReady);

            // (6) 2초 대기
            yield return new WaitForSeconds(readyDuration);

            // (7) ButtonImage 켜고 '플레이'
            if (buttonImage != null)
            {
                buttonImage.SetActive(true);
                SetButtonColor(buttonNormalColor);   // 재시도 시 흰색으로 초기화
            }
            SetText(msgPlay);

            // (8) Grab 박자 판정
            playSucceeded = false;
            yield return StartCoroutine(WaitForPlayResult());

            // (9) 결과 처리
            if (buttonImage != null) buttonImage.SetActive(false);

            if (playSucceeded)
            {
                // (9-1) 성공
                SetText(msgClear);
                yield return new WaitForSeconds(clearTextDuration);
                runningRoutine = null;
                Clear();          // RootActivation.CompleteActivation() → 다음 뿌리로
                yield break;
            }
            else
            {
                // (9-2) 실패 → (1)번으로
                SetText(msgWrong);
                yield return new WaitForSeconds(wrongTextDuration);
                // while 루프가 다시 (1)로 돌려보냄
            }
        }
    }

    // (3) 리듬 패턴 재생 (소리만 — 진동 없음)
    protected virtual IEnumerator PlayVibrationPattern()
    {
        // vibrationPattern 문자열을 왼쪽부터 읽으며 펄스/쉼을 재생한다.
        //  ●        → 소리 펄스 1회
        //  ●● (붙음) → 짧은 쉼(shortGap) 뒤 다음 펄스
        //  공백      → 다음 펄스는 긴 쉼(longGap) 뒤에
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

    // 리듬 펄스 1회 = '똑똑' 효과음 재생 (진동 없음)
    private void SendPulse()
    {
        if (pulseAudioSource != null && pulseClip != null)
            AudioSource.PlayClipAtPoint(pulseClip, transform.position);
    }

    // (8) Grab 박자 판정 : (3)의 소리 패턴대로 Grab 버튼을 눌렀는지 비교 (너무 빡빡하지 않게)
    private IEnumerator WaitForPlayResult()
    {
        // (3)에서 들려준 패턴의 '펄스 사이 간격'들을 기대값으로 만든다.
        List<float> expected = BuildExpectedIntervals();
        int expectedCount = expected.Count + 1;   // 간격 수 + 1 = 눌러야 할 횟수

        List<float> pressTimes = new List<float>();
        bool prevPressed = IsGrabPressed();        // 이미 쥐고 있던 입력은 무시
        float startTime = Time.time;

        while (true)
        {
            bool pressed = IsGrabPressed();

            if (pressed && !prevPressed)
            {
                // 누르는 순간 → 박자 기록 + 회색(누르고 있는 동안)
                pressTimes.Add(Time.time);
                SetButtonColor(buttonPressedColor);
                if (pressTimes.Count >= expectedCount)
                    break;
            }
            else if (!pressed && prevPressed)
            {
                // 손을 뗄 순간 → 흰색
                SetButtonColor(buttonNormalColor);
            }
            prevPressed = pressed;

            // 타임아웃: 첫 입력 전 / 입력 사이 무입력이 길면 종료
            float since = (pressTimes.Count == 0)
                ? Time.time - startTime
                : Time.time - pressTimes[pressTimes.Count - 1];
            float limit = (pressTimes.Count == 0) ? firstInputTimeout : betweenInputTimeout;
            if (since > limit)
                break;

            yield return null;
        }

        SetButtonColor(buttonNormalColor);   // 판정 전 흰색으로 복구
        playSucceeded = Judge(expected, pressTimes);
    }

    // ButtonImage의 Image 색을 바꿈
    private void SetButtonColor(Color color)
    {
        if (buttonImage == null) return;
        Image img = buttonImage.GetComponent<Image>();
        if (img != null) img.color = color;
    }

    // 너무 빡빡하지 않게 판정: 누른 횟수가 맞고, 간격이 대충 맞으면 성공
    private bool Judge(List<float> expected, List<float> pressTimes)
    {
        // 누른 횟수가 다르면 실패
        if (pressTimes.Count != expected.Count + 1)
            return false;

        // 펄스가 1개뿐이면 한 번만 누르면 성공
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

        // 간격 중 requiredMatchRatio 이상만 맞으면 통과
        return ok >= Mathf.CeilToInt(expected.Count * requiredMatchRatio);
    }

    // 오른손 컨트롤러 Grab(그립) 버튼을 눌려 있는지
private bool IsGrabPressed()
    {
        // 역할 제한: 이 미션을 할 수 없는 플레이어의 그랩 입력은 인정하지 않음
        if (!CanLocalPlayerPlay()) return false;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.TryGetFeatureValue(CommonUsages.gripButton, out bool pressed))
            return pressed;
        return false;
    }

    // (3)의 소리 패턴에서 '펄스 시작 사이 간격'들을 계산 (재생 로직과 동일한 규칙)
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

    private void SetText(string msg)
    {
        if (firstText == null) return;
        // 텍스트를 보여줄 때 FirstText가 꺼져 있으면 켜준다.
        if (!firstText.gameObject.activeSelf)
            firstText.gameObject.SetActive(true);
        firstText.text = msg;
    }
}
