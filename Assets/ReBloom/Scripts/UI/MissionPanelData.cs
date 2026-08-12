using UnityEngine;

[CreateAssetMenu(
    fileName = "MissionPanelData",
    menuName = "ReBloom/UI/Mission Panel Data")]
public class MissionPanelData : ScriptableObject
{
    [Header("Tutorial Images")]
    [SerializeField] private Sprite hostImage;
    [SerializeField] private Sprite clientImage;

    public Sprite HostImage => hostImage;
    public Sprite ClientImage => clientImage;
}