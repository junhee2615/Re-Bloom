using UnityEngine;

public class CrouchIK : MonoBehaviour
{
    [Header("References")]
    public Transform headTarget;
    public Transform body;
    public Transform leftFootTarget;
    public Transform rightFootTarget;

    [Header("Settings")]
    public float crouchSpeed = 8f;
    [Range(0f, 1f)] public float crouchStrength = 0.7f;

    [Header("Debug - Play 중 확인")]
    [SerializeField] private bool isRunning;
    [SerializeField] private float standingHeadHeight;
    [SerializeField] private float currentHeadHeight;
    [SerializeField] private float crouchAmount;
    [SerializeField] private float bodyLocalYBefore;
    [SerializeField] private float bodyLocalYAfter;

    private float initialBodyLocalY;
    private float leftFootY;
    private float rightFootY;
    private bool initialized;

    private void Start()
    {
        if (body != null)
        {
            initialBodyLocalY = body.localPosition.y;
        }

        if (leftFootTarget != null)
        {
            leftFootY = leftFootTarget.position.y;
        }

        if (rightFootTarget != null)
        {
            rightFootY = rightFootTarget.position.y;
        }
    }

    private void LateUpdate()
    {
        isRunning = true;

        if (headTarget == null || body == null)
        {
            return;
        }

        currentHeadHeight = headTarget.position.y;

        if (!initialized)
{
        // HeadTarget이 아직 원점에 있다면 HMD 추적이 준비되지 않은 상태
        if (currentHeadHeight < 0.5f)
        {
            return;
        }

        standingHeadHeight = currentHeadHeight;
        initialBodyLocalY = body.localPosition.y;
        initialized = true;

        Debug.Log(
            $"CrouchIK initialized: standingHeight={standingHeadHeight}, " +
            $"bodyLocalY={initialBodyLocalY}"
        );

        return;
    }

        if (leftFootTarget != null)
        {
            leftFootTarget.position = new Vector3(
                leftFootTarget.position.x,
                leftFootY,
                leftFootTarget.position.z
            );
        }

        if (rightFootTarget != null)
        {
            rightFootTarget.position = new Vector3(
                rightFootTarget.position.x,
                rightFootY,
                rightFootTarget.position.z
            );
        }

        crouchAmount = Mathf.Max(
        0f,
        standingHeadHeight - currentHeadHeight
    ) * crouchStrength;

        bodyLocalYBefore = body.localPosition.y;

        Vector3 targetPosition = body.localPosition;
        targetPosition.y = initialBodyLocalY - crouchAmount;

        body.localPosition = Vector3.Lerp(
            body.localPosition,
            targetPosition,
            Time.deltaTime * crouchSpeed
        );

        bodyLocalYAfter = body.localPosition.y;
    }
}