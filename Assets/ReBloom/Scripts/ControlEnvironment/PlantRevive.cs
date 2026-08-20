using System.Collections;
using UnityEngine;

/// <summary>
/// 흑백 상태의 식생을 서서히 컬러로 되돌리는 연출.
///
/// 채도(_SaturationAmount)는 머티리얼 인스턴스가 아니라 MaterialPropertyBlock으로 쓴다.
/// QuickOutline의 Outline 컴포넌트가 OnEnable/OnDisable에서
/// `renderer.materials = renderer.sharedMaterials + 아웃라인 머티리얼` 형태로
/// 렌더러의 머티리얼 배열을 통째로 교체하기 때문에, 머티리얼 인스턴스를 캐싱해 두면
/// 그 참조가 렌더러에서 떨어져 나가 값을 써도 화면에 반영되지 않는다.
/// MaterialPropertyBlock은 렌더러에 붙으므로 머티리얼 배열이 교체돼도 살아남는다.
/// </summary>
public class PlantRevive : MonoBehaviour
{
    private static readonly int SaturationAmountId = Shader.PropertyToID("_SaturationAmount");

    [Header("연출 설정")]
    public float reviveDuration = 5.0f; // 컬러로 변하는 데 걸리는 시간 (초)

    private Renderer plantRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool isReviving;

    // 현재 채도. Outline 토글 등으로 머티리얼이 교체돼도 이 값을 기준으로 다시 적용한다.
    private float saturation;

    private void Awake()
    {
        plantRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();

        // 시작은 흑백(0)으로 확실히 고정
        ApplySaturation(0f);
    }

    // 머티리얼 배열이 교체된 뒤에도 현재 채도가 유지되도록 다시 적용한다.
    private void OnEnable()
    {
        ApplySaturation(saturation);
    }

    /// <summary>외부에서 호출하면 부활 연출이 시작된다.</summary>
    public void StartRevive()
    {
        if (isReviving || plantRenderer == null)
            return;

        // 비활성 오브젝트에서는 코루틴이 돌지 않으므로 즉시 완료 처리한다.
        if (!isActiveAndEnabled)
        {
            ApplySaturation(1f);
            return;
        }

        StartCoroutine(ReviveRoutine());
    }

    // 서서히 채도를 올리는 애니메이션 코루틴
    private IEnumerator ReviveRoutine()
    {
        isReviving = true;

        float elapsedTime = 0f;

        while (elapsedTime < reviveDuration)
        {
            elapsedTime += Time.deltaTime;

            // 0.0 에서 1.0 까지 시간에 따라 부드럽게 값을 계산한다.
            ApplySaturation(Mathf.Lerp(0f, 1f, elapsedTime / reviveDuration));

            yield return null; // 다음 프레임까지 대기
        }

        // 마지막에 확실하게 1(완전 컬러)로 고정
        ApplySaturation(1f);

        isReviving = false;
    }

    // MaterialPropertyBlock을 통해 렌더러 단위로 채도를 쓴다.
    private void ApplySaturation(float value)
    {
        saturation = value;

        if (plantRenderer == null)
            return;

        plantRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(SaturationAmountId, value);
        plantRenderer.SetPropertyBlock(propertyBlock);
    }
}
