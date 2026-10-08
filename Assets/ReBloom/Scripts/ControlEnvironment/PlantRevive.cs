using System.Collections;
using UnityEngine;

/// <summary>
/// 흑백 상태의 식생을 서서히 컬러로 되돌리는 연출.
///
/// 셰이더(Plant Color Shader Graphs)의 세 값을 MaterialPropertyBlock으로 쓴다.
///   _SaturationAmount : 채도 (0=흑백, 1=원본, 1보다 크면 더 선명)
///   _ColorBoost       : BaseColor에 곱하는 밝기
///   _ReviveGlow       : Emission 세기. 복원 순간 확 올랐다가 은은한 세기로 가라앉는다.
///
/// 머티리얼 인스턴스가 아니라 MaterialPropertyBlock을 쓰는 이유:
/// QuickOutline의 Outline 컴포넌트가 OnEnable/OnDisable에서
/// `renderer.materials = renderer.sharedMaterials + 아웃라인 머티리얼` 형태로
/// 렌더러의 머티리얼 배열을 통째로 교체하기 때문에, 머티리얼 인스턴스를 캐싱해 두면
/// 그 참조가 렌더러에서 떨어져 나가 값을 써도 화면에 반영되지 않는다.
/// MaterialPropertyBlock은 렌더러에 붙으므로 머티리얼 배열이 교체돼도 살아남는다.
/// </summary>
public class PlantRevive : MonoBehaviour
{
    private static readonly int SaturationAmountId = Shader.PropertyToID("_SaturationAmount");
    private static readonly int ColorBoostId = Shader.PropertyToID("_ColorBoost");
    private static readonly int ReviveGlowId = Shader.PropertyToID("_ReviveGlow");

    [Header("연출 설정")]
    public float reviveDuration = 5.0f; // 컬러로 변하는 데 걸리는 시간 (초)

    [Header("되살아난 뒤 색감")]
    [Tooltip("최종 채도. 1 = 원본 텍스처 그대로, 1보다 크면 더 선명해진다. 1.2~1.5 권장.")]
    [Range(0f, 2f)]
    public float targetSaturation = 1.3f;

    [Tooltip("최종 밝기 배수. 1 = 원본 그대로. 너무 올리면 밝은 부분이 뭉개지니 1.0~1.2 권장.")]
    [Range(0.5f, 2f)]
    public float targetBrightness = 1.1f;

    [Header("발광 (Emission)")]
    [Tooltip("복원이 끝나는 순간의 최대 발광 세기. 2~3이면 확실히 빛나 보인다.")]
    [Range(0f, 5f)]
    public float peakGlow = 2.5f;

    [Tooltip("피크 이후 유지할 은은한 발광 세기. 0이면 발광이 완전히 꺼진다.")]
    [Range(0f, 3f)]
    public float settleGlow = 0.6f;

    [Tooltip("피크에서 은은한 세기로 가라앉는 데 걸리는 시간(초).")]
    public float glowSettleDuration = 1.5f;

    private Renderer plantRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool isReviving;

    // 현재 진행도(0=흑백, 1=완전 복원)와 발광 세기.
    // Outline 토글 등으로 머티리얼이 교체돼도 이 값들을 기준으로 다시 적용한다.
    private float progress;
    private float glow;

    private void Awake()
    {
        plantRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        // 시작은 흑백(0), 발광 없음으로 확실히 고정
        ApplyProgress(0f);
    }

    // 머티리얼 배열이 교체된 뒤에도 현재 색감이 유지되도록 다시 적용한다.
    private void OnEnable()
    {
        Apply();
    }

    /// <summary>외부에서 호출하면 부활 연출이 시작된다.</summary>
    public void StartRevive()
    {
        if (isReviving || plantRenderer == null)
            return;

        // 비활성 오브젝트에서는 코루틴이 돌지 않으므로 즉시 완료 처리한다.
        if (!isActiveAndEnabled)
        {
            ApplyProgress(1f);
            SetGlow(settleGlow);
            return;
        }

        StartCoroutine(ReviveRoutine());
    }

    // 서서히 채도·밝기·발광을 올리고, 마지막에 발광만 은은하게 가라앉히는 코루틴
    private IEnumerator ReviveRoutine()
    {
        isReviving = true;

        float elapsedTime = 0f;

        while (elapsedTime < reviveDuration)
        {
            elapsedTime += Time.deltaTime;

            // 0.0 에서 1.0 까지 시간에 따라 부드럽게 진행도를 계산한다.
            ApplyProgress(elapsedTime / reviveDuration);

            yield return null; // 다음 프레임까지 대기
        }

        // 복원 완료 시점 = 발광 피크
        ApplyProgress(1f);

        // 피크에서 은은한 세기로 가라앉힌다.
        float settleTime = 0f;
        while (settleTime < glowSettleDuration)
        {
            settleTime += Time.deltaTime;
            SetGlow(Mathf.Lerp(peakGlow, settleGlow, settleTime / glowSettleDuration));
            yield return null;
        }

        SetGlow(settleGlow);

        isReviving = false;
    }

    // 진행도에 맞춰 채도·밝기·발광을 함께 계산해 적용한다.
    private void ApplyProgress(float t)
    {
        progress = Mathf.Clamp01(t);
        glow = Mathf.Lerp(0f, peakGlow, progress);
        Apply();
    }

    // 발광 세기만 따로 조정한다 (피크 이후 가라앉히는 구간).
    private void SetGlow(float value)
    {
        glow = value;
        Apply();
    }

    // MaterialPropertyBlock을 통해 렌더러 단위로 현재 값들을 쓴다.
    private void Apply()
    {
        if (plantRenderer == null || propertyBlock == null)
            return;

        float saturation = Mathf.Lerp(0f, targetSaturation, progress);
        float brightness = Mathf.Lerp(1f, targetBrightness, progress);

        plantRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(SaturationAmountId, saturation);
        propertyBlock.SetFloat(ColorBoostId, brightness);
        propertyBlock.SetFloat(ReviveGlowId, glow);
        plantRenderer.SetPropertyBlock(propertyBlock);
    }

#if UNITY_EDITOR
    // 플레이 중 인스펙터에서 값을 바꾸면 바로 화면에 반영해서 튜닝하기 쉽게 한다.
    private void OnValidate()
    {
        if (Application.isPlaying && plantRenderer != null)
            Apply();
    }

    /// <summary>에디터에서 복원 후 모습(은은한 발광 상태)을 바로 확인한다.</summary>
    [ContextMenu("미리보기: 완전 복원")]
    private void PreviewRevived()
    {
        EnsureRefs();
        ApplyProgress(1f);
        SetGlow(settleGlow);
    }

    /// <summary>에디터에서 발광 피크 순간을 확인한다.</summary>
    [ContextMenu("미리보기: 발광 피크")]
    private void PreviewPeak()
    {
        EnsureRefs();
        ApplyProgress(1f);
    }

    [ContextMenu("미리보기: 흑백")]
    private void PreviewGray()
    {
        EnsureRefs();
        ApplyProgress(0f);
    }

    private void EnsureRefs()
    {
        if (plantRenderer == null) plantRenderer = GetComponent<Renderer>();
        if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
    }
#endif
}
