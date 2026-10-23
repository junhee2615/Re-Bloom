using UnityEngine;

/// <summary>
/// 안내판을 띄울 단계인지만 알려 주는 판정기.
///
/// <see cref="MissionGuideBoard"/> 는 Stage2의 <c>TutorialMissionManager_2</c> 를 직접 읽도록
/// 만들어져 있어 다른 Stage에서는 쓸 수 없었다. 그 판정 한 줄만 이 타입으로 빼 두면,
/// Stage마다 매니저가 달라도 보드 본체(디자인·거리 감지·페이드·빌보드·컷씬 숨김)를
/// 그대로 재사용할 수 있다.
///
/// 보드의 <c>stepSource</c> 를 <b>비워 두면</b> 보드가 기존처럼 Stage2 매니저를 직접 읽는다.
/// 그래서 이미 배치된 Stage2 보드들은 이 타입을 몰라도 동작이 변하지 않는다.
///
/// 인터페이스가 아니라 추상 <see cref="MonoBehaviour"/> 인 이유:
/// Unity의 <c>[SerializeField]</c> 는 인터페이스 참조를 직렬화하지 못한다.
/// 보드와 같은 GameObject에 붙여 Inspector에서 연결한다.
///
/// 구현체는 "지금이 그 단계인가"만 답하고 그 밖의 일을 하지 않는다.
/// 표시 여부·페이드·거리 판정은 모두 보드가 그대로 맡는다.
/// </summary>
public abstract class MissionStepSource : MonoBehaviour
{
    /// <summary>지금이 이 안내판을 띄울 미션 단계인가.</summary>
    public abstract bool IsOnTargetStep { get; }
}
