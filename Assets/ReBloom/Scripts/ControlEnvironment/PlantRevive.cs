using System.Collections;
using UnityEngine;

public class PlantRevive : MonoBehaviour
{
    [Header("연출 설정")]
    public float reviveDuration = 5.0f; // 컬러로 변하는 데 걸리는 시간 (초)

    private Material plantMaterial;
    private bool isReviving = false;

    void Start()
    {
        // 1. 오브젝트에 있는 MeshRenderer에서 Material을 가져옵니다.
        Renderer rend = GetComponent<Renderer>();
        if (rend != null)
        {
            // .material을 사용하면 이 식물만의 고유한 복사본 머티리얼이 생성됩니다.
            plantMaterial = rend.material;

            // 처음엔 확실하게 흑백(0)으로 설정해 둡니다.
            plantMaterial.SetFloat("_SaturationAmount", 0f);
        }
    }


    // 외부(또는 Update)에서 이 함수를 부르면 부활 연출이 시작됩니다!
    public void StartRevive()
    {
        // 연출이 이미 진행 중이 아닐 때만 실행합니다.
        if (!isReviving && plantMaterial != null)
        {
            StartCoroutine(ReviveRoutine());
        }
    }

    // 서서히 숫자를 올리는 애니메이션 코루틴
    IEnumerator ReviveRoutine()
    {
        isReviving = true;
        float elapsedTime = 0f;

        while (elapsedTime < reviveDuration)
        {
            elapsedTime += Time.deltaTime;

            // 0.0 에서 1.0 까지 시간에 따라 부드럽게 값을 계산합니다.
            float currentSaturation = Mathf.Lerp(0f, 1f, elapsedTime / reviveDuration);

            // 계산된 값을 셰이더의 "_SaturationAmount"로 전달합니다.
            plantMaterial.SetFloat("_SaturationAmount", currentSaturation);

            yield return null; // 다음 프레임까지 대기
        }

        // 마지막에 확실하게 1(완전 컬러)로 고정
        plantMaterial.SetFloat("_SaturationAmount", 1f);
        isReviving = false;
    }
}