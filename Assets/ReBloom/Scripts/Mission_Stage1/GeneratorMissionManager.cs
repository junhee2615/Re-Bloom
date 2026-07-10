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
        if (Object == null || !Object.IsValid)
            return;

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

    public override void Spawned()
    {
        Debug.Log($"{name} Spawned!");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        AllGeneratorsRepaired = null;
    }
}