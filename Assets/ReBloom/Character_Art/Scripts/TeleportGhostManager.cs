using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class TeleportGhostManager : MonoBehaviour
{
    public enum CharacterType
    {
        Ear,
        Mental
    }

    [Header("테스트용 캐릭터 선택")]
    [SerializeField]
    private CharacterType characterType = CharacterType.Ear;

    [Header("캐릭터별 플레이어 Root")]
    [SerializeField]
    private Transform earSourceRoot;

    [SerializeField]
    private Transform mentalSourceRoot;

    [Header("텔레포트 참조")]
    [SerializeField]
    private XRRayInteractor teleportInteractor;

    [Header("고스트 프리팹")]
    [SerializeField]
    private GameObject earGhostPrefab;

    [SerializeField]
    private GameObject mentalGhostPrefab;

    private GameObject currentGhost;

    private void Start()
    {
        GameObject selectedPrefab;
        Transform selectedSourceRoot;

        if (characterType == CharacterType.Ear)
        {
            selectedPrefab = earGhostPrefab;
            selectedSourceRoot = earSourceRoot;
        }
        else
        {
            selectedPrefab = mentalGhostPrefab;
            selectedSourceRoot = mentalSourceRoot;
        }

        if (selectedPrefab == null)
        {
            Debug.LogError(
                "선택된 캐릭터의 고스트 프리팹이 연결되지 않았습니다."
            );
            return;
        }

        if (selectedSourceRoot == null)
        {
            Debug.LogError(
                "선택된 캐릭터의 Source Root가 연결되지 않았습니다."
            );
            return;
        }

        if (teleportInteractor == null)
        {
            Debug.LogError(
                "Teleport Interactor가 연결되지 않았습니다."
            );
            return;
        }

        currentGhost = Instantiate(selectedPrefab);
        currentGhost.name = selectedPrefab.name + "_Runtime";

        TeleportGhostPoseCopy poseCopy =
            currentGhost.GetComponent<TeleportGhostPoseCopy>();

        if (poseCopy != null)
        {
            poseCopy.Initialize(selectedSourceRoot);
        }
        else
        {
            Debug.LogError(
                "생성된 고스트에 TeleportGhostPoseCopy가 없습니다."
            );
        }

        TeleportGhostPosition ghostPosition =
            currentGhost.GetComponent<TeleportGhostPosition>();

        if (ghostPosition != null)
        {
            ghostPosition.Initialize(teleportInteractor);
        }
        else
        {
            Debug.LogError(
                "생성된 고스트에 TeleportGhostPosition이 없습니다."
            );
        }
    }
}