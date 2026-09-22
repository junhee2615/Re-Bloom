using System.Collections;
using UnityEngine;

/// <summary>
/// 흑백 상태의 식생을 서서히 컬러로 되돌리는 연출.
///
/// 채도(_SaturationAmount)와 밝기(_ColorBoost)는 머티리얼 인스턴스가 아니라
/// MaterialPropertyBlock으로 쓴다.
/// QuickOutline의 Outline 컴포넌트가 OnEnable/OnDisable에서
/// `renderer.materials = renderer.sharedMaterials + 아웃라인 머티리얼` 형태로
/// 렌더러의 머티리얼 배열을 통째로 교체하기 때문에, 머티리얼 인스턴스를 캐싱해 두면
/// 그 참조가 렌더러에서 떨어져 나가 값을 써도 화면에 반영되지 않는다.
/// MaterialPropertyBlock은 렌더러에 붙으므로 머티리얼 배열이 교체돼도 살아남는다.
///
/// 셰이더의 Saturation 노드에는 클램프가 없어서 채도를 1보다 크게 주면
/// 원본 텍스처보다 더 선명해진다. 밝기는 BaseColor에 곱해지는 _ColorBoost로 따로 조절한다.
/// </summary>
public class PlantRevive : MonoBehaviour
{
    private static readonly int SaturationAmountId = Shader.PropertyToID("_SaturationAmount");
    private static readonly int ColorBoostId = Shader.PropertyToID("_ColorBoost");

    [Header("연출 설정")]
    public float reviveDuration = 5.0f; // 컬러로 변하는 데 걸리는 시간 (초)

    [Header("되살아난 뒤 색감")]
    [Tooltip("최종 채도. 1 = 원본 텍스처 그대로, 1보다 크면 더 선명해진다. 1.2~1.5 권장.")]
    [Range(0f, 2f)]
    public float targetSaturation = 1.3f;

    [Tooltip("최종 밝기 배수. 1 = 원본 그대로. 너무 올리면 밝은 부분이 뭉개지니 1.0~1.2 권장.")]
    [Range(0.5f, 2f)]
    public float targetBrightness = 1.1f;

    private Renderer plantRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool isReviving;

    // 현재 진행도(0=흑백, 1=완전 복원). Outline 토글 등으로 머티리얼이 교체돼도 이 값을 기준으로 다시 적용한다.
    private float progress;

    private void Awake()
    {
        plantRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        // 시작은 흑백(0)으로 확실히 고정
        ApplyProgress(0f);
    }

    // 머티리얼 배열이 교체된 뒤에도 현재 색감이 유지되도록 다시 적용한다.
    private void OnEnable()
    {
        ApplyProgress(progress);
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
            return;
        }

        StartCoroutine(ReviveRoutine());
    }

    // 서서히 채도와 밝기를 올리는 애니메이션 코루틴
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

        // 마지막에 확실하게 1(완전 복원)로 고정
        ApplyProgress(1f);

        isReviving = false;
    }

    // MaterialPropertyBlock을 통해 렌더러 단위로 채도와 밝기를 쓴다.
    private void ApplyProgress(float t)
    {
        progress = Mathf.Clamp01(t);

        if (plantRenderer == null)
            return;

        float saturation = Mathf.Lerp(0f, targetSaturation, progress);
        float brightness = Mathf.Lerp(1f, targetBrightness, progress);

        plantRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(SaturationAmountId, saturation);
        propertyBlock.SetFloat(ColorBoostId, brightness);
        plantRenderer.SetPropertyBlock(propertyBlock);
    }

#if UNITY_EDITOR
    // 플레이 중 인스펙터에서 값을 바꾸면 바로 화면에 반영해서 튜닝하기 쉽게 한다.
    private void OnValidate()
    {
        if (Application.isPlaying && plantRenderer != null)
            ApplyProgress(progress);
    }

    /// <summary>에디터에서 최종 색감만 바로 확인하고 싶을 때 쓴다.</summary>
    [ContextMenu("미리보기: 완전 복원")]
    private void PreviewRevived()
    {
        if (plantRenderer == null)
        {
            plantRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
        }
        ApplyProgress(1f);
    }

    [ContextMenu("미리보기: 흑백")]
    private void PreviewGray()
    {
        if (plantRenderer == null)
        {
            plantRenderer = GetComponent<Renderer>();
            propertyBlock = new MaterialPropertyBlock();
        }
        ApplyProgress(0f);
    }
#endif
}
