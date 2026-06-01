using Fusion;
using UnityEngine;

public class NetworkPlayer : NetworkBehaviour
{
    public GameObject bodyRoot;
    public GameObject mainCamera;
    public GameObject locomotion;
    public UnityEngine.Behaviour[] xrComponents;

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

    void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;

        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}