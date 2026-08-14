using System;

/// <summary>
/// 이 기기(로컬 플레이어)의 Role을 보관한다.
///
/// - StartScene에서 HostBtn/JoinBtn을 누르는 시점에 결정된다. (ConnectionManager)
/// - 스폰 이후에는 서버가 확정한 NetworkPlayer.AssignedRole 값으로 다시 맞춰진다.
///   (NetworkPlayer.Spawned)
///
/// 기존 PlayerRole(PlayerId 기반) 헬퍼와 병행해서 쓸 수 있고,
/// 추후 미션 분기를 Role 기준으로 옮길 때 이 클래스를 호출하면 된다.
/// </summary>
public static class RoleManager
{
    private static Role _localRole;

    /// <summary>로컬 Role이 정해졌는지. 입장 전/세션 종료 후에는 false.</summary>
    public static bool HasLocalRole { get; private set; }

    /// <summary>
    /// 로컬 플레이어의 Role.
    /// HasLocalRole이 false면 enum 기본값(mental)이 나오므로 반드시 함께 확인할 것.
    /// 확인이 귀찮으면 IsLocalRole()을 쓰는 편이 안전하다.
    /// </summary>
    public static Role LocalRole => _localRole;

    /// <summary>로컬 Role이 새로 정해지거나 바뀔 때 호출된다.</summary>
    public static event Action<Role> LocalRoleChanged;

    /// <summary>로컬 Role을 지정한다.</summary>
    public static void SetLocalRole(Role role)
    {
        bool changed = !HasLocalRole || _localRole != role;

        _localRole = role;
        HasLocalRole = true;

        if (changed)
            LocalRoleChanged?.Invoke(role);
    }

    /// <summary>로컬 Role을 미지정 상태로 되돌린다. (세션 종료 시)</summary>
    public static void ClearLocalRole()
    {
        HasLocalRole = false;
        _localRole = default;
    }

    /// <summary>로컬 플레이어가 해당 Role인가. Role 미지정이면 false.</summary>
    public static bool IsLocalRole(Role role) => HasLocalRole && _localRole == role;

    /// <summary>로컬 플레이어가 mental(= Host로 입장)인가.</summary>
    public static bool LocalIsMental => IsLocalRole(Role.mental);

    /// <summary>로컬 플레이어가 ear(= Client로 입장)인가.</summary>
    public static bool LocalIsEar => IsLocalRole(Role.ear);
}
