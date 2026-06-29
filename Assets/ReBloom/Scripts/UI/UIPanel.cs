using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR;

public class UIPanel : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TMP_Text missionText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

    [SerializeField] private MissionPanelData initialMessage;
    [SerializeField] private MissionPanelData generatorCompleteMessage;
    [SerializeField] private MissionPanelData valveCompleteMessage;

    private bool previousXButton;

    private void Awake()
    {
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

    public void ShowTutorial(MissionPanelData message)
    {
        if (message != null)
        {
            missionText.text = message.MissionLabel;
            titleText.text = message.Title;
            descriptionText.text = message.Description;
        }
        // 새 튜토리얼이 뜰 때는 이전에 꺼져 있었어도 무조건 다시 켜기
        panelRoot.SetActive(true);
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
    }
}
