using UnityEngine;


[RequireComponent(typeof(Collider))]
public class PetalTouchForwarder : MonoBehaviour
{
    [Tooltip("이 연꽃잎이 속한 플랜트의 미션 (루트의 PetalRhythmMission)")]
    [SerializeField] private PetalRhythmMission mission;

    [Tooltip("접촉으로 인정할 컨트롤러 태그")]
    [SerializeField] private string rightControllerTag = "Right Controller";


    public void SetMission(PetalRhythmMission m) => mission = m;

private void OnTriggerEnter(Collider other)
    {
        Debug.Log("[PetalDebug] " + name + " TriggerEnter by '" + other.name + "' tag=" + other.tag + " mission=" + (mission != null ? mission.name : "NULL"));

        if (mission == null)
            return;

        if (other.CompareTag(rightControllerTag))
        {
            Debug.Log("[PetalDebug] " + name + " -> OnPetalTouched() 호출");
            mission.OnPetalTouched();
        }
    }
}
