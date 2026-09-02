using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;

public sealed class XRWalkingAnimation : MonoBehaviour
{
    private static readonly int IsWalkingHash =
        Animator.StringToHash("IsWalking");

    [Header("References")]
    [SerializeField]
    private NetworkPlayer networkPlayer;

    [SerializeField]
    private Animator characterAnimator;

    [SerializeField]
    private InputActionReference moveInput;

    [SerializeField]
    private TwoBoneIKConstraint leftLegIK;

    [SerializeField]
    private TwoBoneIKConstraint rightLegIK;

    [Header("Settings")]
    [SerializeField]
    [Range(0f, 1f)]
    private float inputThreshold = 0.1f;

    [SerializeField]
    [Range(0f, 20f)]
    private float ikBlendSpeed = 8f;

    private float currentIKWeight = 1f;

    private void Update()
    {
        if (networkPlayer == null || characterAnimator == null)
        {
            return;
        }

        bool isWalking;

        if (networkPlayer.IsLocalNetworkRig)
        {
            if (moveInput == null)
            {
                return;
            }

            Vector2 moveValue = moveInput.action.ReadValue<Vector2>();
            isWalking = moveValue.magnitude > inputThreshold;
        }
        else
        {
            isWalking = networkPlayer.IsWalking;
        }

        characterAnimator.SetBool(IsWalkingHash, isWalking);

        float targetIKWeight = isWalking ? 0f : 1f;

        currentIKWeight = Mathf.MoveTowards(
            currentIKWeight,
            targetIKWeight,
            ikBlendSpeed * Time.deltaTime
        );

        if (leftLegIK != null)
        {
            leftLegIK.weight = currentIKWeight;
        }

        if (rightLegIK != null)
        {
            rightLegIK.weight = currentIKWeight;
        }
    }
}