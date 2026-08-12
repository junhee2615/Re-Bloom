using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using Fusion;

public class UIPanel : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;

    [Header("Tutorial Image")]
    [SerializeField] private Image tutorialImage;

    [Header("Mission Data")]
    [SerializeField] private MissionPanelData initialMessage;
    [SerializeField] private MissionPanelData generatorCompleteMessage;
    [SerializeField] private MissionPanelData valveCompleteMessage;

    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip tutorialOpenClip;

    private bool previousXButton;

    private Coroutine firstTutorialCoroutine;
    private bool isFirstTutorial = true;

    private void Awake()
    {
        panelRoot.SetActive(false);
        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
    }

    private void Update()
    {
        CheckXButtonToggle();
    }

    private void CheckXButtonToggle()
    {
        InputDevice leftHand =
            InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (leftHand.TryGetFeatureValue(
                CommonUsages.primaryButton,
                out bool xButton))
        {
            if (xButton && !previousXButton)
            {
                panelRoot.SetActive(!panelRoot.activeSelf);
            }

            previousXButton = xButton;
        }
    }

    private void OnTutorialChanged(TutorialStep step)
    {
        if (isFirstTutorial && step == TutorialStep.Initial)
        {
            isFirstTutorial = false;

            if (firstTutorialCoroutine != null)
                StopCoroutine(firstTutorialCoroutine);

            firstTutorialCoroutine =
                StartCoroutine(ShowFirstTutorialAfterDelay());

            return;
        }

        switch (step)
        {
            case TutorialStep.Initial:
                ShowTutorial(initialMessage);
                break;

            case TutorialStep.GeneratorComplete:
                ShowTutorial(generatorCompleteMessage);
                break;

            case TutorialStep.ValveComplete:
                ShowTutorial(valveCompleteMessage);
                break;
        }
    }

    private IEnumerator ShowFirstTutorialAfterDelay()
    {
        yield return new WaitForSeconds(5f);
        ShowTutorial(initialMessage);
    }

    private void ShowTutorial(MissionPanelData message)
    {
        if (message == null || tutorialImage == null)
            return;

        NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();

        // Host와 Client에 따라 서로 다른 이미지 표시
        if (runner != null && runner.LocalPlayer.PlayerId == 1)
        {
            tutorialImage.sprite = message.HostImage;
        }
        else
        {
            tutorialImage.sprite = message.ClientImage;
        }

        if (audioSource != null && tutorialOpenClip != null)
        {
            audioSource.PlayOneShot(tutorialOpenClip);
        }

        // 새로운 미션이 시작될 때 패널 다시 표시
        panelRoot.SetActive(true);
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
    }
}