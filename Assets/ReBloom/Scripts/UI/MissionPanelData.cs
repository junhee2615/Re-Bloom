using UnityEngine;

[CreateAssetMenu(fileName = "MissionPanelData", menuName = "ReBloom/UI/Mission Panel Data")]
public class MissionPanelData : ScriptableObject
{
    [SerializeField] private string missionLabel;
    [SerializeField] private string title;
    [SerializeField, TextArea] private string description;

    public string MissionLabel => missionLabel;
    public string Title => title;
    public string Description => description;
}
