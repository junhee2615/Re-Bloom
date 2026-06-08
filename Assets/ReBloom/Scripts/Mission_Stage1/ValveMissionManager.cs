using UnityEngine;

public class ValveMissionManager : MonoBehaviour
{
    public ValveRotate valve;

    [Range(0f, 1f)]
    public float stability;
    public bool isMissionClear = false;
    public GameObject gameClearText;

    void Update()
    {
        // 현재 밸브 회전값
        float angle = valve.transform.localEulerAngles.z;
       
        if (angle > 180)
            angle -= 360;

        // 왼쪽 회전량 기준 안정도 증가
        float normalized = Mathf.Abs(angle) / 90f;

        stability = Mathf.Clamp01(normalized);

        // 미션 성공 판정
        if (stability >= 0.9f && !isMissionClear)
        {
            isMissionClear = true;
            gameClearText.SetActive(true);
        }
    }
}
