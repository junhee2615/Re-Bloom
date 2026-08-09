using Fusion;

// 로컬 플레이어가 Host 인지 Client 인지 판별하는 헬퍼

public static class PlayerRole
{
    private static NetworkRunner Runner
    {
        get
        {
            var nm = NetworkManager.Instance;
            if (nm == null || nm.Runner == null || !nm.Runner.IsRunning)
                return null;
            return nm.Runner;
        }
    }

    /// <summary>네트워크 세션이 실행 중인지.</summary>
    public static bool IsConnected => Runner != null;

    /// <summary>로컬 플레이어 PlayerId (미연결이면 -1).</summary>
    public static int LocalPlayerId
    {
        get
        {
            var runner = Runner;
            return runner != null ? runner.LocalPlayer.PlayerId : -1;
        }
    }

    /// <summary>로컬 플레이어가 Host(PlayerId 1)인가.</summary>
    public static bool LocalIsHost() => LocalPlayerId == 1;

    /// <summary>로컬 플레이어가 Client(PlayerId 2 이상)인가.</summary>
    public static bool LocalIsClient() => LocalPlayerId >= 2;
}
