using System;
using Fusion;
using UnityEngine;

namespace ReBloom.Solar
{
    /// <summary>
    /// 태양광 패널 미션(Stage3 Mission1)의 클리어 판정. <see cref="requiredPanels"/>가 모두 수리되면 클리어다.
    ///
    /// 판정은 <b>StateAuthority만</b> 한다. 빔과 수리는 피어마다 따로 계산돼 한쪽만 먼저 고쳐질 수 있으므로,
    /// 호스트가 정하고 <see cref="IsClear"/>로 복제해야 양쪽이 같은 시점에 열린다.
    /// 한 번 클리어되면 되돌아가지 않는다.
    ///
    /// 배치: 씬에 <see cref="NetworkObject"/>와 함께 하나. 클리어를 소비하는 쪽(텔레포터 등)이 이것을 참조한다.
    /// </summary>
    [AddComponentMenu("ReBloom/Solar Mission Manager")]
    public class SolarMissionManager : NetworkBehaviour
    {
        [Tooltip("모두 수리돼야 클리어인 패널들. 비우면 씬에서 시작 시 고장난 패널을 전부 찾는다.")]
        public SolarReflector[] requiredPanels;

        /// <summary>미션이 끝났는가. 호스트가 정하고 모든 피어에 복제된다.</summary>
        [Networked] public NetworkBool IsClear { get; set; }

        /// <summary>클리어된 순간 한 번. 연출·UI가 구독한다.</summary>
        public static event Action Cleared;

        bool raised;   // 로컬에서 Cleared를 이미 쐈는가

        public override void Spawned()
        {
            if (requiredPanels == null || requiredPanels.Length == 0)
                requiredPanels = FindBrokenPanels();
        }

        // 시작 시 고장난 패널만 대상으로 삼는다. 처음부터 멀쩡한 판은 조건에 넣어도 의미가 없다.
        static SolarReflector[] FindBrokenPanels()
        {
            SolarReflector[] all = FindObjectsByType<SolarReflector>(FindObjectsSortMode.None);
            int count = 0;

            for (int i = 0; i < all.Length; i++)
                if (all[i].broken) all[count++] = all[i];

            SolarReflector[] broken = new SolarReflector[count];
            Array.Copy(all, broken, count);
            return broken;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority || IsClear) return;

            for (int i = 0; i < requiredPanels.Length; i++)
            {
                SolarReflector panel = requiredPanels[i];
                if (panel != null && panel.broken) return;
            }

            IsClear = true;
        }

        // 복제로 늦게 도착하는 클라이언트에서도 같은 순간에 한 번 쏜다.
        void Update()
        {
            if (raised || Object == null || !Object.IsValid || !IsClear) return;

            raised = true;
            Cleared?.Invoke();
        }

        // 도메인 리로드를 꺼도 static 이벤트가 이전 플레이의 구독자를 물고 있지 않도록.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetEvent() => Cleared = null;
    }
}
