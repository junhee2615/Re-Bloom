using Fusion;
using System;
using System.Collections;
using System.Linq;
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

    [Header("Realtime VR Cutscene")]
    [SerializeField]
    private Stage1XRCutsceneRigFollower stage1Cutscene;

    [Networked]
    public NetworkBool IsMissionClear { get; set; }

    [Networked]
    public NetworkBool IsRestoreApplied { get; set; }

    [Networked]
    public NetworkBool IsDoorOpen { get; set; }

    [Networked]
    public NetworkBool IsCutscenePlaying { get; set; }

    private bool started;
    private bool playedDoorAnimation;

    // 이 클라이언트가 현재 컷씬 상태인지
    private bool localCutsceneRunning;

    // Host에서 받은 컷씬 종료 보고 수
    private int cutsceneReportsReceived;

    private bool subscribedToCutscene;

    [Header("Mission Sound")]
    public AudioSource missionAudioSourceA;
    public AudioSource missionAudioSourceB;

    public AudioSource electricLoopAudioSourceA;
    public AudioSource electricLoopAudioSourceB;

    public AudioClip missionSuccessClip;
    public AudioClip electricLoopClip;

    private bool playedMissionSuccessSound;

    // =================================================
    // Fusion
    // =================================================

    public override void Spawned()
    {
        SubscribeCutsceneEvent();

        ApplyRestoreVisualState();
    }

    private void Start()
    {
        SubscribeCutsceneEvent();
    }

    // Fusion의 정식 정리 훅.
    // SimulationBehaviour가 OnDestroy를 이미 선언하고 있어서
    // OnDestroy를 직접 선언하면 Fusion의 정리가 호출되지 않는다.
    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        base.Despawned(runner, hasState);

        UnsubscribeCutsceneEvent();
    }

    // =================================================
    // Cutscene Event
    // =================================================

    private void SubscribeCutsceneEvent()
    {
        if (subscribedToCutscene)
            return;

        if (stage1Cutscene == null)
            return;

        stage1Cutscene.CutsceneFinished +=
            OnLocalCutsceneFinished;

        subscribedToCutscene = true;
    }

    private void UnsubscribeCutsceneEvent()
    {
        if (!subscribedToCutscene)
            return;

        if (stage1Cutscene != null)
        {
            stage1Cutscene.CutsceneFinished -=
                OnLocalCutsceneFinished;
        }

        subscribedToCutscene = false;
    }

    // =================================================
    // Update
    // =================================================

    private void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        // Timeline이 건물/열차 불을 직접 제어하는 동안에는
        // LeverMissionManager가 상태를 덮어쓰지 않는다.
        if (!localCutsceneRunning)
        {
            ApplyRestoreVisualState();
        }

        // Host가 복구 상태를 확정하면
        // 각 클라이언트에서도 최종 상태를 적용
        if (localCutsceneRunning &&
            IsRestoreApplied)
        {
            localCutsceneRunning = false;
            ApplyRestoreVisualState();
        }

        // 미션 성공 최초 1회
        if (IsMissionClear && !started)
        {
            started = true;

            MissionCleared?.Invoke();

            PlayMissionClearSounds();

            if (HasStateAuthority)
            {
                cutsceneReportsReceived = 0;

                IsCutscenePlaying = true;

                RPC_StartStage1Cutscene();
            }
        }

        if (!HasStateAuthority)
            return;

        // 두 레버 모두 활성화
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

            StartCoroutine(
                OpenDoorAnimation());
        }
    }

    // =================================================
    // Start Cutscene
    // =================================================

    [Rpc(
        RpcSources.StateAuthority,
        RpcTargets.All)]
    private void RPC_StartStage1Cutscene()
    {
        localCutsceneRunning = true;

        if (stage1Cutscene == null)
        {
            Debug.LogError(
                "[LeverMissionManager] Stage1XRCutsceneRigFollower가 연결되지 않았습니다.",
                this);

            RPC_ReportCutsceneFinished();
            return;
        }

        if (!stage1Cutscene.IsReady)
        {
            Debug.LogWarning(
                "[LeverMissionManager] XR Cutscene Rig가 아직 준비되지 않았습니다.",
                this);

            RPC_ReportCutsceneFinished();
            return;
        }

        stage1Cutscene.PlayCutscene();
    }

    // =================================================
    // Local Cutscene Finished
    // =================================================

    private void OnLocalCutsceneFinished()
    {
        if (!localCutsceneRunning)
            return;

        RPC_ReportCutsceneFinished();
    }

    // =================================================
    // Host receives completion
    // =================================================

    [Rpc(
        RpcSources.All,
        RpcTargets.StateAuthority)]
    private void RPC_ReportCutsceneFinished()
    {
        if (!HasStateAuthority)
            return;

        cutsceneReportsReceived++;

        int expectedPlayers =
            Runner != null
                ? Runner.ActivePlayers.Count()
                : 1;

        if (cutsceneReportsReceived >=
            expectedPlayers)
        {
            FinishStage1Cutscene();
        }
    }

    private void FinishStage1Cutscene()
    {
        if (!HasStateAuthority)
            return;

        if (!IsCutscenePlaying)
            return;

        IsCutscenePlaying = false;

        ApplyRestoreAfterCutscene();
    }

    // =================================================
    // Final Restore
    // =================================================

    private void ApplyRestoreAfterCutscene()
    {
        if (!HasStateAuthority)
            return;

        if (IsRestoreApplied)
            return;

        IsRestoreApplied = true;

        StartCoroutine(
            OpenDoorAfterDelay());
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

    // =================================================
    // Mission Sound
    // =================================================

    private void PlayMissionClearSounds()
    {
        if (playedMissionSuccessSound)
            return;

        playedMissionSuccessSound = true;

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

    // =================================================
    // Train Door
    // =================================================

    private IEnumerator OpenDoorAfterDelay()
    {
        // 현재 코드에 있던 3초 유지
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
            rightStart +
            Vector3.right * 0.5f;

        Vector3 leftTarget =
            leftStart +
            Vector3.left * 0.5f;

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

    // =================================================
    // Static Reset
    // =================================================

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetEvent()
    {
        MissionCleared = null;
    }
}