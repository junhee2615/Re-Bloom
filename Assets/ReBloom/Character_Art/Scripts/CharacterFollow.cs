using UnityEngine;

public class CharacterFollow : MonoBehaviour
{
    public Transform xrRig;

    void LateUpdate()
    {
        // XZ 위치만 따라가고 Y는 고정
        Vector3 targetPos = xrRig.position;
        targetPos.y = transform.position.y;
        transform.position = targetPos;
    }
}