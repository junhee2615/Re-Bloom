using UnityEngine;

public class HeadTargetFollow : MonoBehaviour
{
    public Transform hmdCamera;

    void LateUpdate()
    {
        transform.position = hmdCamera.position;
        transform.rotation = hmdCamera.rotation;
    }
}