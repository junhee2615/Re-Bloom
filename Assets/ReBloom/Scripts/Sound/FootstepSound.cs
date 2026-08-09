using UnityEngine;
using UnityEngine.InputSystem;

public class FootstepSound : MonoBehaviour
{
    [Header("Move Input")]
    [SerializeField] private InputActionReference moveInput;

    [Header("Footstep Sound")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] footstepClips;

    [Tooltip("발소리 사이의 간격")]
    [SerializeField] private float stepInterval = 0.5f;

    [Tooltip("이 값보다 조이스틱 입력이 클 때 걷는 것으로 판단")]
    [SerializeField] private float moveThreshold = 0.2f;

    private float stepTimer;

    private void Update()
    {
        if (moveInput == null ||
            audioSource == null ||
            footstepClips == null ||
            footstepClips.Length == 0)
            return;

        Vector2 input = moveInput.action.ReadValue<Vector2>();

        bool isWalking = input.magnitude > moveThreshold;

        if (!isWalking)
        {
            stepTimer = stepInterval;
            return;
        }

        stepTimer -= Time.deltaTime;

        if (stepTimer <= 0f)
        {
            PlayFootstep();
            stepTimer = stepInterval;
        }
    }

    private void PlayFootstep()
    {
        int index = Random.Range(0, footstepClips.Length);

        audioSource.PlayOneShot(footstepClips[index]);
    }
}