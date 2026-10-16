/// <summary>
/// 캐릭터별 아이템 종류.
///
/// byte 기반인 이유: Fusion의 [Networked] 프로퍼티로 그대로 복제하기 위해서다.
/// (프로젝트의 <see cref="Hand"/> enum과 같은 방식)
/// None = 0 이 기본값이므로 "아무것도 들고 있지 않음"이 자동으로 초기 상태가 된다.
/// </summary>
public enum ItemKind : byte
{
    None = 0,

    /// <summary>에코(ear, 청각 제약) 전용 보청기.</summary>
    HearingAid = 1,

    /// <summary>루멘(mental, 정신 제약) 전용 약통.</summary>
    Medicine = 2,
}

/// <summary>
/// 역할과 아이템의 대응 규칙. 한 곳에만 두어 로컬 판정과 호스트 재검증이 어긋나지 않게 한다.
///
/// 역할 매핑 근거 (ResonanceController.cs:64-65):
///   mental(루멘) → 시각 제약  → 약통
///   ear(에코)   → 청각 제약  → 보청기
/// </summary>
public static class ItemKindRules
{
    /// <summary>이 역할이 쓸 수 있는 아이템. 역할 미지정이면 None.</summary>
    public static ItemKind ForRole(Role role)
    {
        switch (role)
        {
            case Role.ear: return ItemKind.HearingAid;
            case Role.mental: return ItemKind.Medicine;
            default: return ItemKind.None;
        }
    }

    /// <summary>이 역할이 이 아이템을 획득할 수 있는지. 다른 역할의 아이템은 잡을 수는 있어도 획득은 안 된다.</summary>
    public static bool CanAcquire(Role role, ItemKind kind)
    {
        return kind != ItemKind.None && ForRole(role) == kind;
    }

    /// <summary>로그용 한글 이름.</summary>
    public static string DisplayName(ItemKind kind)
    {
        switch (kind)
        {
            case ItemKind.HearingAid: return "보청기";
            case ItemKind.Medicine: return "약통";
            default: return "없음";
        }
    }
}
