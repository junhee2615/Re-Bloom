using UnityEngine;

public class ValveTank : MonoBehaviour
{
    public ValveMissionManager missionManager;

    private float timer = 0f;

    private void OnTriggerStay(Collider other)
    {
        ValveHapticVib haptic = other.GetComponent<ValveHapticVib>();

        // ear 역할만 진동을 느낀다.
        if (!RoleManager.LocalIsEar)
            return;

        if (haptic != null)
        {
            timer -= Time.deltaTime;

            if (timer <= 0f)
            {
                float stability = missionManager.stability;

                // stability �������� ���� ����
                float amplitude =
                    Mathf.Lerp(Random.Range(0.2f, 1f),
                               0.5f,
                               stability);

                // stability �������� ���� ����
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
