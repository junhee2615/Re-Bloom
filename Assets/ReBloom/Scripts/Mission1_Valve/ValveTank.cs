using UnityEngine;

public class ValveTank : MonoBehaviour
{
    public ValveMissionManager missionManager;

    private float timer = 0f;

    private void OnTriggerStay(Collider other)
    {
        ValveHapticVib haptic = other.GetComponent<ValveHapticVib>();

        if (haptic != null)
        {
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                float stability = missionManager.stability;

                // stability ³·À»¼ö·Ï ·£´ý °­ÇÔ
                float amplitude =
                    Mathf.Lerp(Random.Range(0.2f, 1f),
                               0.5f,
                               stability);

                // stability ³·À»¼ö·Ï ·£´ý °£°Ý
                float duration =
                    Mathf.Lerp(Random.Range(0.05f, 0.3f),
                               0.15f,
                               stability);

                haptic.TriggerHaptic(amplitude, duration);

                timer = duration;
            }
        }
    }
}
