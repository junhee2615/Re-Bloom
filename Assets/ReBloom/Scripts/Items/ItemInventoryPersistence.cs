using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 스테이지 전환을 넘겨 아이템 보유 상태를 들고 있는 보관소.
///
/// 왜 필요한가:
/// 플레이어 아바타는 스테이지마다 새로 스폰된다. NetworkSceneManagerDefault의
/// DestroySpawnedPrefabsOnSceneUnload가 1이라 씬이 언로드될 때 아바타가 파괴되고,
/// 그 위의 [Networked] 값도 함께 사라진다. 그래서 Stage1에서 획득한 아이템을
/// Stage2·Stage3까지 들고 가려면 아바타 밖에 한 벌 더 남겨 둬야 한다.
///
/// 누가 쓰는가:
/// <b>StateAuthority(호스트)만</b> 기록한다. 호스트는 두 플레이어의 PlayerRef를 모두
/// 알고 있고, 재스폰 복원도 호스트가 수행하므로 한쪽에만 두면 충분하고 일관된다.
/// (클라이언트는 자기 상태를 [Networked] 복제로 받는다)
///
/// 언제 비우는가:
/// <b>StartScene이 Single로 로드될 때만</b> 비운다 — 새 게임 세션의 시작점이기 때문이다.
/// 스테이지 전환은 Fusion이 Lobby/Stage1/2/3을 올리므로 여기에 걸리지 않는다.
/// Opening도 StartScene이 아니므로 통과한다. 즉 전환 중 중복 초기화가 일어나지 않는다.
///
/// ResonanceController가 스테이지마다 공명 거리를 다시 재지 않으려고
/// resonanceDistanceResolved 플래그를 쓰는 것과 같은 결의 장치다.
/// </summary>
public static class ItemInventoryPersistence
{
    /// <summary>새 세션의 시작점. 이 씬이 Single로 로드되면 보유 상태를 비운다.</summary>
    private const string NewSessionSceneName = "StartScene";

    private static readonly Dictionary<PlayerRef, ItemKind> Held = new Dictionary<PlayerRef, ItemKind>();

    /// <summary>에디터 도메인 리로드 대비 초기화.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        Held.Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Additive 로드(Fusion의 스테이지 로드, Opening의 Stage 연출)는 세션 시작이 아니다.
        if (mode != LoadSceneMode.Single)
            return;

        if (scene.name != NewSessionSceneName)
            return;

        if (Held.Count == 0)
            return;

        Debug.Log($"[Item] 새 세션 시작 - 보유 아이템 {Held.Count}건을 초기화합니다.");
        Held.Clear();
    }

    /// <summary>호스트가 획득을 확정했을 때 기록한다.</summary>
    public static void Set(PlayerRef player, ItemKind kind)
    {
        if (kind == ItemKind.None)
        {
            Held.Remove(player);
            return;
        }

        Held[player] = kind;
    }

    public static bool TryGet(PlayerRef player, out ItemKind kind)
    {
        return Held.TryGetValue(player, out kind);
    }

    /// <summary>이 아이템을 이미 누군가 가져갔는지. 월드 아이템을 숨길지 판정할 때 쓴다.</summary>
    public static bool IsKindTaken(ItemKind kind)
    {
        if (kind == ItemKind.None)
            return false;

        foreach (ItemKind value in Held.Values)
        {
            if (value == kind)
                return true;
        }

        return false;
    }

    /// <summary>세션을 수동으로 끝낼 때(디버그 등). 보통은 StartScene 로드가 알아서 비운다.</summary>
    public static void Clear()
    {
        Held.Clear();
    }
}
