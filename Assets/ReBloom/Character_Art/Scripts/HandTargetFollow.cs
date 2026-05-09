using UnityEngine;

public class HandTargetFollow : MonoBehaviour
{
    public Transform xrController;
    public Vector3 positionOffset;
    public Vector3 rotationOffset;

    void LateUpdate()
    {
        transform.position = xrController.TransformPoint(positionOffset);
        transform.rotation = xrController.rotation *
                            Quaternion.Euler(rotationOffset);
    }
}