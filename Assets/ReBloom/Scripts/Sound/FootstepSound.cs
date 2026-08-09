using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Gravity;

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

    [Header("Landing")]
    [SerializeField] private GravityProvider gravityProvider;

    private bool wasGrounded = true;
    private bool wasInAirAfterJump = false;

    private void Update()
    {
        if (gravityProvider != null)
        {
            bool isGrounded = gravityProvider.isGrounded;

            if (!isGrounded)
            {
                wasInAirAfterJump = true;
            }
            else if (!wasGrounded && wasInAirAfterJump)
            {
                PlayFootstep();
                wasInAirAfterJump = false;
            }

            wasGrounded = isGrounded;
        }

        if (moveInput == null ||
            audioSource == null ||
            footstepClips == null ||
            footstepClips.Length == 0)
            return;

        Vector2 input = moveInput.action.ReadValue<Vector2>();

        bool isWalking = input.magnitude > moveThreshold;

        if (!isWalking)
        {
            stepTimer = 0f;
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