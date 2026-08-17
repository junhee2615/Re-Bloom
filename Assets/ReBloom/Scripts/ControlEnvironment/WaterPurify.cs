using System.Collections;
using UnityEngine;

public class WaterPurify : MonoBehaviour
{
    [Header("물 머티리얼(재질) 설정")]
    [Tooltip("기존에 깔려있는 더러운 물(m_dirtywater)을 넣어주세요.")]
    public Material dirtyWaterMat;

    [Tooltip("변화할 깨끗한 물(m_cleanwater)을 넣어주세요.")]
    public Material cleanWaterMat;

    [Header("연출 설정")]
    public float purifyDuration = 5.0f; // 정화되는 데 걸리는 시간 (초)

    [Tooltip("정화되면서 수면이 이만큼(m) 위로 올라간다. 0이면 수위 변화 없음.")]
    public float riseHeight = 0.3f;

    private Renderer waterRenderer;
    private bool isPurifying = false;

    void Start()
    {
        // 1. 물 오브젝트의 Renderer(화면에 그려주는 부품)를 가져옵니다.
        waterRenderer = GetComponent<Renderer>();

        // 2. 시작할 때 원본 머티리얼이 망가지지 않도록 복사본을 만들어 입혀줍니다.
        if (waterRenderer != null && dirtyWaterMat != null)
        {
            waterRenderer.material = new Material(dirtyWaterMat);
        }
    }

    // 외부(미션 매니저 등)에서 이 함수를 부르면 연출이 시작됩니다!
    public void StartPurify()
    {
        if (!isPurifying && waterRenderer != null && cleanWaterMat != null)
        {
            StartCoroutine(PurifyRoutine());
        }
    }

    // 서서히 머티리얼을 섞어주는 코루틴
    IEnumerator PurifyRoutine()
    {
        isPurifying = true;
        float elapsedTime = 0f;

        // 현재 적용되어 있는 물의 머티리얼을 가져옵니다.
        Material currentMat = waterRenderer.material;

        // 수위 상승: 시작 위치에서 riseHeight 만큼 위로.
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + Vector3.up * riseHeight;

        while (elapsedTime < purifyDuration)
        {
            elapsedTime += Time.deltaTime;

            // 0.0 에서 1.0 까지 시간에 따른 진행도를 계산합니다.
            float t = elapsedTime / purifyDuration;

            // 핵심 코드: 더러운 물과 깨끗한 물을 t의 비율만큼 스르륵 섞어줍니다!
            currentMat.Lerp(dirtyWaterMat, cleanWaterMat, t);
            
            transform.position = Vector3.Lerp(startPos, endPos, t);

            yield return null; // 다음 프레임까지 대기
        }

        // 2초가 지나면 최종적으로 깨끗한 물 원본으로 완벽하게 교체해 줍니다.
        waterRenderer.material = cleanWaterMat;
        transform.position = endPos;
        isPurifying = false;
    }
}