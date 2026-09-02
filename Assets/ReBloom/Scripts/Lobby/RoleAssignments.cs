using System.Collections.Generic;
using Fusion;

/// <summary>
/// Lobby 씬에서 정해진 '플레이어 -> Role' 결과를 담아 두는 Host측 저장소.
///
/// Lobby의 LobbyManager는 씬이 바뀌면 사라지지만 스폰은 Stage 씬에서 일어나므로,
/// PlayerSpawner가 참조할 수 있도록 여기에 남겨 둔다.
/// StateAuthority(Host)만 값을 쓰고, PlayerSpawner도 Host에서만 읽는다.
/// </summary>
public static class RoleAssignments
{
    private static readonly Dictionary<PlayerRef, Role> Map = new Dictionary<PlayerRef, Role>();

    /// <summary>지금까지 확정된 선택이 하나라도 있는지.</summary>
    public static bool HasAny => Map.Count > 0;

    public static void Set(PlayerRef player, Role role)
    {
        Map[player] = role;
    }

    public static bool TryGet(PlayerRef player, out Role role)
    {
        return Map.TryGetValue(player, out role);
    }

    public static void Remove(PlayerRef player)
    {
        Map.Remove(player);
    }

    public static void Clear()
    {
        Map.Clear();
    }
}
