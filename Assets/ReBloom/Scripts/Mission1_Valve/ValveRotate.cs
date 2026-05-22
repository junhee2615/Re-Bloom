using Fusion;
using UnityEngine;
using UnityEngine.XR;

public class ValveRotate : MonoBehaviour
{
    public Transform rightController;

    private bool isTouching = false;
    private bool isHolding = false;

    private float previousX;

    void Update()
    {
        // Client면 밸브 조작 불가
        if (NetworkRunner.Instances[0].IsSharedModeMasterClient)
            return;

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // 트리거 버튼 입력
        bool triggerPressed;
        device.TryGetFeatureValue(CommonUsages.triggerButton, out triggerPressed);

        // 닿아 있고 트리거 눌렀을 때
        if (isTouching && triggerPressed)
        {
            if (!isHolding)
            {
                isHolding = true;
                previousX = rightController.position.x;
            }

            float currentX = rightController.position.x;
            // 컨트롤러 위치 이동 계산 
            float delta = currentX - previousX;

            // 밸브 회전
            transform.Rotate(Vector3.forward, -delta * 200f, Space.Self);

            previousX = currentX;
        }
        else
        {
            isHolding = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Right Controller"))
        {
            isTouching = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Right Controller"))
        {
            isTouching = false;
        }
    }
}
