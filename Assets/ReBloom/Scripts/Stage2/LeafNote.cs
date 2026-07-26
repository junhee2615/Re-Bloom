using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 떨어지는 노트(LeafNoteButton)에 붙어, 손 포크/레이 트리거로 "눌리는 순간"(PointerDown)을 감지한다.
/// Button.onClick(누르고 → 떼기)은 노트가 움직이면 뗄 때 대상에서 벗어나 클릭이 취소되기 쉬워서,
/// 눌리는 즉시 반응하도록 PointerDown을 쓴다. FallingNoteMission이 onPressed 콜백을 연결한다.
/// </summary>
public class LeafNote : MonoBehaviour, IPointerDownHandler
{
    public System.Action onPressed;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (onPressed != null) onPressed.Invoke();
    }
}
