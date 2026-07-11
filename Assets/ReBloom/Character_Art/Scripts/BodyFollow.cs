using UnityEngine;

public class BodyFollow : MonoBehaviour
{
    public Transform headTarget;
    public float rotationSpeed = 5f;

    private void Start()
    {
        // Inspector에 연결되어 있지 않다면 자동으로 찾기
        if (headTarget == null)
        {
            HardwareRig rig = FindFirstObjectByType<HardwareRig>();

            if (rig != null)
                headTarget = rig.headTransform;
        }
    }

    void Update()
    {
        if (headTarget == null) return;

        float headYRotation = headTarget.eulerAngles.y;
        Quaternion targetRotation = Quaternion.Euler(0, headYRotation, 0);
        transform.rotation = Quaternion.Lerp(transform.rotation,
                            targetRotation, Time.deltaTime * rotationSpeed);
    }
}