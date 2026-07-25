using UnityEngine;

/// <summary>
/// AliveStump 미션 : 뿌리에서 올라오는 진동 패턴을 느끼고,
/// 그 패턴과 똑같이 컨트롤러 Trigger 버튼을 눌러 맞히는 미션.
/// (AliveStump 하위 Panel 오브젝트에 붙인다.)
/// </summary>
public class VibrationTriggerMission : ActivationMission
{
    public override void StartMission()
    {
        Debug.Log($"{name} : 진동-Trigger 미션 시작");

        // TODO: 미션 로직
        //  - 패널에 안내 텍스트 표시
        //  - 진동 패턴 재생 (햅틱)
        //  - 플레이어의 Trigger 입력 시퀀스 판정
        //  - 패턴을 모두 맞히면 클리어 텍스트 표시 후 Clear() 호출
    }

    public override void StopMission()
    {
        // TODO: 진동 / 입력 판정 중단, 코루틴 정리 등
    }
}
