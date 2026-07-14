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
        if (!HasStateAuthority)
            return;

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

        Vector3 rightTarget =
            rightStart + Vector3.right * 0.5f;

        Vector3 leftTarget =
            leftStart + Vector3.left * 0.5f;

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
