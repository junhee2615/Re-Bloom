using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ControlSky : MonoBehaviour
{
    // Update is called once per frame
    void Update()
    {
        // 하늘 회전
        RenderSettings.skybox.SetFloat("_Rotation", Time.time * 1f);
    }
}
