using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// [RESEARCH] 태그가 붙은 로그만 골라 CSV로 저장하는 트래커.
///
/// - 게임 시작 시 자동 생성되며(씬 배치 불필요) DontDestroyOnLoad로 유지된다.
/// - 호스트(Runner.IsServer) PC에서만 기록한다. 클라이언트에서는 파일을 만들지 않는다.
///   호스트로 기록하던 실행이라면, 세션이 끝난 뒤(Runner 없음)에 나오는 마무리 기록도 남긴다.
/// - 게임을 실행할 때마다 첫 기록 시점에 새 CSV 파일을 만든다.
///   위치: 내 문서\Rebloom_log\Rebloom_yyyy-MM-dd_HH-mm-ss.csv
///   (문서 폴더를 쓸 수 없으면 Application.persistentDataPath\Rebloom_log)
/// - 모든 줄에 Time.time 기준 시각, 현재 스테이지(씬 이름), 현재 미션이 자동으로 붙는다.
/// </summary>
public class ResearchTracker : MonoBehaviour
{
    public const string FolderName = "Rebloom_log";

    private static readonly string[] Header =
    {
        "Time", "Clock", "Stage", "Mission", "Event",
        "Value1", "Value2", "Value3", "Value4", "Value5"
    };

    /// <summary>
    /// 현재 미션이 바뀌기 직전에 발행된다. 이 안에서 남긴 로그는 아직 이전 미션으로 기록된다.
    /// </summary>
    public static event Action MissionChanging;

    /// <summary>
    /// 씬을 나가기 직전(씬 전환, 게임 종료)에 발행된다. 인자는 게임 종료 여부.
    /// 이 안에서 남긴 로그는 아직 이전 스테이지·미션으로 기록된다.
    /// </summary>
    public static event Action<bool> SceneExiting;

    public static ResearchTracker Instance { get; private set; }

    /// <summary>현재 활성 씬 이름.</summary>
    public static string CurrentStage { get; private set; } = string.Empty;

    private static string _currentMission = string.Empty;

    /// <summary>현재 미션. ResearchLog.SetMission으로 지정하고 씬이 바뀌면 비워진다.</summary>
    public static string CurrentMission
    {
        get => _currentMission;
        internal set
        {
            value ??= string.Empty;
            if (value == _currentMission)
                return;

            MissionChanging?.Invoke();
            _currentMission = value;
        }
    }

    /// <summary>이번 실행에서 기록 중인 CSV 경로. 아직 기록 전이면 null.</summary>
    public static string FilePath { get; private set; }

    /// <summary>현재 씬에 들어온 시각(Time.time).</summary>
    public static float SceneEnterTime { get; private set; }

    private StreamWriter _writer;
    private bool _fileFailed;
    private readonly StringBuilder _row = new StringBuilder(256);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        // Enter Play Mode 옵션으로 도메인 리로드가 꺼져 있어도 이전 값이 남지 않게 초기화한다.
        MissionChanging = null;
        SceneExiting = null;
        CurrentStage = string.Empty;
        _currentMission = string.Empty;
        FilePath = null;
        SceneEnterTime = 0f;

        if (Instance != null)
            return;

        var go = new GameObject("[ResearchTracker]");
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<ResearchTracker>();
        go.AddComponent<ResearchResonanceTracker>();
    }

    private void OnEnable()
    {
        Application.logMessageReceived += OnLogMessage;
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    private void Start()
    {
        CurrentStage = SceneManager.GetActiveScene().name;
        SceneEnterTime = Time.time;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessage;
        SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        CloseWriter();
    }

    private void OnApplicationQuit()
    {
        SceneExiting?.Invoke(true);

        // 기록 중이던 스테이지의 체류 시간을 남기고 닫는다.
        if (_writer != null)
            WriteRow("SceneExit", new[] { FormatSeconds(Time.time - SceneEnterTime) });

        CloseWriter();
    }

    private void OnActiveSceneChanged(Scene previous, Scene next)
    {
        // 스테이지·미션 단위 집계를 이전 스테이지 이름으로 먼저 남긴다.
        SceneExiting?.Invoke(false);

        // 이전 씬의 체류 시간. 세션이 끝난 뒤(StartScene 복귀)에도 남기도록
        // 호스트 판정 대신 '이번 실행에서 기록 중인지'로 판단한다(파일은 호스트만 만든다).
        if (_writer != null)
            WriteRow("SceneExit", new[] { FormatSeconds(Time.time - SceneEnterTime) });

        CurrentMission = string.Empty;
        CurrentStage = next.name;
        SceneEnterTime = Time.time;

        // 스테이지 입장 시각을 남겨 두면 스테이지 단위 플레이 시간을 계산할 수 있다.
        if (IsHost())
            WriteRow("SceneEnter", Array.Empty<string>());
    }

    public static string FormatSeconds(float seconds)
    {
        return seconds.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(condition))
            return;

        int tagIndex = condition.IndexOf(ResearchLog.Tag, StringComparison.Ordinal);
        if (tagIndex < 0)
            return;

        if (!ShouldRecord())
            return;

        string body = condition.Substring(tagIndex + ResearchLog.Tag.Length);
        string[] parts = body.Split(ResearchLog.Separator);

        string eventName = parts[0].Trim();
        var values = new string[parts.Length - 1];
        for (int i = 1; i < parts.Length; i++)
            values[i - 1] = parts[i].Trim();

        WriteRow(eventName, values);
    }

    /// <summary>호스트일 때만 true. 네트워크 세션 밖(StartScene 등)에서는 false.</summary>
    public static bool IsHost()
    {
        NetworkManager nm = NetworkManager.Instance;
        var runner = nm != null ? nm.Runner : null;
        return runner != null && runner.IsRunning && runner.IsServer;
    }

    // 호스트이거나, 호스트로 기록하던 실행에서 세션이 끝난 직후(Runner 없음)일 때 기록한다.
    // 이후 클라이언트로 다시 들어가면 Runner가 생기므로 기록하지 않는다.
    private bool ShouldRecord()
    {
        if (IsHost())
            return true;

        NetworkManager nm = NetworkManager.Instance;
        bool noRunner = nm == null || nm.Runner == null;
        return _writer != null && noRunner;
    }

    private void WriteRow(string eventName, string[] values)
    {
        if (!EnsureWriter())
            return;

        _row.Clear();
        _row.Append(Time.time.ToString("0.000", CultureInfo.InvariantCulture)).Append(',');
        _row.Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(',');
        _row.Append(Escape(CurrentStage)).Append(',');
        _row.Append(Escape(CurrentMission)).Append(',');
        _row.Append(Escape(eventName));

        foreach (string v in values)
            _row.Append(',').Append(Escape(v));

        try
        {
            _writer.WriteLine(_row.ToString());
        }
        catch (Exception e)
        {
            _fileFailed = true;
            CloseWriter();
            Debug.LogWarning($"ResearchTracker: CSV 기록 실패, 이번 실행에서는 기록을 중단합니다. ({e.Message})");
        }
    }

    private bool EnsureWriter()
    {
        if (_writer != null)
            return true;
        if (_fileFailed)
            return false;

        string fileName = $"Rebloom_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.csv";

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(documents) && TryOpen(Path.Combine(documents, FolderName), fileName))
            return true;

        if (TryOpen(Path.Combine(Application.persistentDataPath, FolderName), fileName))
            return true;

        _fileFailed = true;
        Debug.LogWarning("ResearchTracker: CSV 파일을 만들 수 없어 연구 로그를 저장하지 않습니다.");
        return false;
    }

    private bool TryOpen(string directory, string fileName)
    {
        try
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, fileName);

            // BOM 포함 UTF-8: 엑셀에서 바로 열어도 한글이 깨지지 않는다.
            // AutoFlush: 게임이 강제 종료되어도 그 시점까지의 기록은 남는다.
            _writer = new StreamWriter(path, false, new UTF8Encoding(true)) { AutoFlush = true };
            _writer.WriteLine(string.Join(",", Header));

            FilePath = path;
            Debug.Log($"ResearchTracker: 연구 로그 기록 시작 → {path}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"ResearchTracker: '{directory}'에 CSV를 만들지 못했습니다. ({e.Message})");
            CloseWriter();
            return false;
        }
    }

    private void CloseWriter()
    {
        if (_writer == null)
            return;

        try
        {
            _writer.Dispose();
        }
        catch
        {
            // 종료 중 오류는 무시한다.
        }

        _writer = null;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s))
            return string.Empty;

        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
            return "\"" + s.Replace("\"", "\"\"") + "\"";

        return s;
    }
}
