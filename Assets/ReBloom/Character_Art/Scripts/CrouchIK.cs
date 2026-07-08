using UnityEngine;

public class CrouchIK : MonoBehaviour
{
    public Transform headTarget;
    public Transform body;
    public Transform leftFootTarget;
    public Transform rightFootTarget;
    public float crouchSpeed = 8f;

    private float standingHeadHeight;
    private float leftFootY;
    private float rightFootY;
    private bool initialized = false;

    void Start()
    {
        leftFootY = leftFootTarget.position.y;
        rightFootY = rightFootTarget.position.y;
    }

    void LateUpdate()
    {
        if (headTarget == null || body == null) return;

        if (!initialized)
        {
            standingHeadHeight = headTarget.position.y;
            initialized = true;
            return;
        }

        // Y축만 고정, XZ와 회전은 자유롭게
        leftFootTarget.position = new Vector3(
            leftFootTarget.position.x,
            leftFootY,
            leftFootTarget.position.z);

        rightFootTarget.position = new Vector3(
            rightFootTarget.position.x,
            rightFootY,
            rightFootTarget.position.z);

        // 발 회전은 건드리지 않음 (HMD 회전 따라가게)

        // 몸통 높이 조정
        float crouchAmount = Mathf.Max(0f, standingHeadHeight - headTarget.position.y);
        body.localPosition = Vector3.Lerp(body.localPosition,
            new Vector3(body.localPosition.x, -crouchAmount, body.localPosition.z),
            Time.deltaTime * crouchSpeed);
    }
}