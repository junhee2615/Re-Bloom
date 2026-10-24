using UnityEngine;

/// <summary>
/// 공명 관련 수치를 연구 로그로 남긴다. 기존 공명 스크립트는 수정하지 않고 공개 상태만 읽는다.
/// ResearchTracker가 같은 오브젝트에 붙여 생성하며, 호스트에서만 동작한다.
///
/// 용어 (코드의 "Released" = 공명 ON)
///  - 공명 ON  : NetworkPlayer.HasCooperativeActivationSucceeded == true
///  - 공명 시도 : 손을 맞대고 둘 다 트리거를 눌러 충전 게이지가 차기 시작한 순간 (CooperativeHoldProgress 0 → 0 초과)
///  - 공명 해제 : 공명 거리를 벗어난 채 유예 시간(5초)을 넘겨 제약이 다시 걸린 순간 (공명 ON → OFF)
///
/// 기록되는 이벤트
///  - ResonanceAttempt        | 스테이지 내 시도 번호 | 캐릭터 간 거리
///  - ResonanceAttemptResult  | Success/Fail/Interrupted | 충전 시간(초)
///  - ResonanceOn             | 캐릭터 간 거리 | 스테이지 내 시도 번호
///  - ResonanceOff            | 캐릭터 간 거리 | 게임 전체 제약 재적용 누적 횟수
///  - GraceStart              | 캐릭터 간 거리                      (공명 거리 이탈, 유예 시작)
///  - GraceReturn             | 이탈 시간(초) | 게임 전체 5초 안 복귀 누적 횟수
///  - ResonanceReset          | (공명 ON 상태로 씬을 나가 유예 없이 초기화됨)
///  - ResonanceTime           | 공명(초) | 완화(초) | 완전 제약(초) | 공명 비율 | 완화 비율 | 완전 제약 비율   ← 스테이지-미션 단위
///  - StageResonanceSummary   | 시도 횟수 | 성공 | 실패                     ← 스테이지 단위
///  - GameResonanceSummary    | 5초 안 복귀 횟수 | 제약 재적용 횟수            ← 게임 종료 시, 게임 전체
/// </summary>
public sealed class ResearchResonanceTracker : MonoBehaviour
{
    private const float ContextSearchInterval = 0.5f;

    // 게임 전체 누적
    private int _graceReturns;
    private int _graceExpires;

    // 스테이지 단위
    private bool _stageHadContext;
    private int _stageAttempts;
    private int _stageSuccesses;
    private int _stageFails;

    // 스테이지-미션 단위 누적 시간
    private float _resonanceTime;       // 공명 발동
    private float _reliefTime;          // 공명 OFF + 공명 거리 안 (완화)
    private float _fullConstraintTime;  // 공명 OFF + 공명 거리 밖 (완전 제약)

    // 관찰 상태
    private bool _observing;
    private bool _prevOn;
    private bool _charging;
    private float _chargeStart;
    private bool _inGrace;
    private float _graceStart;
    private float _lastDistance = -1f;

    private ResonanceSceneContext _context;
    private float _nextContextSearch;

    private void OnEnable()
    {
        ResearchTracker.MissionChanging += FlushMissionTime;
        ResearchTracker.SceneExiting += OnSceneExiting;
    }

    private void OnDisable()
    {
        ResearchTracker.MissionChanging -= FlushMissionTime;
        ResearchTracker.SceneExiting -= OnSceneExiting;
    }

    private void Update()
    {
        if (!ResearchTracker.IsHost() || !TryGetContext())
        {
            _observing = false;
            return;
        }

        ResolvePlayers(out NetworkPlayer local, out NetworkPlayer remote);
        if (local == null)
        {
            _observing = false;
            return;
        }

        _stageHadContext = true;

        bool on = local.HasCooperativeActivationSucceeded;
        bool hasDistance = TryGetPlayerDistance(local, remote, out float distance);
        if (hasDistance)
            _lastDistance = distance;

        float now = Time.time;

        // 관찰을 막 시작한 프레임은 기준 상태만 잡는다(스테이지 시작부터 공명 ON인 경우 등).
        if (!_observing)
        {
            _observing = true;
            _prevOn = on;
            _charging = false;
            _inGrace = false;
            return;
        }

        TrackAttempt(local, on, now);
        TrackOnOff(on, now);
        TrackGrace(on, now);

        // 두 플레이어가 모두 있을 때만 공명/제약 시간을 센다.
        if (hasDistance)
        {
            if (on) _resonanceTime += Time.deltaTime;
            else if (IsRelieved(distance)) _reliefTime += Time.deltaTime;
            else _fullConstraintTime += Time.deltaTime;
        }

        _prevOn = on;
    }

    // ── 공명 시도 (충전 시작 기준) ─────────────────────────────────────────

    private void TrackAttempt(NetworkPlayer local, bool on, float now)
    {
        float hold = local.CooperativeHoldProgress;

        if (!_charging)
        {
            if (!on && hold > 0f)
            {
                _charging = true;
                _chargeStart = now;
                _stageAttempts++;
                ResearchLog.Event("ResonanceAttempt", _stageAttempts, Distance());
            }
            return;
        }

        if (on)
        {
            _charging = false;
            _stageSuccesses++;
            ResearchLog.Event("ResonanceAttemptResult", "Success", ResearchTracker.FormatSeconds(now - _chargeStart));
        }
        else if (hold <= 0f)
        {
            _charging = false;
            _stageFails++;
            ResearchLog.Event("ResonanceAttemptResult", "Fail", ResearchTracker.FormatSeconds(now - _chargeStart));
        }
    }

    // ── 공명 발동 / 해제 ──────────────────────────────────────────────────

    private void TrackOnOff(bool on, float now)
    {
        if (on == _prevOn)
            return;

        if (on)
        {
            ResearchLog.Event("ResonanceOn", Distance(), _stageAttempts);
            return;
        }

        // 공명 OFF는 유예 시간을 넘겨 제약이 재적용될 때만 일어난다(ResonanceController.ReengageConstraint).
        _inGrace = false;
        _graceExpires++;
        ResearchLog.Event("ResonanceOff", Distance(), _graceExpires);
    }

    // ── 5초 유예 ─────────────────────────────────────────────────────────

    private void TrackGrace(bool on, float now)
    {
        ResonanceController controller = ResonanceController.Instance;
        float progress = controller != null ? controller.ReengageProgress : 0f;

        if (!on)
        {
            _inGrace = false;
            return;
        }

        if (!_inGrace && progress > 0f)
        {
            _inGrace = true;
            _graceStart = now;
            ResearchLog.Event("GraceStart", Distance());
        }
        else if (_inGrace && progress <= 0f)
        {
            // 공명이 유지된 채 타이머가 0으로 돌아왔다 = 유예 시간 안에 복귀.
            _inGrace = false;
            _graceReturns++;
            ResearchLog.Event("GraceReturn", ResearchTracker.FormatSeconds(now - _graceStart), _graceReturns);
        }
    }

    // ── 집계 출력 ────────────────────────────────────────────────────────

    // 미션이 바뀌기 직전(아직 이전 미션 이름으로 기록됨)에 공명/제약 시간을 남긴다.
    private void FlushMissionTime()
    {
        float total = _resonanceTime + _reliefTime + _fullConstraintTime;
        if (total > 0f)
        {
            ResearchLog.Event("ResonanceTime",
                ResearchTracker.FormatSeconds(_resonanceTime),
                ResearchTracker.FormatSeconds(_reliefTime),
                ResearchTracker.FormatSeconds(_fullConstraintTime),
                Ratio(_resonanceTime, total),
                Ratio(_reliefTime, total),
                Ratio(_fullConstraintTime, total));
        }

        _resonanceTime = 0f;
        _reliefTime = 0f;
        _fullConstraintTime = 0f;
    }

    private static string Ratio(float part, float total)
    {
        return (part / total).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);
    }

    // 공명 OFF에서 공명 거리보다 가까우면 완화 구간(거리에 따라 제약이 약해짐), 아니면 완전 제약.
    private static bool IsRelieved(float distance)
    {
        ResonanceController controller = ResonanceController.Instance;
        return controller != null && distance < controller.ResonanceDistance;
    }


    private void OnSceneExiting(bool applicationQuitting)
    {
        if (_charging)
        {
            _charging = false;
            ResearchLog.Event("ResonanceAttemptResult", "Interrupted", ResearchTracker.FormatSeconds(Time.time - _chargeStart));
        }

        if (_observing && _prevOn)
            ResearchLog.Event("ResonanceReset");

        FlushMissionTime();

        if (_stageHadContext)
            ResearchLog.Event("StageResonanceSummary", _stageAttempts, _stageSuccesses, _stageFails);

        if (applicationQuitting)
            ResearchLog.Event("GameResonanceSummary", _graceReturns, _graceExpires);

        _stageHadContext = false;
        _stageAttempts = 0;
        _stageSuccesses = 0;
        _stageFails = 0;
        _observing = false;
        _inGrace = false;
        _lastDistance = -1f;
        _context = null;
        _nextContextSearch = 0f;
    }

    // ── 보조 ────────────────────────────────────────────────────────────

    private string Distance()
    {
        return _lastDistance >= 0f ? ResearchTracker.FormatSeconds(_lastDistance) : string.Empty;
    }

    // 켜져 있는 ResonanceSceneContext가 있는 스테이지에서만 측정한다(Lobby, Opening 제외).
    private bool TryGetContext()
    {
        if (_context != null && _context.isActiveAndEnabled)
            return true;

        if (Time.unscaledTime < _nextContextSearch)
            return false;

        _nextContextSearch = Time.unscaledTime + ContextSearchInterval;
        _context = FindFirstObjectByType<ResonanceSceneContext>();
        return _context != null && _context.isActiveAndEnabled;
    }

    private static void ResolvePlayers(out NetworkPlayer localPlayer, out NetworkPlayer remotePlayer)
    {
        localPlayer = null;
        remotePlayer = null;

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player == null || player.Object == null || !player.Object.IsValid)
                continue;

            if (player.IsLocalNetworkRig)
                localPlayer = player;
            else
                remotePlayer ??= player;
        }
    }

    // ResonanceController와 같은 방식으로 거리를 잰다(로컬은 실제 XR 리그 위치 우선).
    private static bool TryGetPlayerDistance(NetworkPlayer localPlayer, NetworkPlayer remotePlayer, out float distance)
    {
        distance = 0f;

        if (localPlayer == null || remotePlayer == null || remotePlayer.PlayerTransform == null)
            return false;

        Transform localTransform = localPlayer.HardwareRig != null ? localPlayer.HardwareRig.playerTransform : null;
        if (localTransform == null)
            localTransform = localPlayer.PlayerTransform;
        if (localTransform == null)
            return false;

        distance = Vector3.Distance(localTransform.position, remotePlayer.PlayerTransform.position);
        return true;
    }
}
