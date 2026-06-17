using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class UIPanel : MonoBehaviour
{
    [SerializeField, Min(0f)] private float duration = 5f;

    [SerializeField] private GameObject panelRoot;

    [SerializeField] private TMP_Text missionText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;

    [SerializeField] private MissionPanelData initialMessage;
    [SerializeField] private MissionPanelData generatorCompleteMessage;
    [SerializeField] private MissionPanelData valveCompleteMessage;

    [SerializeField, Min(1f)] private float spawnDistance = 10f;
    [SerializeField] private float fixedYPosition = 2.36f;


    private Coroutine hideCoroutine;

    private void Awake()
    {
        TutorialMissionManager.TutorialChanged += OnTutorialChanged;
    }

    private void OnTutorialChanged(TutorialStep step)
    {
        switch (step)
        {
            case TutorialStep.Initial:
                ShowTemporarily(initialMessage);
                break;

            case TutorialStep.GeneratorComplete:
                ShowTemporarily(generatorCompleteMessage);
                break;

            case TutorialStep.ValveComplete:
                ShowTemporarily(valveCompleteMessage);
                break;
        }
    }

    public void ShowTemporarily(MissionPanelData message)
    {
        if (message != null)
        {
            missionText.text = message.MissionLabel;
            titleText.text = message.Title;
            descriptionText.text = message.Description;
        }

        MovePanelInFrontOfCamera();

        panelRoot.SetActive(true);

        if (hideCoroutine != null)
            StopCoroutine(hideCoroutine);

        hideCoroutine = StartCoroutine(HideAfterDelay());
    }

    private void MovePanelInFrontOfCamera()
    {
        Camera cam = Camera.main;

        if (cam == null)
        {
            Debug.LogError("[UIPanel] Main Camera를 찾을 수 없습니다.");
            return;
        }

        Vector3 newPosition = cam.transform.position + cam.transform.forward * spawnDistance;

        // 높이는 원래 캔버스 높이로 고정
        newPosition.y = fixedYPosition;
        // 위치만 이동
        panelRoot.transform.position = newPosition;
    }


    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(duration);
        panelRoot.SetActive(false);
    }

    private void OnDestroy()
    {
        TutorialMissionManager.TutorialChanged -= OnTutorialChanged;
    }
}
