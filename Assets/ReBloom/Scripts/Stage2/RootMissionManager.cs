using System.Collections.Generic;
using UnityEngine;

public class RootMissionManager : MonoBehaviour
{
    public RootMissionState currentState = RootMissionState.FindRoots;

    private List<LivingRoot> foundRoots = new List<LivingRoot>();

    public int FoundCount => foundRoots.Count;

    public void OnRootFound(LivingRoot root)
    {
        if (foundRoots.Contains(root))
            return;

        foundRoots.Add(root);

        Debug.Log($"찾은 뿌리 : {FoundCount}/3");

        if (FoundCount >= 3)
        {
            Debug.Log("첫 번째 미션 완료!");
            currentState = RootMissionState.ActivateRoots;
        }
    }
}
