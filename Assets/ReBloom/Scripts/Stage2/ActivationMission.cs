using UnityEngine;

// 미션을 수행할 수 있는 플레이어 역할 (Any = 제한 없음)
// 값 순서는 인스펙터 설정을 유지하기 위해 바꾸지 않는다.
public enum MissionRole { Any, MentalOnly, EarOnly }

/// <summary>
/// 모든 "뿌리 활성화 미션"의 공통 베이스.
/// 각 AliveStump 하위 Panel에 붙는 미션 스크립트가 이 클래스를 상속한다.
/// RootActivation은 이 계약(StartMission / OnCleared)만 알고 미션을 구동하므로,
/// 뿌리마다 미션 방식이 완전히 달라도 상위 흐름은 그대로 동작한다.
///
/// 네트워크 모드에서는 미션 코루틴이 '두 머신 모두에서' 돈다.
/// 수행 역할이 아닌 플레이어도 같은 MissionCanvas 연출을 보되, 입력 판정만 하지 않는다.
/// 판정 결과와 입력 피드백은 RootMissionNet을 통해 중계된다(아래 공유 도구 참고).
/// </summary>
public abstract class ActivationMission : MonoBehaviour
{
    /// <summary>미션 클리어 시 RootActivation이 받을 콜백.</summary>
    public System.Action OnCleared;

    [Header("역할 제한")]
    [Tooltip("이 미션을 수행(입력/판정)할 수 있는 플레이어 역할 (Any=제한없음, MentalOnly=mental만, EarOnly=ear만). 화면 연출은 역할과 관계없이 두 플레이어 모두에게 보인다.")]
    [SerializeField] protected MissionRole requiredRole = MissionRole.Any;

    // 네트워크 동기화용 뿌리 인덱스 (RootActivation이 설정). 오프라인이면 0.
    protected int missionIndex;
    /// <summary>이 미션의 뿌리 인덱스를 설정한다(네트워크 시퀀스 식별용).</summary>
    public void SetMissionIndex(int index) { missionIndex = index; }


    /// <summary>
    /// 이 뿌리 차례가 되면 RootActivation이 호출한다.
    /// 패널 UI(안내 텍스트 / 노트 등)를 세팅하고 입력 판정을 시작한다.
    /// </summary>
    public abstract void StartMission();

    /// <summary>
    /// 미션을 중단 / 리셋할 때 호출 (필요 없으면 비워 둬도 됨).
    /// </summary>
    public virtual void StopMission() { }

    /// <summary>
    /// 컨트롤러 접촉을 이 미션에 알릴 때 호출한다 (접촉 감지가 필요한 미션만 override).
    /// </summary>
    public virtual void NotifyHandDetected() { }

    /// <summary>
    /// 로컬 플레이어가 이 미션을 '수행'할 수 있는 역할인지 (입력이 카운트되는지).
    /// 역할이 정해지지 않은 단독 테스트 상태에서는 항상 허용한다.
    /// </summary>
    public bool CanLocalPlayerPlay()
    {
        // 역할이 아직 없다 = Lobby를 거치지 않은 단독 테스트. 제한하지 않는다.
        if (!RoleManager.HasLocalRole) return true;

        switch (requiredRole)
        {
            case MissionRole.MentalOnly: return RoleManager.LocalIsMental;
            case MissionRole.EarOnly:    return RoleManager.LocalIsEar;
            default:                     return true;
        }
    }

    // ------------------------------------------------------------------
    // 진행 상황 공유 도구
    // 두 머신이 같은 코루틴을 돌리되, 판정은 수행 역할만 하고
    // 그 결과와 입력 피드백을 RootMissionNet으로 중계해 화면을 맞춘다.
    // 키는 (뿌리 인덱스 / 페이즈 / 시도 횟수 / 슬롯)으로 양쪽이 동일하게 계산한다.
    // ------------------------------------------------------------------

    /// <summary>네트워크에 연결돼 상대와 진행 상황을 주고받을 수 있는 상태인지.</summary>
    protected static bool NetConnected { get { return RootMissionNet.Instance != null; } }

    /// <summary>이 머신이 판정을 담당하는지. 오프라인이면 항상 true, 온라인이면 수행 역할만 true.</summary>
    protected bool IsJudge { get { return !NetConnected || CanLocalPlayerPlay(); } }

    /// <summary>한 시도(attempt)의 성공/실패 결과 키.</summary>
    protected int ResultKey(int phase, int attempt)
    {
        return missionIndex * 1000000 + phase * 100000 + attempt * 1000;
    }

    /// <summary>한 시도 안의 개별 입력 피드백(노트 터치, 버튼 누름 등) 신호 키. slot은 0~998.</summary>
    protected int SignalKey(int phase, int attempt, int slot)
    {
        return ResultKey(phase, attempt) + 1 + slot;
    }

    /// <summary>판정자가 결과를 제출한다(오프라인이면 아무것도 하지 않는다).</summary>
    protected void SubmitResult(int key, bool success)
    {
        RootMissionNet net = RootMissionNet.Instance;
        if (net != null) net.SubmitResult(key, success);
    }

    /// <summary>중계된 결과가 도착했는지.</summary>
    protected bool TryGetResult(int key, out bool success)
    {
        RootMissionNet net = RootMissionNet.Instance;
        if (net != null) return net.TryGetResult(key, out success);
        success = false;
        return false;
    }

    /// <summary>입력 피드백 신호를 상대에게 보낸다.</summary>
    protected void SubmitSignal(int key)
    {
        RootMissionNet net = RootMissionNet.Instance;
        if (net != null) net.SubmitResult(key, true);
    }

    /// <summary>상대의 입력 피드백 신호가 도착했는지.</summary>
    protected bool HasSignal(int key)
    {
        bool dummy;
        return TryGetResult(key, out dummy);
    }

    /// <summary>한 시도에서 쓴 결과/신호 키를 모두 비운다(재시도 시 이전 신호가 남지 않도록).</summary>
    protected void ClearRoundKeys(int phase, int attempt, int slotCount)
    {
        RootMissionNet net = RootMissionNet.Instance;
        if (net == null) return;

        net.ClearResult(ResultKey(phase, attempt));
        for (int i = 0; i < slotCount; i++)
            net.ClearResult(SignalKey(phase, attempt, i));
    }

    /// <summary>
    /// 각 미션이 성공 시점(클리어 텍스트를 보여준 뒤 등)에 호출한다.
    /// RootActivation에 완료를 알려 다음 단계로 넘어간다.
    /// </summary>
    protected void Clear()
    {
        OnCleared?.Invoke();
    }
}
