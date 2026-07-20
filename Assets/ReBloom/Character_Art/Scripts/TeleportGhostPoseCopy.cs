using System.Collections.Generic;
using UnityEngine;

public class TeleportGhostPoseCopy : MonoBehaviour
{
    [SerializeField]
    private Transform sourceRoot;

    [SerializeField]
    private Transform ghostRoot;

    private readonly List<TransformPair> transformPairs = new();

    private class TransformPair
    {
        public Transform source;
        public Transform ghost;

        public TransformPair(Transform source, Transform ghost)
        {
            this.source = source;
            this.ghost = ghost;
        }
    }

    private void Awake()
    {
        BuildTransformPairs();
    }

    private void LateUpdate()
    {
        if (sourceRoot == null || ghostRoot == null)
        {
            return;
        }

        // 앉거나 일어날 때 변하는 캐릭터 루트 높이 복사
        ghostRoot.localPosition = sourceRoot.localPosition;

        // 실제 캐릭터의 월드 Y축 방향만 고스트에 복사
        ghostRoot.rotation = Quaternion.Euler(
            0f,
            sourceRoot.eulerAngles.y,
            0f
        );

        // 실제 캐릭터의 모든 본 자세를 고스트에 복사
        foreach (TransformPair pair in transformPairs)
        {
            if (pair.source == null || pair.ghost == null)
            {
                continue;
            }

            pair.ghost.localPosition = pair.source.localPosition;
            pair.ghost.localRotation = pair.source.localRotation;
            pair.ghost.localScale = pair.source.localScale;
        }
    }

    [ContextMenu("Rebuild Transform Pairs")]
    private void BuildTransformPairs()
    {
        transformPairs.Clear();

        if (sourceRoot == null || ghostRoot == null)
        {
            Debug.LogWarning(
                "Source Root 또는 Ghost Root가 연결되지 않았습니다.",
                this
            );

            return;
        }

        Transform[] sourceTransforms =
            sourceRoot.GetComponentsInChildren<Transform>(true);

        foreach (Transform sourceTransform in sourceTransforms)
        {
            // 루트는 LateUpdate에서 별도로 처리
            if (sourceTransform == sourceRoot)
            {
                continue;
            }

            string relativePath = GetRelativePath(
                sourceRoot,
                sourceTransform
            );

            Transform matchingGhostTransform =
                ghostRoot.Find(relativePath);

            if (matchingGhostTransform == null)
            {
                continue;
            }

            transformPairs.Add(
                new TransformPair(
                    sourceTransform,
                    matchingGhostTransform
                )
            );
        }

        Debug.Log(
            $"고스트 포즈 복사 대상 {transformPairs.Count}개 연결 완료",
            this
        );
    }

    private string GetRelativePath(
        Transform root,
        Transform target
    )
    {
        string path = target.name;
        Transform current = target.parent;

        while (current != null && current != root)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }

        return path;
    }
}