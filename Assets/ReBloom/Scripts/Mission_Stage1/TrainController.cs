using Fusion;
using System.Collections;
using UnityEngine;

public class TrainController : NetworkBehaviour
{
    [SerializeField] private float moveDistance = 20f;
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float acceleration = 2f;
    [Networked]
    private NetworkBool IsTrainMoving { get; set; }
    [Networked]
    private NetworkBool IsTrainArrived { get; set; }
    private bool parentSet = false;
    private ScreenFade screenFade;

    private bool isMoving = false;
    private bool lastTrainArrived = false;

    private void Start()
    {
        screenFade = FindFirstObjectByType<ScreenFade>();
    }

    private void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        HardwareRig rig = FindFirstObjectByType<HardwareRig>();

        if (rig == null)
            return;

        if (IsTrainMoving && !parentSet)
        {
            parentSet = true;
            rig.SetTrainParent(transform);
        }

        if (!IsTrainMoving && parentSet)
        {
            parentSet = false;
            rig.ClearTrainParent();
        }

        if (IsTrainArrived && !lastTrainArrived)
        {
            lastTrainArrived = true;

            if (screenFade != null)
            {
                StartCoroutine(screenFade.FadeOut(1f));
            }
        }
    }

    public void StartTrain()
    {
        if (!HasStateAuthority || isMoving)
            return;
        IsTrainMoving = true;

        StartCoroutine(MoveTrain());
    }

    private IEnumerator MoveTrain()
    {
        isMoving = true;

        Vector3 startPos = transform.position;
        Vector3 targetPos = startPos + Vector3.right * moveDistance;

        float currentSpeed = 0f;

        while (transform.position.x < targetPos.x)
        {
            currentSpeed += acceleration * Runner.DeltaTime;
            currentSpeed = Mathf.Min(currentSpeed, maxSpeed);

            transform.position +=
                Vector3.right * currentSpeed * Runner.DeltaTime;

            yield return null;
        }

        transform.position = targetPos;

        isMoving = false;
        IsTrainMoving = false;
        IsTrainArrived = true;

        if (HasStateAuthority)
        {
            StartCoroutine(LoadStage2());
        }
    }

    private IEnumerator LoadStage2()
    {
        yield return new WaitForSeconds(1f); // FadeOut 시간

        if (HasStateAuthority)
        {
            Runner.LoadScene(SceneRef.FromIndex(2));
        }
    }
}
