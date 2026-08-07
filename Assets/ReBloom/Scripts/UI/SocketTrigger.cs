using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[RequireComponent(typeof(XRSocketInteractor))]
public class SocketTrigger : MonoBehaviour
{
    [SerializeField] private GeneratorMissionManager generatorMissionManager;

    [Header("Socket Sound")]
    [SerializeField] private AudioSource socketAudioSource;
    [SerializeField] private AudioClip socketSuccessClip;
    [SerializeField] private AudioClip additionalClip;

    [Header("Generator Sound")]
    [SerializeField] private AudioSource generatorStartAudioSource;
    [SerializeField] private AudioSource generatorLoopAudioSource;

    [SerializeField] private AudioClip generatorStartClip;
    [SerializeField] private AudioClip generatorLoopClip;

    [Tooltip("시동음이 끝나기 몇 초 전에 작동음을 시작할지 설정합니다.")]
    [SerializeField] private float crossfadeTime = 0.3f;

    private const int RequiredSocketCount = 2;
    private static readonly HashSet<SocketTrigger> OccupiedSockets = new();

    private XRSocketInteractor socketInteractor;
    private Coroutine generatorSoundCoroutine;

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

        if (generatorSoundCoroutine != null)
        {
            StopCoroutine(generatorSoundCoroutine);
            generatorSoundCoroutine = null;
        }

        if (generatorStartAudioSource != null)
        {
            generatorStartAudioSource.Stop();
        }

        if (generatorLoopAudioSource != null)
        {
            generatorLoopAudioSource.Stop();
            generatorLoopAudioSource.loop = false;
        }
    }

    private void OnSocketed(SelectEnterEventArgs args)
    {
        OccupiedSockets.Add(this);

        Debug.Log($"[SocketTrigger] Socketed: {name}");
        Debug.Log($"[SocketTrigger] Occupied Count: {OccupiedSockets.Count}");

        // 소켓 삽입 효과음
        if (socketAudioSource != null)
        {
            if (socketSuccessClip != null)
            {
                socketAudioSource.PlayOneShot(socketSuccessClip);
            }

            if (additionalClip != null)
            {
                socketAudioSource.PlayOneShot(additionalClip);
            }
        }

        // 해당 발전기 시동 시작
        if (generatorSoundCoroutine == null)
        {
            generatorSoundCoroutine = StartCoroutine(PlayGeneratorSound());
        }

        // 두 발전기가 모두 수리되었는지 확인
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

    private IEnumerator PlayGeneratorSound()
    {
        if (generatorStartAudioSource == null ||
            generatorLoopAudioSource == null)
        {
            yield break;
        }

        // 시동 효과음 재생
        if (generatorStartClip != null)
        {
            generatorStartAudioSource.clip = generatorStartClip;
            generatorStartAudioSource.loop = false;
            generatorStartAudioSource.Play();

            // 시동음이 완전히 끝나기 전에 Loop 시작
            float waitTime = Mathf.Max(
                0f,
                generatorStartClip.length - crossfadeTime
            );

            yield return new WaitForSeconds(waitTime);
        }

        // 작동 Loop 시작
        if (generatorLoopClip != null)
        {
            generatorLoopAudioSource.clip = generatorLoopClip;
            generatorLoopAudioSource.loop = true;
            generatorLoopAudioSource.Play();
        }

        generatorSoundCoroutine = null;
    }

    private void OnUnsocketed(SelectExitEventArgs args)
    {
        OccupiedSockets.Remove(this);
    }

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration
    )]
    private static void ResetState()
    {
        OccupiedSockets.Clear();
    }
}