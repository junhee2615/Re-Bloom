using UnityEngine;

/// <summary>
/// AliveStump3 미션 : 진동-Trigger 미션과 떨어지는 노트 미션이 합쳐진 복합 미션.
/// (AliveStump3 하위 Panel 오브젝트에 붙인다.)
/// 앞의 두 미션 로직을 재사용할 수 있도록 구성하는 것을 권장한다.
/// </summary>
public class CombinedMission : ActivationMission
{
    public override void StartMission()
    {
        Debug.Log($"{name} : 복합 미션 시작");

        // TODO: 미션 로직
        //  - 진동-Trigger 파트 + 노트 파트를 순차 또는 동시에 진행
        //  - (VibrationTriggerMission / FallingNoteMission 의 공통 판정 로직을 재사용 권장)
        //  - 둘 다 완료되면 클리어 텍스트 표시 후 Clear() 호출
    }

    public override void StopMission()
    {
        // TODO: 두 파트 모두 중단 / 정리
    }
}
