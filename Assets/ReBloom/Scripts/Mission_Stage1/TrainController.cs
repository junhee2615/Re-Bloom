using Fusion;
using System.Collections;
using UnityEngine;

public class TrainController : NetworkBehaviour
{
    [SerializeField] private float moveDistance = 30f;
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float acceleration = 2f;
    [Networked]
    private NetworkBool IsTrainMoving { get; set; }
    private bool parentSet = false;

    private bool isMoving = false;

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
    }
}
