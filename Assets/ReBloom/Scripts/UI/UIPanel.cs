using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR;
using Fusion;

public class UIPanel : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;

    [Header("Texts")]
    [SerializeField] private TMP_Text missionText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

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
        if (message == null)
            return;

        missionText.text = message.MissionLabel;
        titleText.text = message.Title;

        // HostDescription / ClientDescription 은 각각 mental / ear 용 설명이다.
        if (RoleManager.LocalIsMental)
        {
            descriptionText.text = message.HostDescription;
        }
        else
        {
            descriptionText.text = message.ClientDescription;
        }

        if (audioSource != null && tutorialOpenClip != null)
        {
            audioSource.PlayOneShot(tutorialOpenClip);
        }

        // 새 미션이 시작되면 패널 다시 표시
        panelRoot.SetActive(true);
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
    }
}