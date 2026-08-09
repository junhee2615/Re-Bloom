using System.Collections.Generic;
using Fusion;
using UnityEngine;

/// <summary>
/// 뿌리 활성화 시퀀스를 두 플레이어(Host/Client) 사이에서 동기화하는 네트워크 브리지.
/// Host가 StateAuthority로서 활성화 순서(ActiveIndex)를 소유하고, 모든 머신이 이를 따라
/// 같은 뿌리를 켜고/끄고 진행한다. 미션 시작·클리어·페이즈 판정 결과는 RPC로 중계한다.
///
/// - 시퀀스 상태(ActiveIndex): [Networked] 프로퍼티로 지속 동기화 (권한자만 변경).
/// - 시작/클리어/판정결과: 일시적 이벤트라 RPC로 처리.
///
/// RootMissionManager와 같은 GameObject(NetworkObject)에 붙인다.
/// 네트워크 미연결(단독) 상태에서는 이 컴포넌트가 없거나 Instance가 null이므로
/// 기존 로컬 흐름이 그대로 동작한다.
/// </summary>
public class RootMissionNet : NetworkBehaviour
{
    public static RootMissionNet Instance { get; private set; }

    [Tooltip("같은 오브젝트의 RootMissionManager (비우면 자동 검색)")]
    [SerializeField] private RootMissionManager manager;

    /// <summary>현재 활성화 중인 뿌리 인덱스. -1 = 아직 활성화 단계 전, count 이상 = 전체 완료.</summary>
    [Networked] public int ActiveIndex { get; set; }

    // 마지막으로 로컬에 반영한 인덱스 (변화 감지용)
    private int appliedIndex = -999;

    // 페이즈 판정 결과 신호: key -> (1=성공, 2=실패)
    private readonly Dictionary<int, int> results = new Dictionary<int, int>();

    private bool Ready => Object != null && Object.IsValid;

    public override void Spawned()
    {
        Instance = this;
        if (manager == null) manager = GetComponent<RootMissionManager>();
        if (manager != null) manager.AttachNet(this);

        // 권한자는 활성화 단계 전 상태(-1)로 초기화. 클라이언트는 복제된 값을 받는다.
        if (HasStateAuthority) ActiveIndex = -1;

        appliedIndex = -999;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (Instance == this) Instance = null;
    }

    public override void Render()
    {
        if (!Ready) return;
        if (ActiveIndex != appliedIndex)
        {
            int prev = appliedIndex;
            appliedIndex = ActiveIndex;
            if (manager != null) manager.NetApplyIndex(prev, ActiveIndex);
        }
    }

    // ---------------------------------------------------------------- 시퀀스

    /// <summary>권한자만: 활성화 시퀀스를 시작한다(ActiveIndex = 0). 이미 시작했으면 무시.</summary>
    public void AuthorityBeginSequence()
    {
        if (!Ready || !HasStateAuthority) return;
        if (ActiveIndex < 0) ActiveIndex = 0;
    }

// 발견 완료 알림: 아무 기기나 먼저 세 뿌리를 다 찾으면 모두에게 전파해 함께 전환을 시작한다.
    public void NotifyFindComplete()
    {
        if (!Ready) return;
        if (HasStateAuthority) RPC_FindCompleteAll();
        else RPC_FindComplete();
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_FindComplete(RpcInfo info = default)
    {
        RPC_FindCompleteAll();
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_FindCompleteAll()
    {
        if (manager != null) manager.NetFindComplete();
    }


    /// <summary>미션 클리어를 보고한다. 권한자가 다음 인덱스로 전진시킨다.</summary>
    public void ReportCleared(int index)
    {
        if (!Ready) return;
        if (HasStateAuthority) AuthorityAdvance(index);
        else RPC_ReportCleared(index);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ReportCleared(int index, RpcInfo info = default)
    {
        AuthorityAdvance(index);
    }

    private void AuthorityAdvance(int index)
    {
        if (!HasStateAuthority) return;
        if (index != ActiveIndex) return;   // 중복/오래된 보고 무시
        ActiveIndex = ActiveIndex + 1;
    }

    // ---------------------------------------------------------------- 미션 시작

    /// <summary>StartButton 입력 → 모든 머신에서 해당 미션을 시작하게 한다.</summary>
    public void RequestStart(int index)
    {
        if (!Ready) return;
        if (HasStateAuthority) RPC_StartAll(index);
        else RPC_RequestStart(index);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_RequestStart(int index, RpcInfo info = default)
    {
        RPC_StartAll(index);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_StartAll(int index)
    {
        if (manager != null) manager.NetStartMission(index);
    }

    // ---------------------------------------------------------------- 판정 결과 중계

    /// <summary>페이즈 판정 결과를 제출한다(판정자만 호출). 로컬 즉시 반영 후 모두에게 중계.</summary>
    public void SubmitResult(int key, bool success)
    {
        if (!Ready) return;
        SetResult(key, success);
        if (HasStateAuthority) RPC_ResultAll(key, success);
        else RPC_ResultToAuthority(key, success);
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_ResultToAuthority(int key, NetworkBool success, RpcInfo info = default)
    {
        RPC_ResultAll(key, success);
    }

    [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
    private void RPC_ResultAll(int key, NetworkBool success)
    {
        SetResult(key, success);
    }

    private void SetResult(int key, bool success)
    {
        results[key] = success ? 1 : 2;
    }

    /// <summary>중계된 판정 결과가 도착했는지. 도착했으면 success에 담아 true 반환.</summary>
    public bool TryGetResult(int key, out bool success)
    {
        int v;
        if (results.TryGetValue(key, out v))
        {
            success = (v == 1);
            return true;
        }
        success = false;
        return false;
    }

    /// <summary>소비한 판정 결과 신호를 제거한다.</summary>
    public void ClearResult(int key)
    {
        results.Remove(key);
    }
}
