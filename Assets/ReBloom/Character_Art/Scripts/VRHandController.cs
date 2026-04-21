using UnityEngine;
using UnityEngine.InputSystem; // 유니티 6에서 입력을 받기 위해 필수

public class VRHandController : MonoBehaviour
{
    public Animator handAnimator;       // 손 모델의 애니메이터
    public InputActionProperty gripAction; // 컨트롤러의 그립(잡기) 버튼 액션
    public string parameterName = "Grip";  // 애니메이터에 만든 파라미터 이름

    void Update()
    {
        // 1. 컨트롤러 버튼이 얼마나 눌렸는지 소수점 값(0.0~1.0)으로 읽어옵니다.
        float gripValue = gripAction.action.ReadValue<float>();

        // 2. 그 값을 애니메이터의 "Grip" 파라미터에 넣어줍니다.
        handAnimator.SetFloat(parameterName, gripValue);
    }
}