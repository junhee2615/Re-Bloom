using UnityEngine;
using UnityEngine.XR;

public class HapticTrigger : MonoBehaviour
{
    public XRNode targetNode = XRNode.RightHand;

    // amplitude : 진동 세기, duration : 초
    public void TriggerHaptic(float amplitude, float duration)
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(targetNode);

        if (device.TryGetHapticCapabilities(out HapticCapabilities cap))
        {
            if (cap.supportsImpulse)
            {
                device.SendHapticImpulse(0, amplitude, duration);
            }
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        Debug.Log("Collision Enter");

        // Client(PlayerId != 1)만 진동 느낌
        if (!PlayerRole.LocalIsClient())
            return;

        TriggerHaptic(1.0f, 0.1f);
    }
}
