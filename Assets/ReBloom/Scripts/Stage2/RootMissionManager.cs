using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RootMissionManager : MonoBehaviour
{
    public RootMissionState currentState = RootMissionState.FindRoots;

    [Header("활성화 미션 순서")]
    [SerializeField] private List<RootActivation> activationRoots = new List<RootActivation>();

    private List<LivingRoot> foundRoots = new List<LivingRoot>();

    // 현재 활성화 미션을 진행 중인 뿌리의 인덱스
    private int currentActivationIndex = -1;

    [Header("전환 딜레이")]
    [Tooltip("마지막 뿌리를 찾은 뒤 활성화 미션으로 넘어가기까지 대기(초)")]
    [SerializeField] private float activationDelay = 3f;
    private bool activationStarted;
    // 네트워크: 발견 완료를 이미 Host에 알렸는지 (중복 RPC 방지)
    private bool findNotified;
    /// <summary>찾기 미션(MISSION 3) 완료. 세 뿌리를 모두 찾은 순간 모든 머신에서 1회.</summary>
    public static event System.Action RootsFound;

    /// <summary>활성화 미션(MISSION 4) 전체 완료. 모든 머신에서 1회.</summary>
    public static event System.Action AllRootsActivated;

    // 오프라인/네트워크 두 경로 모두 Completed로 수렴하므로 중복 발행을 막는 래치
    private bool allActivatedNotified;



    // 네트워크 브리지 (연결 시에만 존재). null이면 오프라인 단독 흐름.
    private RootMissionNet net;

    /// <summary>RootMissionNet이 Spawned에서 호출. 뿌리들에 인덱스를 부여한다.</summary>
    public void AttachNet(RootMissionNet n)
    {
        net = n;
        for (int i = 0; i < activationRoots.Count; i++)
            if (activationRoots[i] != null)
                activationRoots[i].SetContext(this, i);
    }

    /// <summary>네트워크: ActiveIndex가 바뀔 때 모든 머신에서 호출되어 시퀀스를 갱신.</summary>
    public void NetApplyIndex(int prev, int cur)
    {
        currentState = (cur >= activationRoots.Count)
            ? RootMissionState.Completed
            : RootMissionState.ActivateRoots;

        // 직전 뿌리 완료 연출
        if (prev >= 0 && prev < activationRoots.Count && activationRoots[prev] != null)
            activationRoots[prev].NetComplete();

        // 현재 뿌리 활성화 안내
        if (cur >= 0 && cur < activationRoots.Count && activationRoots[cur] != null)
            activationRoots[cur].BeginActivation();

        if (cur >= activationRoots.Count)
            Debug.Log("모든 뿌리 활성화 완료! 미션 종료 (네트워크)");
        if (cur >= activationRoots.Count)
            NotifyAllActivated();

    }

    /// <summary>네트워크: RPC_StartAll이 모든 머신에서 해당 뿌리 미션을 시작시킬 때.</summary>
    public void NetStartMission(int index)
    {
        if (index < 0 || index >= activationRoots.Count) return;
        if (activationRoots[index] != null)
            activationRoots[index].NetStartLocal();
    }


    public int FoundCount => foundRoots.Count;

    // 첫 번째 미션 : 뿌리 찾기
public void OnRootFound(LivingRoot root)
    {
        if (foundRoots.Contains(root))
            return;

        foundRoots.Add(root);

        Debug.Log($"찾은 뿌리 : {FoundCount}/3");

        if (FoundCount >= 3 && !activationStarted && !findNotified)
        {
            if (net != null)
            {
                // 네트워크: 아무 기기나 먼저 다 찾으면 모두에게 알려 함께 전환 시작
                findNotified = true;
                net.NotifyFindComplete();
            }
            else
            {
                activationStarted = true;
                Debug.Log($"첫 번째 미션 완료! {activationDelay}초 뒤 활성화 미션으로 전환");
                StartCoroutine(DelayedActivation());
            }
        }
    }

// 네트워크: 어느 기기든 발견을 완료하면 모든 기기에서 호출되어 전환을 시작한다.
    public void NetFindComplete()
    {
        if (activationStarted)
            return;
        activationStarted = true;
        Debug.Log($"첫 번째 미션 완료(네트워크)! {activationDelay}초 뒤 활성화 미션으로 전환");
        StartCoroutine(DelayedActivation());
    }


// 마지막 뿌리를 찾은 뒤 잠시 진동을 더 느끼게 두었다가 활성화 미션으로 전환
private IEnumerator DelayedActivation()
    {
        // 대기 동안 LivingRoot는 켜져 있어 마지막 뿌리 진동을 계속 느낌 수 있다.
        // 튜토리얼 진행 알림: 찾기 미션 완료 (TutorialMissionManager_2가 구독)
        // activationStarted 래치 넭분에 오프라인·네트워크 경로 모두에서 정확히 1회만 호출된다.
        RootsFound?.Invoke();

        yield return new WaitForSeconds(activationDelay);

        // 찾기 미션 종료 → LivingRoot(찾기용) 비활성화 (이후 계속 꺼 둔다)
        foreach (LivingRoot found in foundRoots)
        {
            if (found != null)
                found.enabled = false;
        }

        // 네트워크: 상대가 먼저 찾아 전환된 경우, 내가 아직 못 찾은 뿌리의 LivingRoot도 꺼 둔다.
        if (net != null)
        {
            for (int i = 0; i < activationRoots.Count; i++)
            {
                if (activationRoots[i] == null) continue;
                LivingRoot lr = activationRoots[i].GetComponentInChildren<LivingRoot>(true);
                if (lr != null) lr.enabled = false;
            }
        }

        currentState = RootMissionState.ActivateRoots;

        // 네트워크 연결 시: 권한자(Host)가 시퀀스를 시작하고 모든 머신이 ActiveIndex를 따른다.
        if (net != null)
            net.AuthorityBeginSequence();
        else
            StartActivationSequence();
    }


    // 두 번째 미션 : 뿌리 활성화 (순차 진행)
    private void StartActivationSequence()
    {
        Debug.Log("두 번째 미션 시작 : 뿌리 활성화");
        currentActivationIndex = -1;
        BeginNextRoot();
    }

    // 다음 순서의 뿌리 활성화 미션을 시작. 남은 뿌리가 없으면 전체 완료.
    private void BeginNextRoot()
    {
        currentActivationIndex++;

        if (currentActivationIndex >= activationRoots.Count)
        {
            currentState = RootMissionState.Completed;
            Debug.Log("모든 뿌리 활성화 완료! 미션 종료");
            NotifyAllActivated();

            return;
        }

        RootActivation next = activationRoots[currentActivationIndex];

        if (next == null)   // 비어 있는 슬롯은 건너뜀
        {
            BeginNextRoot();
            return;
        }

        next.BeginActivation();
    }

    // 각 뿌리의 활성화 미션이 끝나면 RootActivation이 호출한다.
    public void OnRootActivated(RootActivation root)
    {
        if (currentState != RootMissionState.ActivateRoots)
            return;

        Debug.Log($"{root.name} 활성화 완료 ({currentActivationIndex + 1}/{activationRoots.Count})");

        BeginNextRoot();   // 다음 뿌리로
    }


    // 활성화 미션 전체 완료를 튜토리얼에 알린다. 중복 호출은 무시된다.
    private void NotifyAllActivated()
    {
        if (allActivatedNotified)
            return;

        allActivatedNotified = true;
        AllRootsActivated?.Invoke();
    }

    // 도메인 리로드 비활성화 환경 대비: 재생 세션 시작 전 정적 이벤트 리셋
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        RootsFound = null;
        AllRootsActivated = null;
    }
}
