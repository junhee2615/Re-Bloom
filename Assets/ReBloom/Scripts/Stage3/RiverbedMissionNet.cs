using UnityEngine;

namespace ReBloom.Water
{
    // 맞췄다 라는 것만 서로에게 보고하고 연출은 로컬에서 알아서. 3회 맞추면 클리어

    [AddComponentMenu("ReBloom/Riverbed Mission Net")]
    [DefaultExecutionOrder(-50)]
    public class RiverbedMissionNet : MonoBehaviour
    {
        public static RiverbedMissionNet Instance { get; private set; }

        [Header("연결")]
        public RiverbedFlowController controller;
        public RisingWater risingWater;

        [Header("시작 조건")]
        [Tooltip("두 사람이 각각 서야 하는 발판. 보통 스툴 두 개")]
        public Transform[] standPoints;

        [Tooltip("발판 중심에서 이 거리(m) 안에 있으면 올라선 것으로 본다")]
        public float standRadius = 1.6f;

        [Tooltip("둘 다 선 뒤 첫 물결까지의 뜸 (초)")]
        public float firstWaveDelay = 2.5f;

        [Header("디버그")]
        public bool logToConsole = true;

        [Tooltip("세션 없이 에디터에서 혼자 돌릴 때 사용. 컨트롤러가 단독 모드로 동작한다")]
        public bool soloFallback = true;

        int waveIndex;
        bool waveOpen;
        bool waveIsReal;
        bool mentalHit, earHit;
        float nextWaveTime;
        float waveCloseTime;
        int successCount;
        bool missionStarted, missionComplete;

        bool localStarted;
        bool warnedNoNetwork;

        void Awake()
        {
            Instance = this;
            if (controller == null) controller = GetComponent<RiverbedFlowController>();
            if (risingWater == null) risingWater = GetComponent<RisingWater>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        bool HasSession { get { return NetworkPlayer.LocalInstance != null; } }

        bool IsHost
        {
            get { return NetworkPlayer.LocalInstance != null && NetworkPlayer.LocalInstance.HasNetworkStateAuthority; }
        }

        void Start()
        {
            if (controller != null) controller.networkDriven = true;
        }

        void Update()
        {
            if (controller == null) return;

            if (!HasSession)
            {
                // 세션 밖(에디터 단독) — 컨트롤러가 예전처럼 혼자 돌게 둔다
                if (soloFallback && controller.networkDriven)
                {
                    controller.networkDriven = false;
                    if (logToConsole && !warnedNoNetwork)
                    {
                        warnedNoNetwork = true;
                        Debug.Log("[RiverbedNet] 세션 없음 - 컨트롤러 단독 모드로 전환", this);
                    }
                }
                return;
            }

            if (!controller.networkDriven) controller.networkDriven = true;

            if (IsHost) HostUpdate();
        }

        // ---------------------------------------------------------------
        void HostUpdate()
        {
            if (missionComplete) return;

            if (!missionStarted)
            {
                if (!BothPlayersStanding()) return;

                missionStarted = true;
                nextWaveTime = Time.time + firstWaveDelay;
                Broadcast_Start();
                if (logToConsole) Debug.Log("[RiverbedNet] 두 사람이 발판에 섰다 - 미션 시작", this);
                return;
            }

            // 한 명이라도 내려가면 물결을 멈춘다 (진행도는 유지)
            if (!BothPlayersStanding())
            {
                waveOpen = false;
                nextWaveTime = Time.time + firstWaveDelay;
                return;
            }

            if (!waveOpen && Time.time >= nextWaveTime)
                HostSpawnWave();

            if (waveOpen && Time.time > waveCloseTime)
            {
                waveOpen = false;
                nextWaveTime = Time.time + CurrentInterval;
                if (logToConsole && waveIsReal && !(mentalHit && earHit))
                    Debug.Log("[RiverbedNet] wave #" + waveIndex + " 실패 (mental=" + mentalHit + " ear=" + earHit + ")", this);
            }
        }

        float CurrentInterval
        {
            get
            {
                RiverbedFlowController.RoundSetup r = controller != null ? controller.CurrentRound : null;
                return r != null ? r.interval : 4f;
            }
        }

        void HostSpawnWave()
        {
            RiverbedFlowController.RoundSetup r = controller.CurrentRound;
            if (r == null) return;

            waveIndex++;
            waveIsReal = RiverbedFlowController.Hash01Public(controller.waveSeed + waveIndex) >= r.fakeChance;
            mentalHit = false;
            earHit = false;
            waveOpen = true;

            // 예고 구간 + 여유(ear 의 평탄 구간과 통신 지연)만큼 열어둔다
            waveCloseTime = Time.time + r.approachDuration + 1.2f;

            Broadcast_Wave(waveIndex, waveIsReal, r.approachDuration);
            if (logToConsole)
                Debug.Log("[RiverbedNet] wave #" + waveIndex + " (" + (waveIsReal ? "REAL" : "FAKE") + ") 송출", this);
        }

        bool BothPlayersStanding()
        {
            if (standPoints == null || standPoints.Length == 0) return true;

            NetworkPlayer mental, ear;
            if (!NetworkPlayer.TryGetByRole(Role.mental, out mental)) return false;
            if (!NetworkPlayer.TryGetByRole(Role.ear, out ear)) return false;

            return IsNearAnyStand(mental) && IsNearAnyStand(ear);
        }

        bool IsNearAnyStand(NetworkPlayer p)
        {
            if (p == null) return false;
            Transform t = p.PlayerTransform;
            if (t == null) return false;

            for (int i = 0; i < standPoints.Length; i++)
            {
                if (standPoints[i] == null) continue;
                Vector3 a = t.position;
                Vector3 b = standPoints[i].position;
                float dy = Mathf.Abs(a.y - b.y);
                a.y = 0f; b.y = 0f;
                if (dy < 3f && Vector3.Distance(a, b) <= standRadius) return true;
            }
            return false;
        }

        // 호스트가 클라이언트의 "맞췄다" 보고를 받는 곳 (NetworkPlayer RPC 가 호출)
        public static void HostReceiveHit(int index, Role role)
        {
            if (Instance != null) Instance.HostReceiveHitInternal(index, role);
        }

        void HostReceiveHitInternal(int index, Role role)
        {
            if (!IsHost || !waveOpen || index != waveIndex) return;

            if (role == Role.mental) mentalHit = true;
            else if (role == Role.ear) earHit = true;

            if (logToConsole)
                Debug.Log("[RiverbedNet] wave #" + index + " 보고 " + role + " (mental=" + mentalHit + " ear=" + earHit + ")", this);

            if (!waveIsReal)
            {
                // 페이크를 누른 순간 이번 물결은 끝
                waveOpen = false;
                nextWaveTime = Time.time + CurrentInterval;
                return;
            }

            if (!(mentalHit && earHit)) return;

            // 둘 다 맞췄다
            waveOpen = false;
            successCount++;

            RiverbedFlowController.RoundSetup r = controller.CurrentRound;
            float baseline = r != null ? r.wetBaselineOnSuccess : 0.2f;
            Broadcast_Success(successCount, baseline);

            if (successCount >= controller.rounds.Count)
            {
                missionComplete = true;
                Broadcast_Complete();
                if (logToConsole) Debug.Log("[RiverbedNet] 3회 성공 - 물길이 이어졌다", this);
            }
            else
            {
                nextWaveTime = Time.time + CurrentInterval;
            }
        }

        // 각 피어에서 실행되는 로컬 연출 (NetworkPlayer RPC 가 호출)
        public static void LocalStart()
        {
            if (Instance != null) Instance.LocalStartInternal();
        }

        void LocalStartInternal()
        {
            if (localStarted) return;
            localStarted = true;
            if (controller != null) controller.BeginNetworkMission();
        }

        public static void LocalPlayWave(int index, bool isReal, float approachDuration)
        {
            if (Instance != null && Instance.controller != null)
                Instance.controller.PlayNetworkWave(index, isReal, approachDuration);
        }

        public static void LocalApplySuccess(int count, float baseline)
        {
            if (Instance != null && Instance.controller != null)
                Instance.controller.ApplyNetworkSuccess(count, baseline);
        }

        public static void LocalComplete()
        {
            if (Instance == null) return;
            if (Instance.controller != null) Instance.controller.ApplyNetworkComplete();
        }

        // 로컬 플레이어가 자기 기준으로 "맞췄다"고 판단했을 때 컨트롤러가 부른다
        public static void ReportLocalHit(int index)
        {
            NetworkPlayer local = NetworkPlayer.LocalInstance;
            if (local == null) return;
            local.RequestRiverbedHit(index, (int)local.AssignedRole);
        }

        void Broadcast_Start()
        {
            NetworkPlayer local = NetworkPlayer.LocalInstance;
            if (local != null) local.Rpc_RiverbedStart();
        }

        void Broadcast_Wave(int index, bool isReal, float approachDuration)
        {
            NetworkPlayer local = NetworkPlayer.LocalInstance;
            if (local != null) local.Rpc_RiverbedWave(index, isReal ? 1 : 0, approachDuration);
        }

        void Broadcast_Success(int count, float baseline)
        {
            NetworkPlayer local = NetworkPlayer.LocalInstance;
            if (local != null) local.Rpc_RiverbedSuccess(count, baseline);
        }

        void Broadcast_Complete()
        {
            NetworkPlayer local = NetworkPlayer.LocalInstance;
            if (local != null) local.Rpc_RiverbedComplete();
        }

        // ---------------------------------------------------------------
        void OnDrawGizmosSelected()
        {
            if (standPoints == null) return;
            Gizmos.color = new Color(0.4f, 1f, 0.6f, 0.8f);
            for (int i = 0; i < standPoints.Length; i++)
                if (standPoints[i] != null)
                    Gizmos.DrawWireSphere(standPoints[i].position, standRadius);
        }
    }
}
