using Fusion;
using System;
using System.Collections;
using UnityEngine;

public class LeverMissionManager : NetworkBehaviour
{
    public static event Action MissionCleared;

    [Header("Lever")]
    public LeverSwitch leverA;
    public LeverSwitch leverB;

    [Header("Train Door")]
    public Transform doorRight;
    public Transform doorLeft;

    public AudioSource audioSource;
    public AudioClip doorOpenClip;

    [Header("Stage Clear")]
    public GameObject beforebuilding;
    public GameObject clearBuilding;
    public GameObject trainLight;

    [SerializeField]
    private Stage1ClearCutscene stage1ClearCutscene;

    [Networked]
    public NetworkBool IsMissionClear { get; set; }

    // 컷씬 종료 후 실제 맵 복구 상태
    [Networked]
    public NetworkBool IsRestoreApplied { get; set; }

    [Networked]
    public NetworkBool IsDoorOpen { get; set; }

    private bool started = false;
    private bool playedDoorAnimation = false;
    private bool subscribedToCutscene = false;

    [Header("Mission Sound")]
    public AudioSource missionAudioSourceA;
    public AudioSource missionAudioSourceB;

    public AudioSource electricLoopAudioSourceA;
    public AudioSource electricLoopAudioSourceB;

    public AudioClip missionSuccessClip;
    public AudioClip electricLoopClip;

    private bool playedMissionSuccessSound = false;

    public override void Spawned()
    {
        SubscribeCutsceneEvent();

        ApplyRestoreVisualState();
    }

    private void Start()
    {
        // Scene NetworkObject의 Spawned 시점 차이를 대비
        SubscribeCutsceneEvent();
    }

    private void OnDestroy()
    {
        UnsubscribeCutsceneEvent();
    }

    private void SubscribeCutsceneEvent()
    {
        if (subscribedToCutscene)
            return;

        if (stage1ClearCutscene == null)
            return;

        stage1ClearCutscene.CutsceneFinished +=
            OnStage1ClearCutsceneFinished;

        subscribedToCutscene = true;
    }

    private void UnsubscribeCutsceneEvent()
    {
        if (!subscribedToCutscene)
            return;

        if (stage1ClearCutscene != null)
        {
            stage1ClearCutscene.CutsceneFinished -=
                OnStage1ClearCutsceneFinished;
        }

        subscribedToCutscene = false;
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        // 실제 도시 전환은
        // IsMissionClear가 아니라
        // 컷씬 종료 후 IsRestoreApplied를 기준으로 처리
        ApplyRestoreVisualState();

        if (IsMissionClear && !started)
        {
            started = true;

            MissionCleared?.Invoke();

            PlayMissionClearSounds();

            // 컷씬 시작은 Host만 요청
            if (HasStateAuthority)
            {
                if (stage1ClearCutscene != null)
                {
                    stage1ClearCutscene.BeginCutscene();
                }
                else
                {
                    // 컷씬 참조가 없을 경우 게임 진행이 막히지 않도록
                    Debug.LogWarning(
                        "[LeverMissionManager] Stage1ClearCutscene이 연결되지 않았습니다. " +
                        "컷씬 없이 복구 상태를 적용합니다.",
                        this);

                    ApplyRestoreAfterCutscene();
                }
            }
        }

        if (!HasStateAuthority)
            return;

        // 두 레버가 모두 활성화되면
        // Stage 1 최종 미션 성공
        if (!IsMissionClear &&
            leverA != null &&
            leverB != null &&
            leverA.isActivated &&
            leverB.isActivated)
        {
            IsMissionClear = true;
        }

        if (IsDoorOpen &&
            !playedDoorAnimation)
        {
            playedDoorAnimation = true;

            StartCoroutine(OpenDoorAnimation());
        }
    }

    private void ApplyRestoreVisualState()
    {
        if (beforebuilding != null)
        {
            beforebuilding.SetActive(
                !IsRestoreApplied);
        }

        if (clearBuilding != null)
        {
            clearBuilding.SetActive(
                IsRestoreApplied);
        }

        if (trainLight != null)
        {
            trainLight.SetActive(
                IsRestoreApplied);
        }
    }

    private void PlayMissionClearSounds()
    {
        if (playedMissionSuccessSound)
            return;

        playedMissionSuccessSound = true;

        // 두 배전반에서 성공 효과음 재생
        if (missionAudioSourceA != null &&
            missionSuccessClip != null)
        {
            missionAudioSourceA.PlayOneShot(
                missionSuccessClip);
        }

        if (missionAudioSourceB != null &&
            missionSuccessClip != null)
        {
            missionAudioSourceB.PlayOneShot(
                missionSuccessClip);
        }

        // 두 배전반 전기 작동음 Loop 시작
        if (electricLoopAudioSourceA != null &&
            electricLoopClip != null)
        {
            electricLoopAudioSourceA.clip =
                electricLoopClip;

            electricLoopAudioSourceA.loop = true;
            electricLoopAudioSourceA.Play();
        }

        if (electricLoopAudioSourceB != null &&
            electricLoopClip != null)
        {
            electricLoopAudioSourceB.clip =
                electricLoopClip;

            electricLoopAudioSourceB.loop = true;
            electricLoopAudioSourceB.Play();
        }
    }

    // Stage1ClearCutscene의
    // 모든 플레이어 재생이 끝난 뒤 Host에서 호출됨
    private void OnStage1ClearCutsceneFinished()
    {
        if (!HasStateAuthority)
            return;

        ApplyRestoreAfterCutscene();
    }

    private void ApplyRestoreAfterCutscene()
    {
        if (!HasStateAuthority)
            return;

        if (IsRestoreApplied)
            return;

        // 이제 실제 Stage1 월드를 복구 상태로 변경
        IsRestoreApplied = true;

        // 복구된 도시를 잠깐 본 뒤 열차 문 개방
        StartCoroutine(OpenDoorAfterDelay());
    }

    private IEnumerator OpenDoorAfterDelay()
    {
        yield return new WaitForSeconds(3f);

        if (!HasStateAuthority)
            yield break;

        IsDoorOpen = true;
    }

    private IEnumerator OpenDoorAnimation()
    {
        if (audioSource != null &&
            doorOpenClip != null)
        {
            audioSource.PlayOneShot(
                doorOpenClip);
        }

        if (doorRight == null ||
            doorLeft == null)
        {
            yield break;
        }

        Vector3 rightStart =
            doorRight.localPosition;

        Vector3 leftStart =
            doorLeft.localPosition;

        Vector3 rightTarget =
            rightStart + Vector3.right * 0.5f;

        Vector3 leftTarget =
            leftStart + Vector3.left * 0.5f;

        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime;

            doorRight.localPosition =
                Vector3.Lerp(
                    rightStart,
                    rightTarget,
                    t);

            doorLeft.localPosition =
                Vector3.Lerp(
                    leftStart,
                    leftTarget,
                    t);

            yield return null;
        }

        doorRight.localPosition =
            rightTarget;

        doorLeft.localPosition =
            leftTarget;
    }

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        MissionCleared = null;
    }
}