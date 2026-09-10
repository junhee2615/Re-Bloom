using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 수로 장애물 미션 완료 처리.
/// 모든 장애물(돌·나무·흙)이 치워지면 → 지정 오브젝트 활성화 + WaterPurify.StartPurify() 호출.
///
/// - 돌·나무: 콜라이더가 수로 영역(<see cref="waterwayZone"/>)과 더 이상 겹치지 않으면 치워진 것으로 판정한다
/// - 흙(<see cref="SoilLump"/>): 조각을 다 떠내 Despawn 되면 치워진 것으로 본다.
/// </summary>
public class WaterMissionManager : MonoBehaviour
{
    [Header("완료 조건 — 돌·나무")]
    [Tooltip("수로 입구 영역 콜라이더. 장애물의 콜라이더가 이 영역과 더 이상 겹치지 않으면 치워진 것으로 판정.")]
    [SerializeField] private Collider waterwayZone;
    [Tooltip("장애물 그룹의 부모들.")]
    [SerializeField] private List<Transform> obstacleRoots = new List<Transform>();
    
    private readonly List<WaterMissionObstacle> obstacles = new List<WaterMissionObstacle>();

    [Header("완료 조건 — 흙")]
    [Tooltip("흙 덩이 그룹의 부모.")]
    [SerializeField] private Transform soilRoot;

    private readonly List<SoilLump> soilLumps = new List<SoilLump>();

    [Header("완료 연출")]
    [Tooltip("클리어 시 활성화할 오브젝트들.")]
    [SerializeField] private List<GameObject> objectsToActivate = new List<GameObject>();
    [SerializeField] private Transform dirtyToCleanWaterRoot;
    /// <summary>수로 정화 미션(MISSION 1) 완료. 모든 머신에서 로컬로 1회 발행된다.</summary>
    public static event System.Action MissionCleared;

    private bool completed;
    
    [Header("물 차오르기")]
    [SerializeField] private Transform cleanWater;
    [SerializeField] private float waterStartY = -0.81f;
    [SerializeField] private float waterEndY = 0.30696f;
    [SerializeField] private float waterRiseDuration = 5f;

    private void Awake()
    {
        foreach (Transform root in obstacleRoots)
        {
            if (root == null) continue;
            obstacles.AddRange(root.GetComponentsInChildren<WaterMissionObstacle>(true));
        }

        if (soilRoot != null)
            soilLumps.AddRange(soilRoot.GetComponentsInChildren<SoilLump>(true));
    }

    private void Update()
    {
        if (completed) return;
        if (obstacles.Count == 0 && soilLumps.Count == 0) return;

        JudgeOnAuthority();

        if (AllCleared())
            PlaySequence();
    }

    // 판정은 호스트에서만: 수로 영역(zone) 밖으로 나간 돌·뿌리를 치워짐으로 표시.
    private void JudgeOnAuthority()
    {
        if (waterwayZone == null) return;

        foreach (WaterMissionObstacle o in obstacles)
        {
            if (o == null || o.IsCleared) continue;
            if (o.Object == null || !o.Object.IsValid || !o.HasStateAuthority) continue;

            // 수로 존 밖으로 나가면 치워진 것으로 판정.
            if (!OverlapsZone(waterwayZone, o))
                o.HostMarkCleared();
        }
    }

    // 장애물이 수로 존과 실제로 겹쳐 있으면 true.
    // ComputePenetration은 형상만 비교한다.
    // 양쪽 모두 convex여야 한다.
    private static bool OverlapsZone(Collider zone, WaterMissionObstacle o)
    {
        Collider[] cols = o.BodyColliders;
        if (cols == null) return false;

        foreach (Collider c in cols)
        {
            if (c == null || !c.enabled || c.isTrigger) continue;

            if (Physics.ComputePenetration(
                    c, c.transform.position, c.transform.rotation,
                    zone, zone.transform.position, zone.transform.rotation,
                    out _, out _))
                return true;
        }

        return false;
    }

    private bool AllCleared()
    {
        foreach (WaterMissionObstacle o in obstacles)
            if (o != null && !o.IsCleared) return false;

        foreach (SoilLump s in soilLumps)
            if (s != null) return false;

        return true;
    }

    [ContextMenu("Play Sequence")]
    public void PlaySequence()
    {
        if (completed) return;
        completed = true;
        
        foreach (GameObject go in objectsToActivate) // 지정 오브젝트 활성화
            if (go != null) go.SetActive(true);
        
        if (dirtyToCleanWaterRoot != null) // 물 정화 (DirtyToCleanWater → M_CleanWater)
            foreach (WaterPurify wp in dirtyToCleanWaterRoot.GetComponentsInChildren<WaterPurify>())
                wp.StartPurify();
        
        // 물 차오르기 (5초 연출)
        if (cleanWater != null)
            StartCoroutine(RiseWater());

        // 튜토리얼 진행 알림 (TutorialMissionManager_2가 구독)
        MissionCleared?.Invoke();
    }
    // 도메인 리로드 비활성화 환경 대비: 재생 세션 시작 전 정적 이벤트 리셋
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        MissionCleared = null;
    }

    
    private IEnumerator RiseWater()
    {
        cleanWater.gameObject.SetActive(true);

        Vector3 p = cleanWater.localPosition;
        p.y = waterStartY;
        cleanWater.localPosition = p;

        float t = 0f;
        while (t < waterRiseDuration)
        {
            t += Time.deltaTime;
            float y = Mathf.Lerp(waterStartY, waterEndY, Mathf.Clamp01(t / waterRiseDuration));
            Vector3 cur = cleanWater.localPosition;
            cur.y = y;
            cleanWater.localPosition = cur;
            yield return null;
        }

        Vector3 end = cleanWater.localPosition;
        end.y = waterEndY;
        cleanWater.localPosition = end;
    }
}
