using ReBloom.Water;
using UnityEngine;
using UnityEngine.Events;

namespace ReBloom.Electricity
{
    /// <summary>
    /// 전기 코어 미션이 클리어되면 연결된 연출을 한 번 재생한다(물 차오름 등).
    ///
    /// 새 네트워크 상태를 만들지 않는다 — 진실 공급원은 <see cref="ElectricityCoreMission.IsClear"/>
    /// 하나뿐이고, 각 피어가 <see cref="ElectricityCoreMission.Cleared"/>를 받아 로컬에서 재생한다.
    /// <see cref="PlantCoreFlowerBloom"/>과 같은 구조다.
    ///
    /// 배치: <b>항상 켜져 있는 오브젝트</b>에 붙인다. 꺼진 오브젝트에 붙이면 구독이 안 돼 클리어를 놓친다.
    /// </summary>
    [AddComponentMenu("ReBloom/Electricity Clear Trigger")]
    public class ElectricityClearTrigger : MonoBehaviour
    {
        [Tooltip("클리어를 지켜볼 미션. 비우면 씬에서 찾는다(프리팹에서는 씬 참조를 못 걸기 때문).")]
        [SerializeField] ElectricityCoreMission mission;

        [Tooltip("클리어 순간 Play()할 수면 연출.")]
        [SerializeField] RisingWater[] risingWaters;

        [Tooltip("그 밖에 클리어 순간 부를 것.")]
        [SerializeField] UnityEvent onCleared;

        bool played;

        void Awake()
        {
            if (mission == null)
                mission = FindFirstObjectByType<ElectricityCoreMission>();

            if (mission == null)
                Debug.LogWarning($"[ElectricityClearTrigger] {name}: ElectricityCoreMission을 찾지 못했습니다. " +
                                 $"클리어 연출이 재생되지 않습니다.", this);
        }

        void OnEnable() => ElectricityCoreMission.Cleared += OnCleared;

        void OnDisable() => ElectricityCoreMission.Cleared -= OnCleared;

        void Start()
        {
            // 이미 클리어된 뒤에 켜졌으면 바로 재생한다.
            // (보통은 미션의 Update가 늦게 들어온 피어에게도 Cleared를 쏴주므로 이 경로는 드물다)
            if (mission != null && mission.IsSpawned && mission.IsClear)
                OnCleared();
        }

        void OnCleared()
        {
            if (played) return;
            played = true;

            if (risingWaters != null)
                foreach (RisingWater water in risingWaters)
                    if (water != null) water.Play();

            onCleared?.Invoke();
        }

        [ContextMenu("테스트: 클리어 연출 재생 (Play 모드)")]
        void DebugPlay()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"[ElectricityClearTrigger] {name}: Play 모드에서만 재생됩니다.", this);
                return;
            }

            played = false;
            OnCleared();
        }
    }
}
