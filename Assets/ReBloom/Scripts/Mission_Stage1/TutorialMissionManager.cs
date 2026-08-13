using Fusion;
using System;
using UnityEngine;

public class TutorialMissionManager : NetworkBehaviour
{
    public static event Action<TutorialStep> TutorialChanged;

    [Networked]
    public TutorialStep CurrentTutorial { get; set; }

    private TutorialStep lastTutorial = TutorialStep.None;

    private void Awake()
    {
        GeneratorMissionManager.AllGeneratorsRepaired += ShowGeneratorComplete;
        ValveMissionManager.MissionCleared += ShowValveComplete;
        LeverMissionManager.MissionCleared += ShowAllComplete;
    }

    public override void Spawned()
    {
        if (HasStateAuthority)
        {
            CurrentTutorial = TutorialStep.Initial;
        }
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid)
            return;
        if (CurrentTutorial != lastTutorial)
        {
            lastTutorial = CurrentTutorial;

            if (CurrentTutorial != TutorialStep.None)
            {
                TutorialChanged?.Invoke(CurrentTutorial);
            }
        }
    }

    public void ShowGeneratorComplete()
    {
        if (!HasStateAuthority) return;

        CurrentTutorial = TutorialStep.GeneratorComplete;
    }

    public void ShowValveComplete()
    {
        if (!HasStateAuthority) return;

        CurrentTutorial = TutorialStep.ValveComplete;
    }

    public void ShowAllComplete()
    {
        if (!HasStateAuthority) return;

        CurrentTutorial = TutorialStep.AllComplete;
    }

    private void OnDestroy()
    {
        GeneratorMissionManager.AllGeneratorsRepaired -= ShowGeneratorComplete;
        ValveMissionManager.MissionCleared -= ShowValveComplete;
        LeverMissionManager.MissionCleared -= ShowAllComplete;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        TutorialChanged = null;
    }
}
