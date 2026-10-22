using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// "지금이 정상 플레이 시간인가"를 한 곳에서 판정한다. 아이템 효과 타이머가 이것만 본다.
///
/// 컴포넌트가 아니라 static 헬퍼인 이유:
/// 판정에 필요한 상태를 이미 기존 스크립트들이 들고 있다. 새 오브젝트를 만들면 씬이나
/// 프리팹에 배치·연결해야 하는데, 그러면 Inspector 수동 설정이 생긴다. 여기서는 찾아 읽기만 한다.
///
/// ───────────────────────────────────────────────────────────────────────────
/// 왜 이동 잠금만으로는 부족했는가 (2026-10-19 실기 테스트에서 드러난 버그)
/// ───────────────────────────────────────────────────────────────────────────
/// 처음에는 <see cref="HardwareRig.IsLocomotionLocked"/> 만 봤다. 컷씬 5개가 모두
/// <c>SetLocomotionLocked(true/false)</c> 를 부르는 것을 확인했기 때문이다.
///
/// 그런데 <b>Stage1 레버 미션 클리어 컷씬</b>은 그 호출을 하지 않는다.
/// <c>Stage1XRCutsceneRigFollower</c> 는 XR 리그를 Cinemachine 카메라에 직접 붙여 움직이고,
/// <c>HardwareRig</c> 는 <c>SetTutorialCanvasVisible()</c> 하나만 호출한다
/// (Stage1XRCutsceneRigFollower.cs:322 가 그 파일의 유일한 hardwareRig 사용처).
/// 그래서 그 컷씬 동안 <c>IsLocomotionLocked == false</c> 였고, 타이머가 계속 깎였다.
///
/// 결론: 이동 잠금은 컷씬의 <b>충분조건이 아니다</b>. 그래서 각 컷씬이 노출하는
/// <b>명시적 재생 상태</b>를 함께 본다.
///
/// ───────────────────────────────────────────────────────────────────────────
/// 판정 근거
/// ───────────────────────────────────────────────────────────────────────────
/// ① <b>씬 로딩</b> — <see cref="HardwareRig.IsSceneLoading"/>
///    Fusion의 <c>OnSceneLoadStart</c> ~ <c>OnSceneLoadDone</c> 구간.
///
/// ② <b>이동 잠금</b> — <see cref="HardwareRig.IsLocomotionLocked"/>
///    <c>TrainDepartureManager</c>(Stage1 열차)는 공개 재생 상태가 없고 이 신호만 세운다.
///
/// ③ <b>컷씬 명시 상태</b> — 아래 6개를 폴링한다. <b>하나라도 재생 중이면 정지</b>이므로
///    컷씬이 연속되거나 겹쳐도 마지막이 끝날 때까지 재개되지 않는다.
///      · <c>LeverMissionManager.IsCutscenePlaying</c>  (Stage1, <b>[Networked]</b>)
///      · <c>Stage1XRCutsceneRigFollower.IsPlaying</c>   (Stage1)
///      · <c>Mission1ClearCutscene.IsPlaying</c>         (Stage2)
///      · <c>Stage2SkyCutscene.IsPlaying</c>             (Stage2)
///      · <c>Stage2TeleporterCutscene.IsPlaying</c>      (Stage2)
///      · <c>Stage3RiverCutscene.IsPlaying</c>           (Stage3)
///
/// ④ <b>명시적 보류</b> — <see cref="PushHold"/> / <see cref="PopHold"/> 참조 카운트.
///    위로 잡히지 않는 구간이 생겼을 때의 확장 지점.
///
/// <c>LeverMissionManager.IsCutscenePlaying</c> 이 [Networked]인 것이 특히 중요하다 —
/// 호스트가 자기 로컬 상태만 보고 판정해도 그 값은 복제된 진실이라 두 피어가 일치한다.
///
/// ───────────────────────────────────────────────────────────────────────────
/// 컷씬을 새로 추가할 때
/// ───────────────────────────────────────────────────────────────────────────
/// 새 컷씬이 <c>SetLocomotionLocked</c> 를 부르면 ②로 자동 포착된다. 부르지 않으면
/// <see cref="RegisterProbes"/> 에 한 줄을 추가하거나, 컷씬이 직접 ④를 쓰면 된다.
/// </summary>
public static class ItemEffectClock
{
    /// <summary>명시적 보류를 걸어 둔 수. 0보다 크면 정상 플레이로 보지 않는다.</summary>
    private static int holdCount;

    // 영속 리그 캐시. 씬이 바뀌어도 같은 인스턴스가 살아 있으므로 한 번만 찾으면 된다.
    private static HardwareRig cachedRig;

    // 컷씬 재생 상태를 읽는 함수들. 씬이 바뀔 때마다 다시 수집한다.
    private static readonly List<Func<bool>> cutsceneProbes = new List<Func<bool>>();
    private static bool probesResolved;

    // 마지막으로 로그에 남긴 상태. 변할 때만 출력한다(매 프레임 로그 금지).
    private static bool loggedActive;
    private static bool hasLoggedOnce;

    public static int HoldCount => holdCount;

    /// <summary>
    /// 지금이 정상 플레이 시간인지. 아이템 효과 타이머는 이 값이 true인 동안만 줄어든다.
    ///
    /// 리그를 찾지 못하면 <b>false</b>(= 플레이 중 아님)를 돌려준다.
    /// 판정할 수 없을 때 시간을 깎지 않는 쪽이 안전하다 — 멈춰 있는 경우는
    /// <c>PlayerItemInventory</c> 의 정지 상한 워치독이 결국 풀어 준다.
    /// </summary>
    public static bool IsGameplayActive
    {
        get
        {
            bool active = EvaluateActive();
            LogIfChanged(active);
            return active;
        }
    }

    private static bool EvaluateActive()
    {
        if (holdCount > 0)
            return false;

        HardwareRig rig = ResolveRig();

        if (rig == null)
            return false;

        if (rig.IsSceneLoading || rig.IsLocomotionLocked)
            return false;

        return !IsAnyCutscenePlaying();
    }

    /// <summary>컷씬 하나라도 재생 중인지. 중첩·연속 재생에서 조기 재개를 막는다.</summary>
    private static bool IsAnyCutscenePlaying()
    {
        ResolveProbes();

        for (int i = 0; i < cutsceneProbes.Count; i++)
        {
            if (cutsceneProbes[i]())
                return true;
        }

        return false;
    }

    /// <summary>지금 왜 멈춰 있는지. 로그용.</summary>
    public static string DescribeState()
    {
        HardwareRig rig = ResolveRig();

        if (rig == null)
            return $"리그 없음 (hold={holdCount})";

        ResolveProbes();

        return $"loading={rig.IsSceneLoading}, locomotionLocked={rig.IsLocomotionLocked}, " +
               $"cutscene={IsAnyCutscenePlaying()}, probes={cutsceneProbes.Count}, hold={holdCount}";
    }

    // ── 명시적 보류 ────────────────────────────────────────────────────────

    /// <summary>정상 플레이가 아니라고 표시한다. 반드시 <see cref="PopHold"/> 와 짝을 맞춘다.</summary>
    public static void PushHold() => holdCount++;

    /// <summary>보류를 하나 놓는다. 0 아래로 내려가지 않는다.</summary>
    public static void PopHold()
    {
        if (holdCount > 0)
            holdCount--;
    }

    /// <summary>보류를 전부 비운다. 짝이 맞지 않아 영구 정지된 경우의 탈출구다.</summary>
    public static void ClearHolds() => holdCount = 0;

    // ── 탐색 ───────────────────────────────────────────────────────────────

    private static HardwareRig ResolveRig()
    {
        if (cachedRig != null)
            return cachedRig;

        cachedRig = UnityEngine.Object.FindAnyObjectByType<HardwareRig>(FindObjectsInactive.Include);
        return cachedRig;
    }

    /// <summary>
    /// 컷씬 상태 함수들을 모은다. 씬이 바뀌면 <see cref="OnSceneLoaded"/> 가 무효화한다.
    /// </summary>
    /// <remarks>
    /// 매 프레임 FindObjectsByType을 부르면 비싸므로 씬 단위로 캐시한다.
    /// 컷씬 컴포넌트는 스테이지 씬에 배치돼 있어 씬 로드 때 모으면 충분하다.
    /// 파괴된 컴포넌트는 각 함수가 null 검사로 걸러낸다.
    /// </remarks>
    private static void ResolveProbes()
    {
        if (probesResolved)
            return;

        probesResolved = true;
        cutsceneProbes.Clear();

        RegisterProbes();

        if (cutsceneProbes.Count > 0)
            Debug.Log($"[ItemEffectClock] 컷씬 상태 소스 {cutsceneProbes.Count}개를 찾았습니다.");
    }

    /// <summary>
    /// 폴링할 컷씬을 등록한다. 컷씬을 새로 추가하면 여기에 한 줄 더하면 된다.
    /// </summary>
    private static void RegisterProbes()
    {
        // Stage1 — 레버 미션 클리어 컷씬.
        // IsCutscenePlaying은 [Networked]라 호스트·클라이언트가 같은 값을 본다.
        // 호스트가 두 피어의 완료 보고를 모두 받을 때까지 true로 유지되므로 창이 넓다.
        AddProbes<LeverMissionManager>(c => c.IsCutscenePlaying);

        // Stage1 — 위 컷씬의 실제 재생기. 대기·마무리 구간까지 포함한다.
        AddProbes<Stage1XRCutsceneRigFollower>(c => c.IsPlaying);

        // Stage2
        AddProbes<Mission1ClearCutscene>(c => c.IsPlaying);
        AddProbes<Stage2SkyCutscene>(c => c.IsPlaying);
        AddProbes<Stage2TeleporterCutscene>(c => c.IsPlaying);

        // Stage3
        AddProbes<Stage3RiverCutscene>(c => c.IsPlaying);
    }

    private static void AddProbes<T>(Func<T, bool> read) where T : Component
    {
        T[] found = UnityEngine.Object.FindObjectsByType<T>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (T component in found)
        {
            T captured = component;
            cutsceneProbes.Add(() => captured != null && read(captured));
        }
    }

    // ── 로그 ───────────────────────────────────────────────────────────────

    private static void LogIfChanged(bool active)
    {
        if (hasLoggedOnce && active == loggedActive)
            return;

        hasLoggedOnce = true;
        loggedActive = active;

        Debug.Log($"[ItemEffectClock] IsGameplayActive → {active}  ({DescribeStateRaw()})");
    }

    /// <summary>로그 안에서 쓰는 상태 문자열. <see cref="IsGameplayActive"/> 를 다시 부르지 않는다.</summary>
    private static string DescribeStateRaw()
    {
        HardwareRig rig = ResolveRig();

        if (rig == null)
            return $"리그 없음 (hold={holdCount})";

        return $"loading={rig.IsSceneLoading}, locomotionLocked={rig.IsLocomotionLocked}, " +
               $"cutscene={IsAnyCutscenePlaying()}, probes={cutsceneProbes.Count}, hold={holdCount}";
    }

    // ── 수명 ───────────────────────────────────────────────────────────────

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 다음 조회 때 다시 모은다. 스테이지가 바뀌면 컷씬 컴포넌트도 바뀐다.
        probesResolved = false;
    }

    /// <summary>
    /// 에디터 도메인 리로드와 플레이 재시작 대비 초기화.
    /// static 상태가 이전 플레이 세션의 값을 들고 있으면 타이머가 처음부터 멈춰 있을 수 있다.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        holdCount = 0;
        cachedRig = null;
        cutsceneProbes.Clear();
        probesResolved = false;
        hasLoggedOnce = false;
        loggedActive = false;

        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }
}
