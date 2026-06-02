using Fusion;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    public GameObject bodyRoot;
    public GameObject mainCamera;
    public GameObject locomotion;
    public UnityEngine.Behaviour[] xrComponents;

    public Transform headTarget;
    public Transform leftHandIKTarget;
    public Transform rightHandIKTarget;

    public Transform mainCameraTransform;
    public Transform leftControllerTransform;
    public Transform rightControllerTransform;


    public override void Spawned()
    {
        if (Object.HasInputAuthority)
        {
            // 내 플레이어
            SetLayerRecursively(bodyRoot, LayerMask.NameToLayer("MyThirdPerson"));

            mainCamera.SetActive(true);
            locomotion.SetActive(true);
        }
        else
        {
            // 상대 플레이어
            SetLayerRecursively(bodyRoot, LayerMask.NameToLayer("ThirdPerson"));

            mainCamera.SetActive(false);
            locomotion.SetActive(false);

            foreach (var comp in xrComponents)
            {
                comp.enabled = false;
            }
        }
    }

    private void LateUpdate()
    {
        if (!Object.HasInputAuthority)
            return;

        // 머리
        headTarget.position = mainCameraTransform.position;
        headTarget.rotation = mainCameraTransform.rotation;

        // 왼손
        leftHandIKTarget.position = leftControllerTransform.position;
        leftHandIKTarget.rotation = leftControllerTransform.rotation;

        // 오른손
        rightHandIKTarget.position = rightControllerTransform.position;
        rightHandIKTarget.rotation = rightControllerTransform.rotation;
    }

    void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;

        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}