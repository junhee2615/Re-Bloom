using Fusion;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Audio;

public class LeverMissionManager : NetworkBehaviour
{
    public LeverSwitch leverA;
    public LeverSwitch leverB;
    public Transform doorRight;
    public Transform doorLeft;
    public AudioSource audioSource;
    public AudioClip doorOpenClip;
    [Networked]
    public NetworkBool IsMissionClear { get; set; } // 미션 클리어 상태 공유
    [Networked]
    public NetworkBool IsDoorOpen { get; set; } // 문열림 상태 공유
    public GameObject beforebuilding;
    public GameObject clearBuilding;
    public GameObject trainLight;
    private bool started = false;
    private bool playedDoorAnimation = false;

    [Header("Mission Sound")]
    public AudioSource missionAudioSourceA;
    public AudioSource missionAudioSourceB;

    public AudioSource electricLoopAudioSourceA;
    public AudioSource electricLoopAudioSourceB;

    public AudioClip missionSuccessClip;
    public AudioClip electricLoopClip;

    private bool playedMissionSuccessSound = false;

    void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        if (beforebuilding != null)
            beforebuilding.SetActive(!IsMissionClear);

        if (clearBuilding != null)
            clearBuilding.SetActive(IsMissionClear);

        if (trainLight != null)
            trainLight.SetActive(IsMissionClear);

        if (IsMissionClear && !started)
        {
            started = true;

            if (!playedMissionSuccessSound)
            {
                playedMissionSuccessSound = true;

                // 두 배전반에서 성공 효과음 재생
                if (missionAudioSourceA != null && missionSuccessClip != null)
                    missionAudioSourceA.PlayOneShot(missionSuccessClip);

                if (missionAudioSourceB != null && missionSuccessClip != null)
                    missionAudioSourceB.PlayOneShot(missionSuccessClip);

                // 두 배전반에서 전기 작동음 Loop 시작
                if (electricLoopAudioSourceA != null && electricLoopClip != null)
                {
                    electricLoopAudioSourceA.clip = electricLoopClip;
                    electricLoopAudioSourceA.loop = true;
                    electricLoopAudioSourceA.Play();
                }

                if (electricLoopAudioSourceB != null && electricLoopClip != null)
                {
                    electricLoopAudioSourceB.clip = electricLoopClip;
                    electricLoopAudioSourceB.loop = true;
                    electricLoopAudioSourceB.Play();
                }
            }

            if (HasStateAuthority)
                StartCoroutine(OpenDoorAfterDelay());
        }

        if (!HasStateAuthority)
            return;

        // 두 레버 활성화 확인
        if (leverA.isActivated && leverB.isActivated)
        {
            IsMissionClear = true;
        }

        if (IsDoorOpen && !playedDoorAnimation)
        {
            playedDoorAnimation = true;
            StartCoroutine(OpenDoorAnimation());
        }
    }

    IEnumerator OpenDoorAfterDelay()
    {
        yield return new WaitForSeconds(3f);

        IsDoorOpen = true;
    }

    IEnumerator OpenDoorAnimation()
    {
        if (audioSource != null)
            audioSource.PlayOneShot(doorOpenClip);

        Vector3 rightStart = doorRight.localPosition;
        Vector3 leftStart = doorLeft.localPosition;

        Vector3 rightTarget = rightStart + Vector3.right * 0.5f;
        Vector3 leftTarget = leftStart + Vector3.left * 0.5f;

        float t = 0;

        while (t < 1)
        {
            t += Time.deltaTime;

            doorRight.localPosition =
                Vector3.Lerp(rightStart, rightTarget, t);

            doorLeft.localPosition =
                Vector3.Lerp(leftStart, leftTarget, t);

            yield return null;
        }
    }
}



