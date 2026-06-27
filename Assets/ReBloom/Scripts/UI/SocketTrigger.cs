using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRSocketInteractor))]
public class SocketTrigger : MonoBehaviour
{
    [SerializeField] private GeneratorMissionManager generatorMissionManager;

    private const int RequiredSocketCount = 2;
    private static readonly HashSet<SocketTrigger> OccupiedSockets = new();

    private XRSocketInteractor socketInteractor;

    private void Awake()
    {
        socketInteractor = GetComponent<XRSocketInteractor>();
    }

    private void OnEnable()
    {
        socketInteractor.selectEntered.AddListener(OnSocketed);
        socketInteractor.selectExited.AddListener(OnUnsocketed);
    }

    private void OnDisable()
    {
        socketInteractor.selectEntered.RemoveListener(OnSocketed);
        socketInteractor.selectExited.RemoveListener(OnUnsocketed);
        OccupiedSockets.Remove(this);
    }

    private void OnSocketed(SelectEnterEventArgs args)
    {
        OccupiedSockets.Add(this);
        Debug.Log($"[SocketTrigger] Socketed: {name}");
        Debug.Log($"[SocketTrigger] Occupied Count: {OccupiedSockets.Count}");

        if (OccupiedSockets.Count == RequiredSocketCount)
        {
            Debug.Log("All generators repaired!");
            if (generatorMissionManager == null)
            {
                Debug.LogError("[SocketTrigger] generatorMissionManager is NULL");
                return;
            }
            generatorMissionManager.SetGeneratorClear();
        }
    }

    private void OnUnsocketed(SelectExitEventArgs args)
    {
        OccupiedSockets.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] // static 변수 초기화
    private static void ResetState()
    {
        OccupiedSockets.Clear();
    }
}
