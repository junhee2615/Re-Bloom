using Fusion;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using static UnityEngine.ParticleSystem;

public class TrainFloor : NetworkBehaviour
{
    [Networked] private NetworkBool Player1On { get; set; }
    [Networked] private NetworkBool Player2On { get; set; }
    [Networked] private NetworkBool IsActivated { get; set; }

    public AudioSource audioSource;
    public AudioClip doorCloseClip;
    public Transform doorRight;
    public Transform doorLeft;
    private ScreenFade screenFade;

    private void OnTriggerEnter(Collider other)
    {
        if (!HasStateAuthority)
            return;

        NetworkObject obj = other.GetComponentInParent<NetworkObject>();

        if (obj == null)
            return;

        if (obj.InputAuthority.PlayerId == 1)
            Player1On = true;

        if (obj.InputAuthority.PlayerId == 2)
            Player2On = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (!HasStateAuthority)
            return;

        NetworkObject obj = other.GetComponentInParent<NetworkObject>();

        if (obj == null)
            return;

        if (obj.InputAuthority.PlayerId == 1)
            Player1On = false;

        if (obj.InputAuthority.PlayerId == 2)
            Player2On = false;
    }

    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        if (Player1On &&
            Player2On &&
            !IsActivated)
        {
            IsActivated = true;

            Activate();
        }
    }

    private void Activate()
    {
        StartCoroutine(CloseDoorAnimation());
        StartCoroutine(screenFade.FadeOut(1f));
        if (HasStateAuthority)
        {
            Runner.LoadScene(SceneRef.FromIndex(2));
        }
    }

    IEnumerator CloseDoorAnimation()
    {
        yield return new WaitForSeconds(3f);

        if (audioSource != null)
            audioSource.PlayOneShot(doorCloseClip);

        Vector3 rightStart = doorRight.localPosition;
        Vector3 leftStart = doorLeft.localPosition;

        Vector3 rightTarget = rightStart + Vector3.left * 0.5f;
        Vector3 leftTarget = leftStart + Vector3.right * 0.5f;

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
        // trainController.StartTrain();
    }
}
