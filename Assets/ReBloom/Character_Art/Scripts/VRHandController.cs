using UnityEngine;
using UnityEngine.InputSystem;

public class VRHandController : MonoBehaviour
{
    public Animator handAnimator;
    public InputActionProperty gripAction;
    public string parameterName = "Grip";

    void Update()
    {
        float gripValue = gripAction.action.ReadValue<float>();

        handAnimator.SetFloat(parameterName, gripValue);
    }
}