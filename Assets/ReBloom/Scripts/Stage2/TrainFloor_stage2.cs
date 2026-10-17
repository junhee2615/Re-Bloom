using System.Collections;
using UnityEngine;
using Fusion;

public class TrainFloor_stage2 : NetworkBehaviour
{
    public AudioSource audioSource;
    public AudioClip doorCloseClip;

    public Transform doorRight;
    public Transform doorLeft;

    public override void Spawned()
    {
        // 문 Transform은 네트워크로 동기화되지 않으므로 모든 피어가 각자 연다
        StartCoroutine(OpenDoorAnimation());
    }

    IEnumerator OpenDoorAnimation()
    {
        // Stage2 시작 후 3초 대기
        yield return new WaitForSeconds(3f);

        if (audioSource != null)
            audioSource.PlayOneShot(doorCloseClip);

        Vector3 rightStart = doorRight.localPosition;
        Vector3 leftStart = doorLeft.localPosition;

        // 두 문짝이 서로 멀어지는 방향으로 연다 (doorRight/doorLeft를 반대로 연결해도 안전)
        float side = Mathf.Sign(rightStart.x - leftStart.x);
        if (side == 0f) side = 1f;

        Vector3 rightTarget =
            rightStart + Vector3.right * (0.5f * side);

        Vector3 leftTarget =
            leftStart + Vector3.left * (0.5f * side);

        float t = 0;

        while (t < 1)
        {
            t += Time.deltaTime;

            doorRight.localPosition =
                Vector3.Lerp(rightStart, rightTarget, t);

            doorLeft.localPosition =
                Vector3.Lerp(leftStart, leftTarget, t);

            yield return null;
        }

        doorRight.localPosition = rightTarget;
        doorLeft.localPosition = leftTarget;
    }
}
