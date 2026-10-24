using UnityEngine;

/// <summary>튜토리얼 목표 표시(바닥 원, 기둥)를 숨 쉬듯 살짝 커졌다 작아지게 한다.</summary>
public class TutorialGlowPulse : MonoBehaviour
{
    [SerializeField] private float amount = 0.08f;
    [SerializeField] private float speed = 2.5f;
    [SerializeField] private bool keepY = true;

    private Vector3 baseScale;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void Update()
    {
        float k = 1f + Mathf.Sin(Time.time * speed) * amount;
        transform.localScale = keepY
            ? new Vector3(baseScale.x * k, baseScale.y, baseScale.z * k)
            : baseScale * k;
    }
}
