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

        if (IsTrainMoving && !parentSet)
        {
            parentSet = true;

            HardwareRig rig = FindFirstObjectByType<HardwareRig>();

            if (rig != null)
            {
                rig.SetTrainParent(transform);
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
    }
}
