using UnityEngine;

public class XRSimpleMove : MonoBehaviour
{
    public float speed = 3.0f;
    public CharacterController controller;
    public Transform head; // Ä«¸Þ¶ó

    void Update()
    {
        float x = Input.GetAxis("Horizontal"); // A, D
        float z = Input.GetAxis("Vertical");   // W, S

        Vector3 move = head.forward * z + head.right * x;
        move.y = 0;

        controller.Move(move * speed * Time.deltaTime);
    }
}