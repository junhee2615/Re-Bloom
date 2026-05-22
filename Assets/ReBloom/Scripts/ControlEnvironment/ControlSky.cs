using UnityEngine;

public class ControlSky : MonoBehaviour
{
    // 회전하는 하늘
    void Update()
    {
        RenderSettings.skybox.SetFloat("_Rotation", Time.time * 1.0f);
    }
}
