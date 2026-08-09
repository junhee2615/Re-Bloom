using UnityEngine;

/// <summary>
/// 흙 : 한 손으로 잡아 옮기거나 던진다.
/// 잡은 손을 따라오다가, 놓으면 그 순간의 속도대로 물리로 날아가/떨어진다.
/// </summary>
public class SoilObstacle : WaterMissionObstacle
{
    private Vector3 offsetPos;
    private Quaternion offsetRot;

    protected override void OnHeldBegin(Hands h)
    {
        if (!h.hasA) return;
        // 잡은 순간의 손 기준 상대 포즈를 유지
        offsetPos = Quaternion.Inverse(h.rotA) * (transform.position - h.posA);
        offsetRot = Quaternion.Inverse(h.rotA) * transform.rotation;
    }

    protected override void ComputeHeldPose(Hands h, ref Vector3 pos, ref Quaternion rot)
    {
        if (!h.hasA) return;
        pos = h.posA + h.rotA * offsetPos;
        rot = h.rotA * offsetRot;
    }

    // 놓기: 베이스 기본 동작(실어준 속도로 물리 낙하 = 던지기)을 그대로 사용한다.
}
