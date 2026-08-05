using System.Collections;
using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// InteractablePlant1/2/3 용 진동 스크립트.
/// 오른손 컨트롤러가 닿아 있는 동안, 인스펙터에서 입력한 리듬(vibrationPattern)대로
/// 오른손에 햅틱 진동을 반복 재생한다. 딸린 UI/Canvas 는 없다.
///
/// - 접촉 감지 방식은 LivingRoot 를 그대로 따른다("Right Controller" 태그 필터).
/// - 리듬/펄스 재생 방식은 VibrationTriggerMission.PlayVibrationPattern 을 그대로 따른다.
/// - 이 오브젝트의 Collider 는 isTrigger = true 여야 트리거가 감지된다.
/// </summary>
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

    [Header("효과음 (선택)")]
    [Tooltip("각 펄스마다 재생할 효과음 (없으면 진동만)")]
    [SerializeField] private AudioSource pulseAudioSource;
    [SerializeField] private AudioClip pulseClip;

    private Coroutine runningRoutine;
    private bool isTouching;

    private void OnTriggerEnter(Collider other)
    {
        // 미션 등에서 비활성화되면 진동을 시작하지 않는다.
        if (!enabled)
            return;

        // 오른손 컨트롤러만 반응 (LivingRoot 와 동일한 태그 필터)
        if (!other.CompareTag("Right Controller"))
            return;

        isTouching = true;
        if (runningRoutine == null)
            runningRoutine = StartCoroutine(PlayLoop());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Right Controller"))
            return;

        // 진행 중인 리듬은 끝까지 재생한 뒤 멈춘다.
        isTouching = false;
    }

    private void OnDisable()
    {
        // 비활성화되면 즉시 정지
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

    // VibrationTriggerMission.PlayVibrationPattern 과 동일한 규칙으로 패턴을 재생한다.
    //  ●        → 진동 펄스 1회
    //  ●● (붙음) → 짧은 쉼(shortGap) 뒤 다음 펄스
    //  공백      → 다음 펄스는 긴 쉼(longGap) 뒤에
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

    // 오른손 컨트롤러에 진동 펄스 1회 + (선택) 효과음 재생. (LivingRoot / VibrationTriggerMission 와 동일)
    private void SendPulse()
    {
        if (pulseAudioSource != null && pulseClip != null)
            pulseAudioSource.PlayOneShot(pulseClip);

        InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.TryGetHapticCapabilities(out HapticCapabilities caps) && caps.supportsImpulse)
            device.SendHapticImpulse(0u, amplitude, pulseDuration);
    }
}
