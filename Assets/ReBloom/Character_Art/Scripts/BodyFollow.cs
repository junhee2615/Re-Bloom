using UnityEngine;

public class BodyFollow : MonoBehaviour
{
    public Transform headTarget;
    public float rotationSpeed = 5f;

    void Update()
    {
        if (headTarget == null) return;

        float headYRotation = headTarget.eulerAngles.y;
        Quaternion targetRotation = Quaternion.Euler(0, headYRotation, 0);
        transform.rotation = Quaternion.Lerp(transform.rotation,
                            targetRotation, Time.deltaTime * rotationSpeed);
    }
}