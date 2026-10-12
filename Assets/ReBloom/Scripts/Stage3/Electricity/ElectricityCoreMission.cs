using System;
using Fusion;
using UnityEngine;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어 미션(Stage3 Mission3)의 판정. 두 사람이 각자 레버 앞에 서서,
    /// 빛과 진동이 겹치는 순간(<see cref="ElectricityPulseClock.IsConverged"/>)에 함께 당기면 한 번 성공이다.
    /// <see cref="requiredSuccesses"/>회 성공하면 클리어.
    ///
    /// 판정은 <b>StateAuthority만</b> 한다. 성공·클리어는 이산 상태라 피어마다 갈리면 영구히 어긋난다.
    ///
    /// 배치: <see cref="ElectricityPulseClock"/>과 같은 GameObject(= NetworkObject 하나).
    /// </summary>
    /// <remarks>
    /// <b>판정은 "호스트가 보고를 받은 시각"이 아니라 "당긴 사람의 tick"으로 한다.</b>
    /// 받은 시각으로 재면 핑이 높은 쪽이 일방적으로 손해를 본다 — 창이 0.25초인데 왕복 지연이
    /// 그만큼 먹기 때문이다. 각 피어가 자기가 당긴 tick을 찍어 보내고 호스트는 그 숫자만 비교한다.
    /// </remarks>
    [AddComponentMenu("ReBloom/Electricity Core Mission")]
    [RequireComponent(typeof(ElectricityPulseClock))]
    public class ElectricityCoreMission : NetworkBehaviour
    {
        [Header("시작 조건")]
        [Tooltip("처음 누군가 레버를 잡은 뒤 박자가 시작되기까지의 뜸(초). 잡자마자 첫 박이 와서 놓치는 것을 막는다.")]
        [Min(0f)] public float startDelay = 2f;

        [Header("성공 조건")]
        [Tooltip("몇 번 맞춰야 클리어인가.")]
        [Min(1)] public int requiredSuccesses = 1;

        [Tooltip("두 사람이 당긴 tick 차이가 이 시간(초) 안이어야 '함께 당겼다'로 본다.")]
        [Min(0.05f)] public float syncWindow = 0.4f;

        [Header("디버그")]
        [Tooltip("스폰·잡기·판정을 콘솔에 찍는다. 미션이 안 돌 때 '스폰은 됐는가'부터 가른다.")]
        public bool logToConsole;

        /// <summary>박자가 돌고 있는가. <b>한 명만 잡아도</b> 돈다. 호스트가 정하고 복제된다.</summary>
        [Networked] public NetworkBool IsRunning { get; set; }

        /// <summary>박자의 0점(초). <b>처음</b> 누군가 잡은 시각 + <see cref="startDelay"/>.</summary>
        [Networked] public float RunStartTime { get; set; }

        /// <summary>mental이 레버를 잡고 있는가. 연출이 "내 신호를 내보낼지" 판단할 때 읽는다.</summary>
        [Networked] public NetworkBool MentalHolding { get; set; }

        /// <summary>ear가 레버를 잡고 있는가.</summary>
        [Networked] public NetworkBool EarHolding { get; set; }

        /// <summary>
        /// 스폰되었는가. <b>Networked 프로퍼티는 이게 true일 때만 읽을 수 있다</b> —
        /// 아니면 접근하는 순간 예외가 난다. 씬의 NetworkObject가 베이크되지 않았거나
        /// 러너보다 씬이 먼저 뜨면 실제로 겪는 상태이므로, 조용히 꺼져 있어야 한다.
        /// </summary>
        public bool IsSpawned { get; private set; }

        /// <summary>그 역할이 지금 레버를 잡고 있는가.</summary>
        public bool IsHeldBy(Role role)
        {
            if (!IsSpawned) return false;

            if (role == Role.mental) return MentalHolding;
            if (role == Role.ear) return EarHolding;

            return MentalHolding || EarHolding;
        }

        /// <summary>미션이 끝났는가. 호스트가 정하고 모든 피어에 복제된다.</summary>
        [Networked] public NetworkBool IsClear { get; set; }

        /// <summary>지금까지 맞춘 횟수. 연출·UI가 읽는다.</summary>
        [Networked] public int SuccessCount { get; set; }

        /// <summary>한 번 맞춘 순간. 연출이 구독한다.</summary>
        public static event Action<int> Succeeded;

        /// <summary>겹치지 않은 때 당겼을 때. 실패 연출이 구독한다.</summary>
        public static event Action Failed;

        /// <summary>클리어된 순간 한 번.</summary>
        public static event Action Cleared;

        /// <summary>박자가 시작되기를 기다리는 중인가(잡았지만 아직 뜸). 연출이 읽는다.</summary>
        public bool IsArming => IsSpawned && IsRunning && Runner.SimulationTime < RunStartTime;

        ElectricityPulseClock clock;

        // 호스트가 들고 있는 "아직 짝을 못 만난 보고". 역할별로 하나씩만 기억한다.
        int mentalPullTick = -1;
        int earPullTick = -1;

        int raisedSuccesses;   // 로컬에서 Succeeded를 몇 번까지 쏘았는가
        bool raisedClear;

        void Awake() => clock = GetComponent<ElectricityPulseClock>();

        public override void Spawned()
        {
            IsSpawned = true;

            if (logToConsole)
                Debug.Log($"[전기코어] Spawned — 권한={HasStateAuthority}, " +
                          $"내역할={(RoleManager.HasLocalRole ? RoleManager.LocalRole.ToString() : "미정")}", this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            base.Despawned(runner, hasState);

            IsSpawned = false;
        }

        /// <summary>
        /// 레버를 잡거나 놓았다고 보고한다. <see cref="ElectricityLever"/>가 부른다.
        /// <b>한 명만 잡아도 박자는 돈다</b> — 다만 신호는 잡은 사람에게만 간다.
        /// </summary>
        public void ReportHold(Role role, bool holding)
        {
            if (!IsSpawned || IsClear) return;

            RPC_ReportHold((int)role, holding);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ReportHold(int role, NetworkBool holding)
        {
            if (!HasStateAuthority || IsClear) return;

            if ((Role)role == Role.mental) MentalHolding = holding;
            else if ((Role)role == Role.ear) EarHolding = holding;

            // 놓은 사람의 "아직 짝을 못 만난 당김"은 버린다. 안 그러면 다시 잡은 뒤
            // 상대가 당겼을 때 놓기 전의 낡은 tick과 짝지어져 엉뚱한 실패가 난다.
            if (!holding)
            {
                if ((Role)role == Role.mental) mentalPullTick = -1;
                else if ((Role)role == Role.ear) earPullTick = -1;
            }

            bool anyone = MentalHolding || EarHolding;

            // 아무도 안 잡고 있다가 처음 잡힌 순간에만 0점을 새로 찍는다.
            // 나중에 잡는 사람은 이미 돌고 있는 박에 합류한다 — 그래야 응집 순간이 하나로 같다.
            if (anyone && !IsRunning)
            {
                IsRunning = true;
                RunStartTime = Runner.SimulationTime + startDelay;

                if (logToConsole)
                    Debug.Log($"[전기코어] 박자 시작 예약 — 0점={RunStartTime:F2}s (지금 {Runner.SimulationTime:F2}s)", this);
            }
            else if (!anyone && IsRunning)
            {
                // 둘 다 놓으면 멈춘다. 모아둔 보고도 버려 다음 시도가 깨끗하게 시작되도록.
                IsRunning = false;
                mentalPullTick = -1;
                earPullTick = -1;
            }
        }

        // 복제된 상태를 시계에 그대로 물린다. 모든 피어가 같은 0점을 쓰므로 박이 어긋나지 않는다.
        void LateUpdate()
        {
            if (clock == null) return;

            if (!IsSpawned)
            {
                clock.SetRunning(false, 0d);
                return;
            }

            bool beating = IsRunning && !IsClear && Runner.SimulationTime >= RunStartTime;
            clock.SetRunning(beating, RunStartTime);
        }

        /// <summary>
        /// 레버를 당겼다고 보고한다. <b>레버 쪽 컴포넌트가 로컬에서 당긴 순간</b> 부른다.
        /// 호스트든 클라이언트든 같은 입구를 쓴다.
        /// </summary>
        public void ReportPull(Role role)
        {
            // 박자가 돌기 전(뜸 포함)과 클리어 뒤의 당김은 아무 일도 하지 않는다.
            if (!IsSpawned || IsClear || !clock.Running) return;

            RPC_ReportPull((int)role, Runner.Tick);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ReportPull(int role, int tick)
        {
            if (!HasStateAuthority || IsClear || !IsRunning) return;

            if ((Role)role == Role.mental) mentalPullTick = tick;
            else if ((Role)role == Role.ear) earPullTick = tick;

            JudgeIfBothPulled();
        }

        void JudgeIfBothPulled()
        {
            if (mentalPullTick < 0 || earPullTick < 0) return;

            // ① 두 사람이 충분히 같은 순간에 당겼는가
            float gap = TicksToSeconds(Mathf.Abs(mentalPullTick - earPullTick));
            bool together = gap <= syncWindow;

            // ② 그 순간이 실제로 응집 때였는가 — 늦게 온 쪽 기준으로 본다.
            //    시계의 박은 RunStartTime이 0점이므로 경과 시간으로 환산해서 물어야 한다.
            int judgeTick = Mathf.Max(mentalPullTick, earPullTick);
            double elapsed = TickToTime(judgeTick) - RunStartTime;
            bool onBeat = clock.IsConvergedAt(elapsed);

            mentalPullTick = -1;
            earPullTick = -1;

            if (logToConsole)
                Debug.Log($"[전기코어] 판정 — 간격={gap:F3}s(창 {syncWindow}) 동시={together}, " +
                          $"경과={elapsed:F2}s 응집={onBeat} → {(together && onBeat ? "성공" : "실패")}", this);

            if (together && onBeat) SuccessCount++;
            else RPC_Failed();

            if (SuccessCount < requiredSuccesses) return;

            IsClear = true;
            IsRunning = false;   // 박자를 멈춘다. 클리어 뒤에도 깜빡이면 안 된다.
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        void RPC_Failed() => Failed?.Invoke();

        // 복제로 늦게 도착하는 클라이언트에서도 같은 횟수만큼 쏜다.
        void Update()
        {
            if (!IsSpawned) return;

            while (raisedSuccesses < SuccessCount)
            {
                raisedSuccesses++;
                Succeeded?.Invoke(raisedSuccesses);
            }

            if (IsClear && !raisedClear)
            {
                raisedClear = true;
                Cleared?.Invoke();
            }
        }

        double TickToTime(int tick) => tick * (double)Runner.DeltaTime;

        float TicksToSeconds(int ticks) => ticks * Runner.DeltaTime;

        // 도메인 리로드를 꺼도 static 이벤트가 이전 플레이의 구독자를 물고 있지 않도록.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEvents()
        {
            Succeeded = null;
            Failed = null;
            Cleared = null;
        }
    }
}
