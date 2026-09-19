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
    private Vector3 initialPosition;
    private bool initialPositionSaved;
    private Coroutine waterRiseCoroutine;

    void Start()
    {
        // 1. 물 오브젝트의 Renderer(화면에 그려주는 부품)를 가져옵니다.
        waterRenderer = GetComponent<Renderer>();
        initialPosition = transform.position;
        initialPositionSaved = true;

        // 2. 시작할 때 원본 머티리얼이 망가지지 않도록 복사본을 만들어 입혀줍니다.
        if (waterRenderer != null && dirtyWaterMat != null)
        {
            waterRenderer.material = new Material(dirtyWaterMat);
        }
    }

    // 외부(미션 매니저 등)에서 이 함수를 부르면 연출이 시작됩니다!
    public void StartPurify()
    {
        ApplyCleanMaterial();
        StartWaterRise(purifyDuration);
    }

    public void StartWaterRise(float duration)
    {
        if (waterRiseCoroutine != null)
            StopCoroutine(waterRiseCoroutine);

        if (!initialPositionSaved)
        {
            initialPosition = transform.position;
            initialPositionSaved = true;
        }

        waterRiseCoroutine = StartCoroutine(
            WaterRiseRoutine(Mathf.Max(duration, 0.01f)));
    }

    public void ApplyCleanMaterial()
    {
        if (waterRenderer == null)
            waterRenderer = GetComponent<Renderer>();

        if (waterRenderer != null && cleanWaterMat != null)
            waterRenderer.material = cleanWaterMat;
    }

    public void ResetForTest()
    {
        StopAllCoroutines();
        waterRiseCoroutine = null;

        if (waterRenderer == null)
            waterRenderer = GetComponent<Renderer>();

        if (waterRenderer != null && dirtyWaterMat != null)
            waterRenderer.material = new Material(dirtyWaterMat);

        if (initialPositionSaved)
            transform.position = initialPosition;
    }

    private IEnumerator WaterRiseRoutine(float duration)
    {
        Vector3 startPos = transform.position;
        Vector3 endPos = initialPosition + Vector3.up * riseHeight;
        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsedTime / duration);
            transform.position = Vector3.Lerp(startPos, endPos, t);
            yield return null;
        }

        transform.position = endPos;
        waterRiseCoroutine = null;
    }

}