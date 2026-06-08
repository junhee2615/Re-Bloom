using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class LeverSwitch : MonoBehaviour
{
    private XRGrabInteractable grabInteractable;
    private Transform interactor;
    private float previousY;
    public bool isActivated = false;

    [Header("Lever Settings")]
    public float rotateSpeed = 200f;

    public float activationAngle = -170;
    private float currentAngle = 0f;

    void Start()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();

        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);
    }

    void Update()
    {
        if (interactor != null)
        {
            float currentY = interactor.position.y;
            float delta = currentY - previousY;

            currentAngle += delta * rotateSpeed;

            // 회전 범위 제한
            currentAngle = Mathf.Clamp(currentAngle, -170f, 0f);

            // 직접 회전 적용
            transform.localRotation = Quaternion.Euler(currentAngle, 0f, 0f);

            previousY = currentY;

            Debug.Log(currentAngle);

            if (currentAngle <= activationAngle && !isActivated)
            {
                isActivated = true;
                Debug.Log(gameObject.name + " Activated");
            }
        }
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        interactor = args.interactorObject.transform;
        previousY = interactor.position.y;
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        interactor = null;
    }
}