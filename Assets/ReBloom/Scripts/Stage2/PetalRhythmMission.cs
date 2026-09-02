using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class PetalRhythmMission : MonoBehaviour
{
    [Header("식별")]
    [Tooltip("네트워크 복원 동기화용 고유 ID. 플랜트마다 다르게(1,2,3).")]
    [SerializeField] private int plantId = 1;

    [Header("연결")]
    [Tooltip("리듬 소스 (같은 플랜트의 진동 스크립트)")]
    [SerializeField] private InteractablePlantVibration vibration;
    [Tooltip("클리어 시 색을 되돌릴 PlantRevive (연꽃 메시)")]
    [SerializeField] private PlantRevive plantRevive;

    [Header("역할 제한")]
    [Tooltip("체크 시 mental 역할만 연꽃잎 입력이 유효. 솔로 테스트 시 해제.")]
    [FormerlySerializedAs("hostOnly")]
    [SerializeField] private bool mentalOnly = true;

    [Header("박자 판정 (넉넉하게)")]
    [Tooltip("간격 허용 오차(초). 클수록 관대")]
    [SerializeField] private float beatTolerance = 0.35f;
    [Tooltip("각 간격의 이 비율만큼 추가 허용 (0.6 = 60%)")]
    [SerializeField] private float relativeTolerance = 0.6f;
    [Range(0f, 1f)]
    [Tooltip("간격 중 이 비율 이상 맞으면 성공")]
    [SerializeField] private float requiredMatchRatio = 0.6f;
    [Tooltip("입력 사이 무입력이 이만큼 지나면 시퀀스 초기화(초)")]
    [SerializeField] private float betweenInputTimeout = 2.5f;
    [Tooltip("한 번의 터치가 두 콜라이더에서 중복 인식되는 것 방지용 최소 간격(초)")]
    [SerializeField] private float touchDebounce = 0.08f;

    [Header("터치 확인음")]
    [Tooltip("연꽃잎을 터치할 때마다 재생할 효과음")]
    [SerializeField] private AudioClip touchClip;
    [Tooltip("비우면 플랜트 위치에서 임시 재생. 지정하면 이 AudioSource로 재생.")]
    [SerializeField] private AudioSource touchAudioSource;
    [Range(0f, 1f)]
    [SerializeField] private float touchVolume = 1f;

    private readonly List<float> pressTimes = new List<float>();
    private float lastTouchTime = -999f;
    private bool cleared;

    // 복원 여부
    public bool IsCleared => cleared;

    // 연출 트리거
    public static event System.Action Revived;


    // plantId → 인스턴스. 네트워크 RPC(NetworkPlayer.Rpc_RevivePlant)가 id 로 찾아 로컬 복원
    private static readonly Dictionary<int, PetalRhythmMission> _registry =
        new Dictionary<int, PetalRhythmMission>();

    private void OnEnable()
    {
        _registry[plantId] = this;
    }

    private void OnDisable()
    {
        if (_registry.TryGetValue(plantId, out var m) && m == this)
            _registry.Remove(plantId);
    }

    /// 연꽃잎 포워더가 오른손 접촉을 알릴 때 호출. mental 로컬에서만 유효
public void OnPetalTouched()
    {
        Debug.Log("[PetalDebug] " + name + " OnPetalTouched 진입 cleared=" + cleared + " mentalOnly=" + mentalOnly + " LocalIsMental=" + RoleManager.LocalIsMental + " vibNull=" + (vibration == null));

        if (cleared)
        {
            Debug.Log("[PetalDebug] return: cleared");
            return;
        }

        if (mentalOnly && !RoleManager.LocalIsMental)
        {
            Debug.Log("[PetalDebug] return: not mental");
            return;
        }

        if (vibration == null)
        {
            Debug.Log("[PetalDebug] return: vibration null");
            return;
        }

        float now = Time.time;

        if (now - lastTouchTime < touchDebounce)
        {
            Debug.Log("[PetalDebug] return: debounce");
            return;
        }

        if (pressTimes.Count > 0 && now - lastTouchTime > betweenInputTimeout)
            pressTimes.Clear();

        lastTouchTime = now;
        pressTimes.Add(now);

        Debug.Log("[PetalDebug] PlayTouchSound 호출 (pressTimes=" + pressTimes.Count + "/" + vibration.ExpectedPulseCount + ")");
        PlayTouchSound();

        int expectedCount = vibration.ExpectedPulseCount;
        if (expectedCount <= 0)
            return;

        if (pressTimes.Count >= expectedCount)
        {
            bool ok = Judge(vibration.BuildExpectedIntervals(), pressTimes);
            pressTimes.Clear();

            Debug.Log("[PetalDebug] 판정 ok=" + ok);
            if (ok)
                ClearMission();
        }
    }

    // 터치 확인용 오른손 햅틱 펄스 (피드백용, UI 아님)
    // 터치 확인용 효과음 재생 (피드백용, UI 아님)
private void PlayTouchSound()
    {
        if (touchClip == null)
            return;

        // 일부 플랜트의 개별 AudioSource가 동일 설정인데도 런타임에 출력을 안 하는 사례가 있어,
        // 매 터치마다 새 임시 소스로 확실히 재생한다. (플레이어가 만질 땐 플랜트 앞이라 위치도 자연스럽다.)
        AudioSource.PlayClipAtPoint(touchClip, transform.position, touchVolume);
    }


    private void ClearMission()
    {
        if (cleared)
            return;

        cleared = true;

        // 네트워크로 두 플레이어 모두 복원 연출
        if (NetworkPlayer.LocalInstance != null)
            NetworkPlayer.LocalInstance.RequestRevivePlant(plantId);
        else
            ReviveById(plantId);
    }

    /// 플랜트 로컬 복원
    public static void ReviveById(int id)
    {
        if (_registry.TryGetValue(id, out var mission) && mission != null)
            mission.ApplyReviveLocal();
    }

    private void ApplyReviveLocal()
    {
        cleared = true;

        if (plantRevive != null)
            plantRevive.StartRevive();

        // 복원 후에는 줄기 진동을 끔
        if (vibration != null)
            vibration.enabled = false;

        // 완료 연출 코디네이터(PlantClearSequence)가 3개 클리어를 감지하도록 알림
        Revived?.Invoke();
    }

    // 횟수가 맞고, 간격이 requiredMatchRatio 이상 맞으면 성공.
    private bool Judge(List<float> expected, List<float> times)
    {
        if (times.Count != expected.Count + 1)
            return false;

        if (expected.Count == 0)
            return true;   // 펄스 1개면 한 번만 터치하면 성공

        int ok = 0;
        for (int i = 0; i < expected.Count; i++)
        {
            float gap = times[i + 1] - times[i];
            float allow = Mathf.Max(beatTolerance, expected[i] * relativeTolerance);
            if (Mathf.Abs(gap - expected[i]) <= allow)
                ok++;
        }

        return ok >= Mathf.CeilToInt(expected.Count * requiredMatchRatio);
    }


    // 도메인 리로드 비활성화 환경 대비: 재생 세션 시작 전 정적 상태 리셋
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Revived = null;
        _registry.Clear();
    }
}
