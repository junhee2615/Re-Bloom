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
        [Tooltip("두 역할이 모두 레버를 잡고 있어야 박자가 돈다. 한 명이라도 놓으면 멈추고 처음부터다.")]
        public bool requireBothHolding = true;

        [Tooltip("둘 다 잡은 뒤 박자가 시작되기까지의 뜸(초). 잡자마자 첫 박이 와서 놓치는 것을 막는다.")]
        [Min(0f)] public float startDelay = 2f;

        [Header("성공 조건")]
        [Tooltip("몇 번 맞춰야 클리어인가.")]
        [Min(1)] public int requiredSuccesses = 3;

        [Tooltip("두 사람이 당긴 tick 차이가 이 시간(초) 안이어야 '함께 당겼다'로 본다.")]
        [Min(0.05f)] public float syncWindow = 0.4f;

        /// <summary>박자가 돌고 있는가(= 미션 진행 중). 호스트가 정하고 복제된다.</summary>
        [Networked] public NetworkBool IsRunning { get; set; }

        /// <summary>박자의 0점(초). 둘 다 잡은 시각 + <see cref="startDelay"/>.</summary>
        [Networked] public float RunStartTime { get; set; }

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

        /// <summary>박자가 시작되기를 기다리는 중인가(둘 다 잡았지만 아직 뜸). 연출이 읽는다.</summary>
        public bool IsArming => IsRunning && Runner != null && Runner.SimulationTime < RunStartTime;

        ElectricityPulseClock clock;

        // 호스트가 들고 있는 "아직 짝을 못 만난 보고". 역할별로 하나씩만 기억한다.
        int mentalPullTick = -1;
        int earPullTick = -1;

        // 호스트가 보는 현재 레버 점유 상태.
        bool mentalHolding;
        bool earHolding;

        int raisedSuccesses;   // 로컬에서 Succeeded를 몇 번까지 쏘았는가
        bool raisedClear;

        void Awake() => clock = GetComponent<ElectricityPulseClock>();

        /// <summary>
        /// 레버를 잡거나 놓았다고 보고한다. <see cref="ElectricityLever"/>가 부른다.
        /// 둘 다 잡혀 있는 동안에만 박자가 돈다 — 한 명이라도 놓으면 멈추고 다음에 처음부터다.
        /// </summary>
        public void ReportHold(Role role, bool holding)
        {
            if (Runner == null || IsClear) return;

            RPC_ReportHold((int)role, holding);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        void RPC_ReportHold(int role, NetworkBool holding)
        {
            if (!HasStateAuthority || IsClear) return;

            if ((Role)role == Role.mental) mentalHolding = holding;
            else if ((Role)role == Role.ear) earHolding = holding;

            bool ready = !requireBothHolding || (mentalHolding && earHolding);

            if (ready && !IsRunning)
            {
                IsRunning = true;
                RunStartTime = Runner.SimulationTime + startDelay;
            }
            else if (!ready && IsRunning)
            {
                // 중간에 놓으면 멈춘다. 모아둔 보고도 버려 다음 시도가 깨끗하게 시작되도록.
                IsRunning = false;
                mentalPullTick = -1;
                earPullTick = -1;
            }
        }

        // 복제된 상태를 시계에 그대로 물린다. 모든 피어가 같은 0점을 쓰므로 박이 어긋나지 않는다.
        void LateUpdate()
        {
            if (clock == null || Runner == null) return;

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
            if (Runner == null || IsClear || !clock.Running) return;

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

            // ② 그 순간이 실제로 겹친 때였는가 — 늦게 온 쪽 기준으로 본다.
            //    시계의 박은 RunStartTime이 0점이므로 경과 시간으로 환산해서 물어야 한다.
            int judgeTick = Mathf.Max(mentalPullTick, earPullTick);
            double elapsed = TickToTime(judgeTick) - RunStartTime;
            bool onBeat = clock.BeatGap(elapsed) <= clock.convergeWindow;

            mentalPullTick = -1;
            earPullTick = -1;

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
