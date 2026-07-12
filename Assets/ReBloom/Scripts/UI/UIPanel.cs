using System;
using System.Collections;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR;
using Fusion;

public class UIPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TMP_Text missionText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

    [SerializeField] private MissionPanelData initialMessage;
    [SerializeField] private MissionPanelData generatorCompleteMessage;
    [SerializeField] private MissionPanelData valveCompleteMessage;
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
        InputDevice leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (leftHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool xButton))
        {
            // 버튼을 누르는 순간 한 번만 실행
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

            firstTutorialCoroutine = StartCoroutine(ShowFirstTutorialAfterDelay());
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

    public void ShowTutorial(MissionPanelData message)
    {
        if (message != null)
        {
            // 튜토리얼 메시지 구분
            missionText.text = message.MissionLabel;
            titleText.text = message.Title;

            NetworkRunner runner = FindFirstObjectByType<NetworkRunner>();

            if(runner != null && runner.LocalPlayer.PlayerId == 1)
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

        }
        // 새 튜토리얼이 뜰 때는 이전에 꺼져 있었어도 무조건 다시 켜기
        panelRoot.SetActive(true);
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
    }
}
