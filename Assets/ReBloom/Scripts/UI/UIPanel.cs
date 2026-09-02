using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.XR;
using Fusion;

public class UIPanel : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private float fadeDuration = 0.3f;

    [Header("Texts")]
    [SerializeField] private TMP_Text missionText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

    [Header("Mission Data")]
    [SerializeField] private MissionPanelData initialMessage;
    [SerializeField] private MissionPanelData generatorCompleteMessage;
    [SerializeField] private MissionPanelData valveCompleteMessage;
    [Header("Mission Data - Stage2")]
    [SerializeField] private MissionPanelData stage2InitialMessage;
    [SerializeField] private MissionPanelData stage2WaterCompleteMessage;
    [SerializeField] private MissionPanelData stage2PlantCompleteMessage;
    [SerializeField] private MissionPanelData stage2StumpCompleteMessage;


    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip tutorialOpenClip;

    private bool previousXButton;

    private Coroutine firstTutorialCoroutine;
    private Coroutine fadeCoroutine;
    private bool isFirstTutorial = true;
    private bool isPanelVisible;
    private CanvasGroup panelCanvasGroup;

    private void Awake()
    {
        panelCanvasGroup = panelRoot.GetComponent<CanvasGroup>();
        if (panelCanvasGroup == null)
            panelCanvasGroup = panelRoot.AddComponent<CanvasGroup>();

        panelCanvasGroup.alpha = 0f;
        panelRoot.SetActive(false);
        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
        TutorialMissionManager_2.TutorialChanged += OnTutorialChanged_2;

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
                SetPanelVisible(!isPanelVisible);
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

    // Stage2 튜토리얼. 첫 미션 지연(5초)은 TutorialMissionManager_2가 처리하므로
    // 여기서는 받은 단계를 그대로 표시한다.
    private void OnTutorialChanged_2(TutorialStep_2 step)
    {
        switch (step)
        {
            case TutorialStep_2.Initial:
                ShowTutorial(stage2InitialMessage);
                break;

            case TutorialStep_2.WaterComplete:
                ShowTutorial(stage2WaterCompleteMessage);
                break;

            case TutorialStep_2.PlantComplete:
                ShowTutorial(stage2PlantCompleteMessage);
                break;

            case TutorialStep_2.StumpComplete:
                ShowTutorial(stage2StumpCompleteMessage);
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
        SetPanelVisible(true);
    }

    private void SetPanelVisible(bool visible)
    {
        isPanelVisible = visible;

        if (fadeCoroutine != null)
            StopCoroutine(fadeCoroutine);

        fadeCoroutine = StartCoroutine(FadePanel(visible));
    }

    private IEnumerator FadePanel(bool visible)
    {
        if (visible)
            panelRoot.SetActive(true);

        float startAlpha = panelCanvasGroup.alpha;
        float targetAlpha = visible ? 1f : 0f;
        float duration = Mathf.Max(fadeDuration, 0.001f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            panelCanvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, elapsed / duration);
            yield return null;
        }

        panelCanvasGroup.alpha = targetAlpha;

        if (!visible)
            panelRoot.SetActive(false);

        fadeCoroutine = null;
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
        TutorialMissionManager_2.TutorialChanged -= OnTutorialChanged_2;

    }
}