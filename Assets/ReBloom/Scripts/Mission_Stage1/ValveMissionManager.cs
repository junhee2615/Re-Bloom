using UnityEngine;
using Fusion;

public class ValveMissionManager : NetworkBehaviour
{
    public ValveRotate valve;

    [Range(0f, 1f)]
    public float stability;

    [Networked]
    public NetworkBool isMissionClear { get; set; }

    public GameObject gameClearText;

    void Update()
    {
        if (Object == null || !Object.IsValid)
            return;

        // 현재 밸브 회전값
        float angle = valve.CurrentAngle;
       
        if (angle > 180)
            angle -= 360;

        // 왼쪽 회전량 기준 안정도 증가
        float normalized = Mathf.Abs(angle) / 90f;
        stability = Mathf.Clamp01(normalized);

        // UI는 Host/Client 각자 화면에서 표시
        if (gameClearText != null)
            gameClearText.SetActive(isMissionClear);

        // 미션 성공 판정은 StateAuthority만 처리
        if (!HasStateAuthority)
            return;

        if (isMissionClear)
            return;

        if (stability >= 0.9f)
        {
            isMissionClear = true;
        }
    }
}
