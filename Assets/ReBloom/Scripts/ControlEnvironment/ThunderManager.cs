using UnityEngine;
using System.Collections;

[RequireComponent(typeof(AudioSource))]
public class ThunderManager : MonoBehaviour
{
    [Header("천둥 사운드 6개")]
    public AudioClip[] thunderClips;

    [Header("천둥이 치는 시간 간격 설정 (초 단위)")]
    public float minDelay = 5f;  // 최소 5초 뒤에
    public float maxDelay = 15f; // 최대 15초 뒤에 천둥 발생

    private AudioSource audioSource;

    void Start()
    {
        audioSource = GetComponent<AudioSource>();

        // 게임이 시작되자마자 자동으로 무한 반복 루틴(Coroutine) 실행
        StartCoroutine(AutoThunderRoutine());
    }

    IEnumerator AutoThunderRoutine()
    {
        // 배열에 사운드가 없다면 안전하게 루틴 종료
        if (thunderClips.Length == 0) yield break;

        // 게임이 실행 중인 동안 무한 반복
        while (true)
        {
            // minDelay와 maxDelay 사이의 랜덤한 시간 동안 대기
            float randomWaitTime = Random.Range(minDelay, maxDelay);
            yield return new WaitForSeconds(randomWaitTime);

            // 6개의 사운드 중 하나를 랜덤으로 선택해서 재생
            int randomIndex = Random.Range(0, thunderClips.Length);
            audioSource.PlayOneShot(thunderClips[randomIndex]);
        }
    }
}
