using UnityEngine;

/// <summary>
/// 모든 "뿌리 활성화 미션"의 공통 베이스.
/// 각 AliveStump 하위 Panel에 붙는 미션 스크립트가 이 클래스를 상속한다.
/// RootActivation은 이 계약(StartMission / OnCleared)만 알고 미션을 구동하므로,
/// 뿌리마다 미션 방식이 완전히 달라도 상위 흐름은 그대로 동작한다.
/// </summary>
public abstract class ActivationMission : MonoBehaviour
{
    /// <summary>미션 클리어 시 RootActivation이 받을 콜백.</summary>
    public System.Action OnCleared;

    /// <summary>
    /// 이 뿌리 차례가 되면 RootActivation이 호출한다.
    /// 패널 UI(안내 텍스트 / 노트 등)를 세팅하고 입력 판정을 시작한다.
    /// </summary>
    public abstract void StartMission();

    /// <summary>
    /// 미션을 중단 / 리셋할 때 호출 (필요 없으면 비워 둬도 됨).
    /// </summary>
    public virtual void StopMission() { }

/// <summary>
    /// 컨트롤러 접촉을 이 미션에 알릴 때 호출한다 (접촉 감지가 필요한 미션만 override).
    /// </summary>
    public virtual void NotifyHandDetected() { }


    /// <summary>
    /// 각 미션이 성공 시점(클리어 텍스트를 보여준 뒤 등)에 호출한다.
    /// RootActivation에 완료를 알려 다음 단계로 넘어간다.
    /// </summary>
    protected void Clear()
    {
        OnCleared?.Invoke();
    }
}
