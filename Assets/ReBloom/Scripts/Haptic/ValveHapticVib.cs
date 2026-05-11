using UnityEngine;
using UnityEngine.XR;

public class ValveHapticVib : MonoBehaviour
{
    public XRNode targetNode;

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
}
