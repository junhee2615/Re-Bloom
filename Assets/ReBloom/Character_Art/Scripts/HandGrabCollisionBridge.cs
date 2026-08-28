using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 그랩 인터랙터의 잡기/놓기 이벤트를 물리 손(PhysicsHand)에 이어준다.
/// 잡는 동안 손↔잡힌 물체의 충돌을 무시시켜, 물리 손과 잡힌 물체가 같은 공간에서 서로 밀치는 것을 막는다.
/// 물체가 손에서 완전히 빠져나온 뒤에 복원한다.
///
/// 배치: 그랩 인터랙터(Near-Far Interactor 등)와 같은 GameObject에 붙인다.
/// physicsHand에는 그 인터랙터와 같은 쪽 손을 지정한다.
/// </summary>
public class HandGrabCollisionBridge : MonoBehaviour
{
    [Tooltip("충돌을 무시시킬 물리 손. 이 인터랙터와 같은 쪽 손을 지정한다.")]
    [SerializeField] PhysicsHand physicsHand;

    [Tooltip("놓은 뒤 물체가 손에서 빠져나오길 기다리는 최대 시간(초). 넘으면 겹쳐 있어도 복원한다.")]
    [SerializeField] float restoreTimeout = 3f;

    // 잡기/놓기 이벤트를 내는 그랩 인터랙터.
    XRBaseInteractor interactor;

    // 지금 충돌을 무시 중인 콜라이더
    readonly HashSet<Collider> ignored = new HashSet<Collider>();
    // 놓은 뒤 분리를 기다리는 콜라이더 → 복원 마감 시각(Time.time).
    readonly Dictionary<Collider, float> pendingRestore = new Dictionary<Collider, float>();

    void Awake()
    {
        interactor = GetComponent<XRBaseInteractor>();

        if (interactor == null)
            Debug.LogWarning($"[HandGrabCollisionBridge] {name}: 인터랙터를 찾지 못했습니다. " + $"그랩 인터랙터와 같은 GameObject에 붙여주세요.", this);

        if (physicsHand == null)
            Debug.LogWarning($"[HandGrabCollisionBridge] {name}: Physics Hand가 비어 있습니다. " + $"인스펙터에서 이 인터랙터와 같은 쪽 손을 지정해주세요.", this);
    }

    void OnEnable()
    {
        if (interactor == null) return;

        interactor.selectEntered.AddListener(OnSelectEntered);
        interactor.selectExited.AddListener(OnSelectExited);
    }

    void OnDisable()
    {
        if (interactor != null)
        {
            interactor.selectEntered.RemoveListener(OnSelectEntered);
            interactor.selectExited.RemoveListener(OnSelectExited);
        }

        RestoreIgnored(); // 잡은 채 비활성화되어도 무시 상태가 새지 않게 복원
    }

    void OnSelectEntered(SelectEnterEventArgs args)
    {
        if (physicsHand == null) return;

        foreach (Collider col in args.interactableObject.colliders)
        {
            if (col == null) continue;

            // 놓자마자 다시 잡은 경우: 대기 중이던 복원을 취소하고 계속 무시한다.
            pendingRestore.Remove(col);

            if (!ignored.Add(col)) continue; // 이미 무시 중

            physicsHand.IgnoreCollisionWith(col, true);
        }
    }

    void OnSelectExited(SelectExitEventArgs args)
    {
        if (physicsHand == null) return;

        float deadline = Time.time + restoreTimeout;

        foreach (Collider col in args.interactableObject.colliders)
        {
            if (col == null || !ignored.Contains(col)) continue;

            pendingRestore[col] = deadline;
        }
        
        if (pendingRestore.Count > 0)
            StartCoroutine(RestoreWhenSeparated());
    }

    // 손에서 빠져나온 콜라이더부터 하나씩 충돌을 되살린다.
    // 손 위에 얹힌 채 계속 겹쳐 있는 경우를 대비해 마감 시각을 두어 무기한 대기를 막는다.
    IEnumerator RestoreWhenSeparated()
    {
        List<Collider> separated = new List<Collider>();

        while (pendingRestore.Count > 0)
        {
            yield return new WaitForFixedUpdate();

            separated.Clear();
            foreach (KeyValuePair<Collider, float> entry in pendingRestore)
            {
                if (entry.Key == null || Time.time >= entry.Value || !physicsHand.OverlapsWith(entry.Key))
                    separated.Add(entry.Key);
            }

            foreach (Collider col in separated)
            {
                pendingRestore.Remove(col);
                ignored.Remove(col);

                if (physicsHand != null)
                    physicsHand.IgnoreCollisionWith(col, false);
            }
        }
    }

    void RestoreIgnored()
    {
        StopAllCoroutines();

        if (physicsHand != null)
        {
            foreach (Collider col in ignored)
                physicsHand.IgnoreCollisionWith(col, false);
        }

        ignored.Clear();
        pendingRestore.Clear();
    }
}
