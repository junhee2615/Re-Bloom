using UnityEngine;

public class TeleportGhostPoseCopy : MonoBehaviour
{
    [Header("Pose Roots")]
    [SerializeField]
    private Transform sourceRoot;

    [SerializeField]
    private Transform ghostRoot;

    private Transform[] sourceBones;
    private Transform[] ghostBones;

    private void Awake()
    {
        RefreshBones();
    }

    private void LateUpdate()
    {
        if (sourceRoot == null || ghostRoot == null)
        {
            return;
        }

        if (sourceBones == null || ghostBones == null)
        {
            RefreshBones();
            return;
        }

        // 앉기 높이 등 실제 플레이어 루트의 위치 복사
        ghostRoot.localPosition = sourceRoot.localPosition;

        // 실제 플레이어가 바라보는 Y축 방향 복사
        Vector3 sourceEulerAngles = sourceRoot.eulerAngles;

        ghostRoot.rotation = Quaternion.Euler(
            0f,
            sourceEulerAngles.y,
            0f
        );

        int boneCount = Mathf.Min(
            sourceBones.Length,
            ghostBones.Length
        );

        // 루트 다음의 자식 본부터 자세 복사
        for (int i = 1; i < boneCount; i++)
        {
            ghostBones[i].localPosition =
                sourceBones[i].localPosition;

            ghostBones[i].localRotation =
                sourceBones[i].localRotation;

            ghostBones[i].localScale =
                sourceBones[i].localScale;
        }
    }

    public void Initialize(Transform newSourceRoot)
    {
        sourceRoot = newSourceRoot;
        RefreshBones();
    }

    private void RefreshBones()
    {
        if (sourceRoot != null)
        {
            sourceBones =
                sourceRoot.GetComponentsInChildren<Transform>(true);
        }
        else
        {
            sourceBones = null;
        }

        if (ghostRoot != null)
        {
            ghostBones =
                ghostRoot.GetComponentsInChildren<Transform>(true);
        }
        else
        {
            ghostBones = null;
        }
    }
}