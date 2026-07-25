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

    public int FoundCount => foundRoots.Count;

    // 첫 번째 미션 : 뿌리 찾기
    public void OnRootFound(LivingRoot root)
    {
        if (foundRoots.Contains(root))
            return;

        foundRoots.Add(root);

        Debug.Log($"찾은 뿌리 : {FoundCount}/3");

        if (FoundCount >= 3)
        {
            Debug.Log("첫 번째 미션 완료! (뿌리 찾기 완료)");

            // 찾기 미션 종료 → LivingRoot(찾기용) 비활성화 (이후 계속 꺼 둠)
            foreach (LivingRoot found in foundRoots)
            {
                if (found != null)
                    found.enabled = false;
            }

            // 두 번째 미션(활성화) 시작
            currentState = RootMissionState.ActivateRoots;
            StartActivationSequence();
        }
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
}
