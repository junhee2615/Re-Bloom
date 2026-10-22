using UnityEngine;

/// <summary>
/// Stage3 강바닥(간헐천) 물결 미션의 연구 로그. 호스트의 RiverbedMissionNet이 호출한다.
///
/// 기록되는 이벤트 (Mission 열에는 Riverbed가 자동으로 붙는다)
///  - WaveStart  | 물결 번호 | Real/Fake | 예고 구간 길이(초)
///                 → 이 줄의 Time 열이 물결이 오기 시작한 시각(호스트 기준)
///  - WaveInput  | 물결 번호 | 역할(ear/mental) | 물결 시작 후 경과(초) | 판정 시각 대비 오차(초, -=빠름 +=늦음) | Hit/Wrong/Empty
///                 Hit   = 그 플레이어 기준으로 맞춤
///                 Wrong = 가짜 물결을 눌렀거나 타이밍이 틀림
///                 Empty = 물결이 없거나 이미 이번 물결에 입력한 뒤에 누름 (경과·오차 비움)
///  - WaveResult | 물결 번호 | Real/Fake | Success/Fail | mental 입력 결과 | ear 입력 결과
///                 진짜 물결: 둘 다 Hit여야 Success
///                 가짜 물결: 아무도 누르지 않아야 Success
/// </summary>
public static class ResearchRiverbedTracker
{
    public const int ResultHit = 0;
    public const int ResultWrong = 1;
    public const int ResultEmpty = 2;

    private const string NoInput = "None";

    private static int _waveIndex = -1;
    private static bool _waveIsReal;
    private static bool _waveSuccess;
    private static string _mentalResult = NoInput;
    private static string _earResult = NoInput;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _waveIndex = -1;
    }

    /// <summary>호스트가 물결을 송출한 직후.</summary>
    public static void WaveStart(int index, bool isReal, float approachDuration)
    {
        // ResearchTracker가 이벤트를 초기화할 수 있으므로 매번 빼고 다시 넣는다(중복 없음).
        ResearchTracker.SceneExiting -= OnSceneExiting;
        ResearchTracker.SceneExiting += OnSceneExiting;

        FinalizeWave();

        _waveIndex = index;
        _waveIsReal = isReal;
        _waveSuccess = false;
        _mentalResult = NoInput;
        _earResult = NoInput;

        ResearchLog.Event("WaveStart", index, isReal ? "Real" : "Fake", ResearchTracker.FormatSeconds(approachDuration));
    }

    /// <summary>호스트가 어느 플레이어의 입력 보고를 받았을 때(호스트 자신 포함).</summary>
    public static void Input(int index, Role role, float sinceWaveStart, float errorSeconds, int result)
    {
        string resultText = result == ResultHit ? "Hit" : result == ResultWrong ? "Wrong" : "Empty";
        bool empty = result == ResultEmpty;

        ResearchLog.Event("WaveInput", index, role.ToString(),
            empty ? string.Empty : ResearchTracker.FormatSeconds(sinceWaveStart),
            empty ? string.Empty : ResearchTracker.FormatSeconds(errorSeconds),
            resultText);

        // 물결 결과에는 각 플레이어의 '이번 물결 첫 입력'만 반영한다(게임도 첫 입력만 판정한다).
        if (empty || index != _waveIndex)
            return;

        if (role == Role.mental && _mentalResult == NoInput) _mentalResult = resultText;
        else if (role == Role.ear && _earResult == NoInput) _earResult = resultText;
    }

    /// <summary>호스트가 두 사람의 Hit를 모두 받아 성공을 선언했을 때.</summary>
    public static void WaveSuccess(int index)
    {
        if (index != _waveIndex)
            return;

        _waveSuccess = true;
        FinalizeWave();
    }

    private static void OnSceneExiting(bool applicationQuitting)
    {
        FinalizeWave();
    }

    private static void FinalizeWave()
    {
        if (_waveIndex < 0)
            return;

        bool success = _waveIsReal
            ? _waveSuccess
            : _mentalResult == NoInput && _earResult == NoInput;

        ResearchLog.Event("WaveResult", _waveIndex, _waveIsReal ? "Real" : "Fake",
            success ? "Success" : "Fail", _mentalResult, _earResult);

        _waveIndex = -1;
    }
}
