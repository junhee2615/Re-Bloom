using UnityEngine;

public class CrouchIK : MonoBehaviour
{
    [Header("References")]
    public Transform headTarget;
    public Transform body;
    public Transform leftFootTarget;
    public Transform rightFootTarget;
    public Transform playerRoot;

    [Header("Settings")]
    public float crouchSpeed = 8f;

    [Range(0f, 1f)]
    public float crouchStrength = 0.7f;

    [Header("Debug - Play 중 확인")]
    [SerializeField] private bool isRunning;
    [SerializeField] private float standingHeadHeight;
    [SerializeField] private float currentHeadHeight;
    [SerializeField] private float crouchAmount;
    [SerializeField] private float bodyLocalYBefore;
    [SerializeField] private float bodyLocalYAfter;

    [SerializeField] private float leftFootLocalY;
    [SerializeField] private float rightFootLocalY;

    private float initialBodyLocalY;
    private bool initialized;

    private void Start()
    {
        if (body != null)
        {
            initialBodyLocalY = body.localPosition.y;
        }

        if (playerRoot != null)
        {
            if (leftFootTarget != null)
            {
                leftFootLocalY =
                    leftFootTarget.position.y - playerRoot.position.y;
            }

            if (rightFootTarget != null)
            {
                rightFootLocalY =
                    rightFootTarget.position.y - playerRoot.position.y;
            }
        }
    }

    private void LateUpdate()
    {
        isRunning = true;

        if (headTarget == null ||
            body == null ||
            playerRoot == null)
        {
            return;
        }

        // Player Root 기준 상대 머리 높이
        currentHeadHeight =
            headTarget.position.y - playerRoot.position.y;

        if (!initialized)
        {
            if (currentHeadHeight < 0.5f)
            {
                return;
            }

            standingHeadHeight = currentHeadHeight;
            initialBodyLocalY = body.localPosition.y;

            // HMD 추적이 정상화된 시점에 발 높이도 다시 저장
            if (leftFootTarget != null)
            {
                leftFootLocalY =
                    leftFootTarget.position.y - playerRoot.position.y;
            }

            if (rightFootTarget != null)
            {
                rightFootLocalY =
                    rightFootTarget.position.y - playerRoot.position.y;
            }

            initialized = true;

            Debug.Log(
                $"CrouchIK initialized: " +
                $"standingHeight={standingHeadHeight}, " +
                $"bodyLocalY={initialBodyLocalY}"
            );

            return;
        }

        // Player Root 기준 상대 높이로 발 Y 유지
        if (leftFootTarget != null)
        {
            leftFootTarget.position = new Vector3(
                leftFootTarget.position.x,
                playerRoot.position.y + leftFootLocalY,
                leftFootTarget.position.z
            );
        }

        if (rightFootTarget != null)
        {
            rightFootTarget.position = new Vector3(
                rightFootTarget.position.x,
                playerRoot.position.y + rightFootLocalY,
                rightFootTarget.position.z
            );
        }

        crouchAmount = Mathf.Max(
            0f,
            standingHeadHeight - currentHeadHeight
        ) * crouchStrength;

        bodyLocalYBefore = body.localPosition.y;

        Vector3 targetPosition = body.localPosition;

        targetPosition.y =
            initialBodyLocalY - crouchAmount;

        body.localPosition = Vector3.Lerp(
            body.localPosition,
            targetPosition,
            Time.deltaTime * crouchSpeed
        );

        bodyLocalYAfter = body.localPosition.y;
    }
}