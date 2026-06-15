using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRSocketInteractor))]
public class SocketTrigger : MonoBehaviour
{
    private const int RequiredSocketCount = 2;
    private static readonly HashSet<SocketTrigger> OccupiedSockets = new();

    public static event Action AllGeneratorsRepaired;

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
        Debug.Log("Occupied Sockets: " + string.Join(", ", OccupiedSockets));
        
        if (OccupiedSockets.Count == RequiredSocketCount)
        {
            Debug.Log("All generators repaired!");
            AllGeneratorsRepaired?.Invoke();
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
        AllGeneratorsRepaired = null;
    }
}
