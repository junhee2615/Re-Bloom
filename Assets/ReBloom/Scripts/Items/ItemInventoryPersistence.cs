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

    /// <summary>
    /// <b>현재 손에 들고 있는</b> 아이템. 사용(소비)하면 지워진다.
    /// 재스폰 시 HeldItem을 되돌리는 데 쓴다.
    /// </summary>
    private static readonly Dictionary<PlayerRef, ItemKind> Held = new Dictionary<PlayerRef, ItemKind>();

    /// <summary>
    /// <b>획득 이력</b>. 한 번 획득되면 소비한 뒤에도 남는다 — 단조 증가다.
    ///
    /// 왜 Held와 분리하는가:
    /// 소비하면 Held는 비워야 하지만(손목 UI·재사용 차단), 월드 아이템 숨김과
    /// 설명 UI 표시는 "이미 가져간 아이템"을 계속 true로 봐야 한다. 둘을 한 통에
    /// 담으면 사용하는 순간 아이템이 월드에 되살아나고 설명 UI가 숨는다.
    /// </summary>
    private static readonly Dictionary<PlayerRef, ItemKind> Acquired = new Dictionary<PlayerRef, ItemKind>();

    /// <summary>
    /// 진행 중인 아이템 효과의 <b>남은 플레이 시간</b>과 누적 정지 시간.
    ///
    /// 아바타가 스테이지마다 재스폰되면 [Networked] 값이 사라지므로, 효과가 전환을 넘어
    /// 이어지려면 여기 남겨야 한다. 남은 시간으로 저장하기 때문에 전환에 걸린 실제 시간과
    /// 무관하게 그대로 이어진다.
    /// </summary>
    private struct EffectState
    {
        public float RemainingSeconds;
        public float PausedSeconds;
    }

    private static readonly Dictionary<PlayerRef, EffectState> Effects =
        new Dictionary<PlayerRef, EffectState>();

    /// <summary>에디터 도메인 리로드 대비 초기화.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        Held.Clear();
        Acquired.Clear();
        Effects.Clear();
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

        if (Held.Count == 0 && Acquired.Count == 0 && Effects.Count == 0)
            return;

        Debug.Log(
            $"[Item] 새 세션 시작 - 보유 {Held.Count}건 / 획득 이력 {Acquired.Count}건 / " +
            $"진행 효과 {Effects.Count}건을 초기화합니다.");

        Held.Clear();
        Acquired.Clear();
        Effects.Clear();
    }

    /// <summary>
    /// 호스트가 획득을 확정했을 때 기록한다. 보유와 획득 이력을 함께 남긴다.
    /// </summary>
    public static void Set(PlayerRef player, ItemKind kind)
    {
        if (kind == ItemKind.None)
        {
            Held.Remove(player);
            return;
        }

        Held[player] = kind;
        Acquired[player] = kind;
    }

    /// <summary>
    /// 호스트가 사용(소비)을 확정했을 때 호출한다.
    /// 보유만 비우고 <b>획득 이력은 남긴다</b> — 월드 아이템이 되살아나지 않게 하는 핵심이다.
    /// </summary>
    public static void MarkConsumed(PlayerRef player)
    {
        Held.Remove(player);
    }

    /// <summary>재스폰 시 되돌릴 <b>현재 보유</b> 아이템.</summary>
    public static bool TryGet(PlayerRef player, out ItemKind kind)
    {
        return Held.TryGetValue(player, out kind);
    }

    /// <summary>재스폰 시 되돌릴 <b>획득 이력</b>. 소비 후에도 값이 남아 있다.</summary>
    public static bool TryGetAcquired(PlayerRef player, out ItemKind kind)
    {
        return Acquired.TryGetValue(player, out kind);
    }

    /// <summary>
    /// 이 아이템을 이미 누군가 가져갔는지. 월드 아이템을 숨길지, 설명 UI를 띄울지 판정할 때 쓴다.
    /// <b>획득 이력</b>을 보므로 사용(소비) 후에도 계속 true다.
    /// </summary>
    public static bool IsKindTaken(ItemKind kind)
    {
        if (kind == ItemKind.None)
            return false;

        foreach (ItemKind value in Acquired.Values)
        {
            if (value == kind)
                return true;
        }

        return false;
    }

    /// <summary>
    /// 진행 중인 효과의 남은 시간을 기록한다. 호스트가 틱마다 갱신한다.
    /// <paramref name="remainingSeconds"/> 가 0 이하면 기록을 지운다.
    /// </summary>
    public static void SetEffectRemaining(PlayerRef player, float remainingSeconds, float pausedSeconds)
    {
        if (remainingSeconds <= 0f)
        {
            Effects.Remove(player);
            return;
        }

        Effects[player] = new EffectState
        {
            RemainingSeconds = remainingSeconds,
            PausedSeconds = pausedSeconds,
        };
    }

    /// <summary>재스폰 시 이어 쓸 남은 효과 시간. 진행 중인 효과가 없으면 false.</summary>
    public static bool TryGetEffect(PlayerRef player, out float remainingSeconds, out float pausedSeconds)
    {
        if (Effects.TryGetValue(player, out EffectState state))
        {
            remainingSeconds = state.RemainingSeconds;
            pausedSeconds = state.PausedSeconds;
            return true;
        }

        remainingSeconds = 0f;
        pausedSeconds = 0f;
        return false;
    }

    /// <summary>세션을 수동으로 끝낼 때(디버그 등). 보통은 StartScene 로드가 알아서 비운다.</summary>
    public static void Clear()
    {
        Held.Clear();
        Acquired.Clear();
        Effects.Clear();
    }
}
