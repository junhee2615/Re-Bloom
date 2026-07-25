using UnityEngine;

/// <summary>
/// AliveStump2 미션 : 떨어지는 노트를 타이밍에 맞춰 터치하는 리듬게임.
/// (AliveStump2 하위 Panel 오브젝트에 붙인다.)
/// </summary>
public class FallingNoteMission : ActivationMission
{
    public override void StartMission()
    {
        Debug.Log($"{name} : 떨어지는 노트 미션 시작");

        // TODO: 미션 로직
        //  - 패널에 안내 텍스트 표시
        //  - 노트 스폰 / 낙하 시작
        //  - 터치 타이밍 판정
        //  - 모든 노트를 처리하면 클리어 텍스트 표시 후 Clear() 호출
    }

    public override void StopMission()
    {
        // TODO: 노트 스폰 중단, 남은 노트 정리 등
    }
}
