using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TeleportGhostPosition : MonoBehaviour
{
    [SerializeField]
    private XRRayInteractor teleportInteractor;

    private Renderer[] ghostRenderers;
    private bool isVisible;

    private void Awake()
    {
        ghostRenderers = GetComponentsInChildren<Renderer>(true);
        SetGhostVisible(false);
    }

    private void Update()
    {
        if (teleportInteractor == null)
        {
            SetGhostVisible(false);
            return;
        }

        bool teleportActive =
            teleportInteractor.isActiveAndEnabled &&
            teleportInteractor.gameObject.activeInHierarchy;

        if (teleportActive &&
            teleportInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            transform.position = hit.point;
            SetGhostVisible(true);
        }
        else
        {
            SetGhostVisible(false);
        }
    }

    private void SetGhostVisible(bool visible)
    {
        if (isVisible == visible)
        {
            return;
        }

        isVisible = visible;

        foreach (Renderer ghostRenderer in ghostRenderers)
        {
            ghostRenderer.enabled = visible;
        }
    }
}