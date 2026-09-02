using System;

public static class RoleManager
{
    private static Role _localRole;

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
