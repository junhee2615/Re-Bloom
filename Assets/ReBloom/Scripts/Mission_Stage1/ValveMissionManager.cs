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

    // public GameObject gameClearText;

    private bool clearEventRaised;

    [Header("Valve Clear Sound")]
    [SerializeField] private AudioSource valveCompleteAudioSource;
    [SerializeField] private AudioSource waterLoopAudioSource;

    [SerializeField] private AudioClip valveCompleteClip;
    [SerializeField] private AudioClip waterLoopClip;

    void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        float angle = valve.CurrentAngle;
       
        if (angle > 180)
            angle -= 360;

        float normalized = Mathf.Abs(angle) / 90f;
        stability = Mathf.Clamp01(normalized);

        //if (gameClearText != null)
        //    gameClearText.SetActive(isMissionClear);

        if (isMissionClear && !clearEventRaised)
        {
            clearEventRaised = true;

            // 완료 효과음 1회
            if (valveCompleteAudioSource != null &&
                valveCompleteClip != null)
            {
                valveCompleteAudioSource.PlayOneShot(valveCompleteClip);
            }

            // 물 흐르는 소리 Loop 시작
            if (waterLoopAudioSource != null &&
                waterLoopClip != null)
            {
                waterLoopAudioSource.clip = waterLoopClip;
                waterLoopAudioSource.loop = true;
                waterLoopAudioSource.PlayDelayed(0.2f);
            }

            MissionCleared?.Invoke();
        }

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
