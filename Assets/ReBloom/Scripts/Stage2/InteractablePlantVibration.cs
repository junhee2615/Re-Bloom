using System.Collections;
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(Collider))]
public class InteractablePlantVibration : MonoBehaviour
{
    [Header("진동 리듬 (플랜트마다 다르게 입력)")]
    [Tooltip("● = 진동 펄스, 공백 = 긴 쉼.  예: ● ● ●●●  /  ●● ● ●")]
    [SerializeField] private string vibrationPattern = "● ● ●●●";

    [Header("진동 파라미터")]
    [Range(0f, 1f)]
    [Tooltip("진동 세기 (0~1)")]
    [SerializeField] private float amplitude = 0.5f;
    [Tooltip("펄스 하나의 진동 길이(초)")]
    [SerializeField] private float pulseDuration = 0.12f;
    [Tooltip("붙어있는 펄스(●●) 사이 짧은 쉼(초)")]
    [SerializeField] private float shortGap = 0.12f;
    [Tooltip("공백으로 구분된 펄스 사이 긴 쉼(초)")]
    [SerializeField] private float longGap = 0.4f;

    [Header("반복")]
    [Tooltip("손을 대고 있는 동안 리듬을 계속 반복할지")]
    [SerializeField] private bool loopWhileTouching = true;
    [Tooltip("리듬 한 번이 끝나고 다음 반복까지의 쉼(초)")]
    [SerializeField] private float loopGap = 0.6f;

    [Header("역할 제한")]
    [Tooltip("체크 시 Client(PlayerId != 1)만 진동을 느낀다. 솔로 테스트 시 해제.")]
    [SerializeField] private bool clientOnly = true;

    private Coroutine runningRoutine;
    private bool isTouching;

    private void OnTriggerEnter(Collider other)
    {
        // 미션에서 비활성화되면 미션 시작하지 않음
        if (!enabled)
            return;

        // 오른손 컨트롤러만 반응
        if (!other.CompareTag("Right Controller"))
            return;

        // Client(PlayerId != 1)만 진동 느낌
        if (clientOnly && !PlayerRole.LocalIsClient())
            return;

        isTouching = true;
        if (runningRoutine == null)
            runningRoutine = StartCoroutine(PlayLoop());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Right Controller"))
            return;

        // 진행 중인 리듬은 끝까지 재생한 뒤 멈춤
        isTouching = false;
    }

    private void OnDisable()
    {
        // 비활성화되면 진동 즉시 정지
        isTouching = false;
        if (runningRoutine != null)
        {
            StopCoroutine(runningRoutine);
            runningRoutine = null;
        }
    }

    private IEnumerator PlayLoop()
    {
        do
        {
            yield return StartCoroutine(PlayVibrationPattern());

            if (loopWhileTouching && isTouching && loopGap > 0f)
                yield return new WaitForSeconds(loopGap);
        }
        while (loopWhileTouching && isTouching);

        runningRoutine = null;
    }

    // 진동 인스펙터에서 입력 
    private IEnumerator PlayVibrationPattern()
    {
        bool first = true;
        bool pendingLongGap = false;

        foreach (char c in vibrationPattern)
        {
            if (c == '●')
            {
                if (!first)
                    yield return new WaitForSeconds(pendingLongGap ? longGap : shortGap);

                SendPulse();
                yield return new WaitForSeconds(pulseDuration);

                first = false;
                pendingLongGap = false;
            }
            else if (c == ' ')
            {
                pendingLongGap = true;
            }
        }
    }

    // 오른손 컨트롤러에 진동 펄스 
    private void SendPulse()
    {
        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            device.SendHapticImpulse(0u, amplitude, pulseDuration);
    }


    // PetalRhythmMission 판정용 리듬 접근자
    public string Pattern => vibrationPattern;

    public int ExpectedPulseCount
    {
        get
        {
            int n = 0;
            foreach (char c in vibrationPattern)
                if (c == '●') n++;
            return n;
        }
    }

    public System.Collections.Generic.List<float> BuildExpectedIntervals()
    {
        var intervals = new System.Collections.Generic.List<float>();
        bool first = true;
        bool pendingLongGap = false;
        foreach (char c in vibrationPattern)
        {
            if (c == '●')
            {
                if (!first)
                    intervals.Add(pulseDuration + (pendingLongGap ? longGap : shortGap));
                first = false;
                pendingLongGap = false;
            }
            else if (c == ' ')
            {
                pendingLongGap = true;
            }
        }
        return intervals;
    }
}
