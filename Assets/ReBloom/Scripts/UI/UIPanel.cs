using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class UIPanel : MonoBehaviour
{
    [SerializeField, Min(0f)] private float duration = 2f;
    [SerializeField] private bool showOnStart = true;
    [SerializeField] private TMP_Text missionText;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private MissionPanelData initialMessage;
    [SerializeField] private MissionPanelData generatorCompleteMessage;
    [SerializeField] private MissionPanelData valveCompleteMessage;

    public bool Active => gameObject.activeSelf;
    
    [SerializeField, Min(1f)] private float spawnDistance = 2f;
    [SerializeField] private float verticalOffset = 0.25f;
    [SerializeField] private float frontPitchOffset = 8f;

    private Action showGeneratorCompleteMessage;
    private Action showValveCompleteMessage;

    private void Awake()
    {
        showGeneratorCompleteMessage = () => ShowCompleteMessage(generatorCompleteMessage);
        showValveCompleteMessage = () => ShowCompleteMessage(valveCompleteMessage);

        SocketTrigger.AllGeneratorsRepaired += showGeneratorCompleteMessage;
        ValveMissionManager.MissionCleared += showValveCompleteMessage;
    }

    private void Start()
    {
        if (showOnStart)
        {
            ShowTemporarily(initialMessage);
        }
        else
        {
            gameObject.SetActive(false);
        }
    }
    
    private void Show()
    {
        Transform cam = Camera.main.transform;

        transform.position =
            cam.position + cam.forward * spawnDistance + cam.up * verticalOffset;

        Vector3 frontDirection = Quaternion.AngleAxis(-frontPitchOffset, cam.right)
                                 * (transform.position - cam.position);

        transform.rotation =
            Quaternion.LookRotation(frontDirection, cam.up);

        gameObject.SetActive(true);
    }
    
    public void ShowTemporarily(MissionPanelData message)
    {
        if (message)
        {
            missionText.text = message.MissionLabel;
            titleText.text = message.Title;
            descriptionText.text = message.Description;
        }

        Show();
        
        StartCoroutine(nameof(HideAfterDelay));
    }
    
    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(duration);
        gameObject.SetActive(false);
    }

    private void ShowCompleteMessage(MissionPanelData message)
    {
        ShowTemporarily(message);
    }

    private void OnDestroy()
    {
        SocketTrigger.AllGeneratorsRepaired -= showGeneratorCompleteMessage;
        ValveMissionManager.MissionCleared -= showValveCompleteMessage;
    }
}
