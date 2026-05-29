using UnityEngine;
using UnityEngine.XR;
using Fusion;

public class ValveHapticVib : MonoBehaviour
{
    public XRNode targetNode;

    public void TriggerHaptic(float amplitude, float duration)
    {
        // Host면 진동 안 함
        // if (!NetworkRunner.Instances[0].IsSharedModeMasterClient)
           // return;

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
