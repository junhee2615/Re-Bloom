using System;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// 연구용 수치 로그를 남기는 진입점.
/// 여기서 찍은 로그는 콘솔에 "[RESEARCH] 이벤트 | 값1 | 값2 ..." 형태로 출력되고,
/// ResearchTracker가 이를 받아 호스트 PC에서만 CSV 한 줄로 저장한다.
///
/// 사용 예)
///   ResearchLog.Event("RootFail", "Root2", 3);
///   ResearchLog.SetMission("Waterway");   // 이후 모든 줄의 Mission 열에 기록됨
/// </summary>
public static class ResearchLog
{
    public const string Tag = "[RESEARCH]";
    public const char Separator = '|';

    /// <summary>이벤트 이름과 값들을 연구 로그로 남긴다. 값은 CSV의 Value1, Value2... 열에 들어간다.</summary>
    public static void Event(string eventName, params object[] values)
    {
        var sb = new StringBuilder(Tag).Append(' ').Append(Clean(eventName));

        if (values != null)
        {
            foreach (object v in values)
                sb.Append(' ').Append(Separator).Append(' ').Append(Format(v));
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>현재 진행 중인 미션을 지정한다. 씬이 바뀌면 자동으로 비워진다.</summary>
    public static void SetMission(string missionName)
    {
        ResearchTracker.CurrentMission = missionName ?? string.Empty;
    }

    /// <summary>현재 미션 표시를 비운다.</summary>
    public static void ClearMission()
    {
        ResearchTracker.CurrentMission = string.Empty;
    }

    private static string Format(object v)
    {
        switch (v)
        {
            case null:
                return string.Empty;
            case float f:
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            case double d:
                return d.ToString("0.###", CultureInfo.InvariantCulture);
            case Vector3 p:
                // 쉼표가 CSV 열을 나누지 않도록 공백으로 구분한다.
                return string.Format(CultureInfo.InvariantCulture, "({0:0.###} {1:0.###} {2:0.###})", p.x, p.y, p.z);
            case IFormattable fm:
                return Clean(fm.ToString(null, CultureInfo.InvariantCulture));
            default:
                return Clean(v.ToString());
        }
    }

    // 값 안에 구분자나 줄바꿈이 들어가면 열이 밀리므로 치환한다.
    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        return s.Replace(Separator, '/').Replace('\n', ' ').Replace('\r', ' ').Trim();
    }
}
