using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VegetationGrowth : MonoBehaviour
{
    public List<Transform> vegetationList = new List<Transform>();
    public float growDuration = 3.0f;

    private Dictionary<Transform, Vector3> originalScales = new Dictionary<Transform, Vector3>();
    private bool isGrowing = false;

    void Start()
    {
        Debug.Log($"[테스트] 게임 시작! 등록된 식생 개수: {vegetationList.Count}개");
        int count = 0;
        foreach (Transform veg in vegetationList)
        {
            if (veg != null)
            {
                originalScales[veg] = veg.localScale;
                veg.localScale = Vector3.zero;
                veg.gameObject.SetActive(true);
                count++;
            }
        }
        Debug.Log($"[테스트] {count}개의 식생 크기를 0으로 만들고 활성화했습니다.");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Debug.Log("[테스트] 스페이스바 입력이 정상적으로 감지되었습니다!");
            if (!isGrowing)
            {
                Debug.Log("[테스트] 식생 성장 코루틴 시작!");
                StartCoroutine(GrowAllVegetation());
            }
        }
    }

    IEnumerator GrowAllVegetation()
    {
        isGrowing = true;
        float elapsedTime = 0f;

        while (elapsedTime < growDuration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / growDuration;
            float curve = Mathf.Sin(t * Mathf.PI * 0.5f);

            foreach (Transform veg in vegetationList)
            {
                if (veg != null && originalScales.ContainsKey(veg))
                {
                    veg.localScale = Vector3.Lerp(Vector3.zero, originalScales[veg], curve);
                }
            }
            yield return null;
        }

        foreach (Transform veg in vegetationList)
        {
            if (veg != null && originalScales.ContainsKey(veg))
            {
                veg.localScale = originalScales[veg];
            }
        }

        Debug.Log("[테스트] 식생 성장 완료!");
        isGrowing = false;
    }
}