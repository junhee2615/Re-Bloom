using ReBloom.Electricity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ReBloom.Rendering
{
    /// <summary>
    /// 스테이지 씬 안에서 미션 완료를 감지해 복원 전 색감 필터를 끈다.
    /// <see cref="RestorationColorSceneController"/>가 런타임에 만들며, 씬이 언로드되면 함께 사라진다.
    ///
    /// 완료 판정은 각 피어에 복제되는 Networked 상태를 직접 읽는다. 이벤트는 빠른 경로로만 쓴다.
    /// - 완료 전 상태를 한 번이라도 본 뒤 완료되면 3초 페이드(Host/Client 각자 로컬에서 실행).
    /// - 처음 확인한 상태가 이미 완료면 늦게 들어온 것이므로 즉시 끈다(페이드하지 않음).
    /// 미션 판정·컷신 로직은 읽기만 하고 바꾸지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    internal sealed class RestorationColorStageTracker : MonoBehaviour
    {
        private const float MissionSearchInterval = 0.5f;

        private RestorationColorStage stage;
        private Scene stageScene;
        private bool initialized;

        private LeverMissionManager stage1Mission;
        private ElectricityCoreMission stage3Mission;
        private float nextMissionSearchTime;

        // Networked 상태가 준비된 뒤 "아직 완료 전"을 한 번이라도 확인했는가.
        private bool observedBeforeClear;
        private bool resolved;

        public void Initialize(RestorationColorStage activeStage, Scene scene)
        {
            stage = activeStage;
            stageScene = scene;
            initialized = true;
        }

        private void OnEnable()
        {
            LeverMissionManager.MissionCleared += OnStage1MissionCleared;
            ElectricityCoreMission.Cleared += OnStage3MissionCleared;
        }

        private void OnDisable()
        {
            LeverMissionManager.MissionCleared -= OnStage1MissionCleared;
            ElectricityCoreMission.Cleared -= OnStage3MissionCleared;
        }

        private void Update()
        {
            if (!initialized || resolved)
                return;

            // Networked 상태가 아직 준비되지 않았으면(러너 시작 전, 스폰 전) 필터를 유지한 채 기다린다.
            if (!TryReadClearState(out bool cleared))
                return;

            if (!cleared)
            {
                observedBeforeClear = true;
                return;
            }

            Resolve();
        }

        private void OnStage1MissionCleared()
        {
            if (initialized && stage == RestorationColorStage.Stage1)
                Resolve();
        }

        private void OnStage3MissionCleared()
        {
            if (initialized && stage == RestorationColorStage.Stage3)
                Resolve();
        }

        private void Resolve()
        {
            if (resolved)
                return;

            resolved = true;

            if (observedBeforeClear)
                RestorationColorSceneController.FadeOutAll();
            else
                RestorationColorSceneController.DeactivateAll();

            enabled = false;
        }

        private bool TryReadClearState(out bool cleared)
        {
            cleared = false;

            if (stage == RestorationColorStage.Stage1)
            {
                if (stage1Mission == null && !TryFindMission(ref stage1Mission))
                    return false;

                if (stage1Mission.Object == null || !stage1Mission.Object.IsValid)
                    return false;

                cleared = stage1Mission.IsMissionClear || stage1Mission.IsRestoreApplied;
                return true;
            }

            if (stage3Mission == null && !TryFindMission(ref stage3Mission))
                return false;

            // IsSpawned 전에 Networked 프로퍼티를 읽으면 예외가 난다.
            if (!stage3Mission.IsSpawned ||
                stage3Mission.Object == null ||
                !stage3Mission.Object.IsValid)
            {
                return false;
            }

            cleared = stage3Mission.IsClear;
            return true;
        }

        private bool TryFindMission<T>(ref T mission) where T : MonoBehaviour
        {
            if (Time.unscaledTime < nextMissionSearchTime)
                return false;

            nextMissionSearchTime = Time.unscaledTime + MissionSearchInterval;

            T[] candidates = FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (T candidate in candidates)
            {
                if (candidate.gameObject.scene.handle != stageScene.handle)
                    continue;

                mission = candidate;
                return true;
            }

            return false;
        }
    }
}
