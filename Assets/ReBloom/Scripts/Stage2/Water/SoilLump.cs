using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// 손이 그립하면 그 손에 작은 흙 조각(<see cref="SoilObstacle"/>)을 스폰해 들려주고,
/// 덩이를 한 단계 축소한다. pieceCount 만큼 다 떠내면 덩이를 Despawn 한다.
///
/// "호스트 권위":
///  - <see cref="XRGrabInteractable"/> 은 "그립 감지 전용"(trackPosition/Rotation off) — 안 움직인다.
///  - 그립/해제를 RPC 로 호스트에 알린다.
///  - 스폰·배정·축소·디스폰은 모두 호스트(StateAuthority)에서만.
///  - 스폰된 조각은 호스트가 그 손을 grabber 로 배정해(<see cref="WaterMissionObstacle.HostAssignGrabber"/>)
///    손을 따라오게 하고, 그립을 놓으면 배정을 해제해 던져지게 한다.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class SoilLump : NetworkBehaviour
{
    [Header("조각")]
    [Tooltip("그립할 때 손에 스폰할 작은 흙 조각")]
    [SerializeField] private NetworkObject piecePrefab;

    [Tooltip("이 덩이에서 떠낼 수 있는 조각 수. 다 떠내면 덩이가 사라진다.")]
    [SerializeField] private int pieceCount = 3;

    [Tooltip("남은 조각 비율만큼 덩이를 축소한다(다 떠내면 Despawn).")]
    [SerializeField] private bool shrinkWithPieces = true;

    // 남은 조각 수(네트워크 공유). 클라이언트는 이 값으로 축소를 따라 그린다.
    [Networked] private int PiecesRemaining { get; set; }

    private Vector3 baseScale;
    private XRGrabInteractable grab;

    // 호스트 전용: 현재 그립 중인 (player,hand) → 스폰된 조각. 해제/디스폰 매칭용.
    private struct GripEntry { public PlayerRef player; public bool isLeft; public WaterMissionObstacle piece; }
    private readonly List<GripEntry> activeGrips = new List<GripEntry>();

    public override void Spawned()
    {
        baseScale = transform.localScale;

        grab = GetComponent<XRGrabInteractable>();
        if (grab != null)
        {
            grab.trackPosition = false;
            grab.trackRotation = false;
            grab.throwOnDetach = false;
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }

        if (HasStateAuthority)
            PiecesRemaining = pieceCount;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (grab != null)
        {
            grab.selectEntered.RemoveListener(OnSelectEntered);
            grab.selectExited.RemoveListener(OnSelectExited);
        }
    }

    // ---------- 로컬 그립 감지 → 호스트에 요청 ----------

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        bool isLeft = args.interactorObject.handedness == InteractorHandedness.Left;
        RPC_Grip(Runner.LocalPlayer, isLeft);
    }

    private void OnSelectExited(SelectExitEventArgs args)
    {
        bool isLeft = args.interactorObject.handedness == InteractorHandedness.Left;
        RPC_Release(Runner.LocalPlayer, isLeft);
    }

    // ---------- 호스트 처리 ----------

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Grip(PlayerRef player, NetworkBool isLeft)
    {
        if (PiecesRemaining <= 0 || piecePrefab == null) return;
        if (!TryGetHand(player, isLeft, out Vector3 handPos, out Quaternion handRot)) return;

        // 손 위치에 조각을 스폰하고, 그 손이 잡은 것으로 배정 → 손을 따라온다.
        NetworkObject pieceObj = Runner.Spawn(piecePrefab, handPos, handRot);
        WaterMissionObstacle piece = pieceObj != null ? pieceObj.GetComponent<WaterMissionObstacle>() : null;
        piece?.HostAssignGrabber(player, isLeft);

        activeGrips.Add(new GripEntry { player = player, isLeft = isLeft, piece = piece });

        PiecesRemaining--;
    }

    [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
    private void RPC_Release(PlayerRef player, NetworkBool isLeft)
    {
        for (int i = activeGrips.Count - 1; i >= 0; i--)
        {
            GripEntry g = activeGrips[i];
            if (g.player != player || g.isLeft != isLeft) continue;

            // 배정 해제 → 조각의 OnReleased(던지기)가 실행된다.
            if (g.piece != null)
                g.piece.HostReleaseGrabber(player, isLeft);
            activeGrips.RemoveAt(i);
            break;
        }

        // 다 떠냈고 잡고 있는 손도 없으면 덩이 제거.
        if (PiecesRemaining <= 0 && activeGrips.Count == 0)
            Runner.Despawn(Object);
    }

    // ---------- 축소 표현 ----------

    // 매 렌더 프레임 돈다.
    // 남은 조각 비율로 축소를 그리기
    public override void Render()
    {
        if (shrinkWithPieces)
            transform.localScale = baseScale * RemainingFraction();
    }

    private float RemainingFraction()
        => pieceCount > 0 ? Mathf.Clamp01((float)PiecesRemaining / pieceCount) : 0f;

    private bool TryGetHand(PlayerRef p, NetworkBool isLeft, out Vector3 pos, out Quaternion rot)
    {
        pos = default; rot = Quaternion.identity;
        if (p == PlayerRef.None) return false;
        if (!NetworkPlayer.TryGet(p, out var np)) return false;

        Transform hand = isLeft ? np.LeftHand : np.RightHand;
        if (hand == null) return false;

        pos = hand.position;
        rot = hand.rotation;
        return true;
    }
}
