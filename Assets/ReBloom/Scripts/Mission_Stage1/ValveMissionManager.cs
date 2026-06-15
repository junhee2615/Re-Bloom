using System;
using UnityEngine;
using Fusion;

public class ValveMissionManager : NetworkBehaviour
{
    public static event Action MissionCleared;

    public ValveRotate valve;

    [Range(0f, 1f)]
    public float stability;

    [Networked]
    public NetworkBool isMissionClear { get; set; }

    public GameObject gameClearText;

    private bool clearEventRaised;

    void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        // ���� ��� ȸ����
        float angle = valve.CurrentAngle;
       
        if (angle > 180)
            angle -= 360;

        // ���� ȸ���� ���� ������ ����
        float normalized = Mathf.Abs(angle) / 90f;
        stability = Mathf.Clamp01(normalized);

        // UI�� Host/Client ���� ȭ�鿡�� ǥ��
        if (gameClearText != null)
            gameClearText.SetActive(isMissionClear);

        if (isMissionClear && !clearEventRaised)
        {
            clearEventRaised = true;
            MissionCleared?.Invoke();
        }

        // �̼� ���� ������ StateAuthority�� ó��
        if (!HasStateAuthority)
            return;

        if (isMissionClear)
            return;

        if (stability >= 0.9f)
        {
            isMissionClear = true;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        MissionCleared = null;
    }
}
