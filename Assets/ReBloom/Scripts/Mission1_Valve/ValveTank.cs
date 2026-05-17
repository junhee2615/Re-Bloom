using UnityEngine;

public class ValveTank : MonoBehaviour
{
    private void OnTriggerStay(Collider other)
    {
        ValveHapticVib haptic = other.GetComponent<ValveHapticVib>();

        if (haptic != null)
        {
            haptic.TriggerHaptic(1.0f, 0.1f);
        }
    }
}
