/// <summary>
/// StartScene에서 고른 Single / Multi 선택값을 Opening이 끝날 때까지 들고 있는 보관소.
///
/// 흐름:
///   StartScene의 Single/Multi 버튼 → ConnectionManager가 여기에 Set()
///   → Opening을 일반 SceneManager로 로드 (Fusion 세션은 아직 시작하지 않는다)
///   → Opening 연출이 끝난 뒤 이 값을 읽어 NetworkManager.EnterSession()을 호출
///
/// 왜 static인가:
/// Opening은 Fusion 세션 시작 전의 순수 로컬 씬이고, 이 값은 "아직 쓰지 않은 요청"일 뿐이다.
/// MonoBehaviour나 DontDestroyOnLoad 오브젝트를 하나 더 만들 이유가 없다.
/// 이미 영속인 NetworkManager를 수정하지 않기 위해서도 별도 보관소로 둔다.
///
/// 수명:
/// 플레이 세션 동안만 유효하다. 에디터에서는 도메인 리로드로 초기화되고,
/// 런타임에서는 EnterSession을 호출한 쪽이 Clear()로 비운다.
/// </summary>
public static class OpeningSessionRequest
{
    /// <summary>
    /// StartScene에서 고른 세션 이름.
    ///
    /// Single은 "Solo_" + 기기 고유 ID다.
    /// Multi는 <c>null</c>이다 — 고정된 방 이름을 쓰지 않고 Fusion 자동매칭에 맡긴다.
    /// NetworkManager.EnterSession이 <c>SessionName = null</c>로 넘겨
    /// MatchmakingMode.FillRoom으로 빈 방부터 채우기 때문이다.
    /// 따라서 이 값이 null인 것은 정상이며 "요청이 없다"는 뜻이 아니다.
    /// </summary>
    public static string RoomCode { get; private set; }

    /// <summary>StartScene에서 어느 버튼으로 들어왔는지.</summary>
    public static SessionMode Mode { get; private set; } = SessionMode.Multi;

    /// <summary>
    /// StartScene의 Single/Multi 버튼을 거쳐 들어왔는지.
    ///
    /// 판정 기준은 <see cref="Set"/> 가 호출되었는지 **하나**다.
    /// RoomCode가 비었는지로 판정하면 안 된다 — Multi는 자동매칭이라 RoomCode가
    /// 정상적으로 null이고, 그러면 Multi로 들어온 플레이어가 Opening 끝에서
    /// "세션 요청이 없다"로 막혀 Lobby로 가지 못한다.
    ///
    /// Opening을 버튼 없이 직접 실행하는 경우는 그대로 걸러진다.
    /// 그때는 Set이 한 번도 불리지 않아 이 값이 기본값 false로 남는다.
    /// </summary>
    public static bool HasRequest { get; private set; }

    /// <summary>StartScene의 버튼 처리에서 호출한다.</summary>
    public static void Set(string roomCode, SessionMode mode)
    {
        RoomCode = roomCode;
        Mode = mode;

        // roomCode가 null이어도 유효한 요청이다. (Multi = Fusion 자동매칭)
        HasRequest = true;
    }

    /// <summary>EnterSession으로 소비한 뒤 호출한다.</summary>
    public static void Clear()
    {
        RoomCode = null;
        Mode = SessionMode.Multi;
        HasRequest = false;
    }
}
