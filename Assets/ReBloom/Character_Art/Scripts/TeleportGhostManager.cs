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

    [Header("플레이어 Root")]
    [SerializeField]
    private Transform sourceRoot;

    [Header("텔레포트 참조")]
    [SerializeField]
    private XRRayInteractor teleportInteractor;

    [Header("고스트 프리팹")]
    [SerializeField]
    private GameObject earGhostPrefab;

    [SerializeField]
    private GameObject mentalGhostPrefab;

    private GameObject currentGhost;

    public void Initialize(
        CharacterType newCharacterType,
        Transform newSourceRoot,
        XRRayInteractor newTeleportInteractor)
    {
        characterType = newCharacterType;
        sourceRoot = newSourceRoot;
        teleportInteractor = newTeleportInteractor;

        CreateGhost();
    }

    private void CreateGhost()
    {
        // 씬을 옮기며 Initialize가 다시 불릴 때 고스트가 중복 생성되지 않도록 정리한다.
        if (currentGhost != null)
        {
            Destroy(currentGhost);
            currentGhost = null;
        }

        GameObject selectedPrefab;

        if (characterType == CharacterType.Ear)
        {
            selectedPrefab = earGhostPrefab;
        }
        else
        {
            selectedPrefab = mentalGhostPrefab;
        }

        if (selectedPrefab == null)
        {
            Debug.LogError(
                "선택된 캐릭터의 고스트 프리팹이 연결되지 않았습니다."
            );
            return;
        }

        if (sourceRoot == null)
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
            poseCopy.Initialize(sourceRoot);
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