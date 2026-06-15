using UnityEngine;

public class BodyFollow : MonoBehaviour
{
    public Transform headTarget;
    public Transform spineBone;
    public float rotationSpeed = 5f;
    public float standingHeight = 1.6f;
    public float bendMultiplier = 60f;
    public float maxBendAngle = 45f;

    void Update()
    {
        if (headTarget == null) return;

        float headYRotation = headTarget.eulerAngles.y;
        Quaternion targetYRotation = Quaternion.Euler(0, headYRotation, 0);
        transform.rotation = Quaternion.Lerp(transform.rotation,
                            targetYRotation, Time.deltaTime * rotationSpeed);
    }

    void LateUpdate()
    {
        if (headTarget == null || spineBone == null) return;

        float heightDiff = standingHeight - headTarget.position.y;
        float bendAngle = Mathf.Clamp(heightDiff * bendMultiplier, 0f, maxBendAngle);

        UnityEngine.Debug.Log("heightDiff: " + heightDiff);
        UnityEngine.Debug.Log("bendAngle: " + bendAngle);
        UnityEngine.Debug.Log("spineBone localRotation: " + spineBone.localRotation);

        spineBone.localRotation = Quaternion.Euler(bendAngle, 0, 0);
    }
}