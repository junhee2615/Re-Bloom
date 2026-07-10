using UnityEngine;

public class HeadTargetFollow : MonoBehaviour
{
    public Transform hmdCamera;

    private void Start()
    {
        if (hmdCamera == null)
        {
            HardwareRig rig = FindFirstObjectByType<HardwareRig>();

            if (rig != null)
            {
                hmdCamera = rig.headTransform;
            }
            else
            {
                Debug.LogError("HardwareRig를 찾을 수 없습니다.");
            }
        }
    }

    void LateUpdate()
    {
        transform.position = hmdCamera.position;
        transform.rotation = hmdCamera.rotation;
    }
}