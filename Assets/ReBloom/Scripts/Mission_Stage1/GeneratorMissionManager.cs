using Fusion;
using System;
using UnityEngine;

public class GeneratorMissionManager : NetworkBehaviour
{
    public static event Action AllGeneratorsRepaired;

    [Networked]
    public NetworkBool IsGeneratorClear { get; set; }

    private bool clearEventRaised;

    private void Update()
    {
        if (IsGeneratorClear && !clearEventRaised)
        {
            clearEventRaised = true;
            AllGeneratorsRepaired?.Invoke();
        }
    }

    public void SetGeneratorClear()
    {
        if (!HasStateAuthority) return;

        IsGeneratorClear = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        AllGeneratorsRepaired = null;
    }
}