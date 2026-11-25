using System.Collections;
using UnityEngine;

/// <summary>
/// BabblingWater_low 컨테이너에 추가한다.
/// WaterCoreClearCutscene이 ActivateWaterFlow() 시 아래 순서로 호출한다.
///   1. PrepareAnimation()      — gameObject.SetActive 전: 메쉬 scale을 0으로 준비
///   2. gameObject.SetActive(true) — 호출측(WaterCoreClearCutscene)이 처리
///   3. BeginAnimation(preview)    — gameObject.SetActive 후: 코루틴 시작
/// 미리보기 종료 시 StopAnimation()을 호출하면 메쉬가 초기 상태로 복원된다.
/// </summary>
[AddComponentMenu("ReBloom/Stage3/Water Flow Animator")]
public class WaterFlowAnimator : MonoBehaviour
{
    [Header("메쉬 연결")]
    [Tooltip("BabblingWater_low 자식 물줄기 메쉬 Transform.")]
    [SerializeField] private Transform waterfallMesh;

    [Tooltip("WaterPuddle_low 물웅덩이 메쉬 Transform.")]
    [SerializeField] private Transform puddleMesh;

    [Header("타이밍")]
    [Tooltip("물줄기가 파이프 출구에서 바닥까지 나타나는 시간(초).")]
    [SerializeField] private float waterfallDuration = 0.4f;

    [Tooltip("물줄기가 바닥에 닿은 후 웅덩이가 퍼지는 시간(초).")]
    [SerializeField] private float puddleDuration = 0.3f;

    [Header("물줄기 형태")]
    [Tooltip("애니메이션 시작 시 물줄기 굵기 비율 (0 = 없음, 0.2 = 최종 굵기의 20%). " +
             "세로가 길어지면서 가로 너비도 함께 이 비율에서 100%로 증가.")]
    [SerializeField, Range(0f, 1f)] private float initialWidthRatio = 0.2f;

    [Tooltip("물웅덩이 확장 시작 시점. 0 = 물줄기와 동시에 시작, 1 = 물줄기 완료 후 시작. " +
             "0.75 = 물줄기가 75% 진행됐을 때 웅덩이 확장 시작.")]
    [SerializeField, Range(0f, 1f)] private float puddleStartProgress = 0.75f;

    // ── 내부 상태 ─────────────────────────────────────────────

    private Vector3 _fallSavedLocalPos;
    private Vector3 _fallSavedLocalScale;
    private float   _fallMeshMaxY   = float.NaN; // 수도관 출구 고정용 (Y 보정)
    private float   _fallMeshCenterX = float.NaN; // 가로 중심 고정용 (X 보정)
    private float   _fallMeshCenterZ = float.NaN; // 가로 중심 고정용 (Z 보정)

    private Vector3 _puddleSavedLocalScale;
    private bool    _transformsSaved;

    // ── 공개 API ──────────────────────────────────────────────

    /// <summary>gameObject.SetActive(true) 직전에 호출. 메쉬 scale을 초기값으로 설정.</summary>
    public void PrepareAnimation()
    {
        SaveTransformsIfNeeded();
        // s_y=0: 세로 없음, s_xz=initialWidthRatio: 처음부터 20% 굵기로 시작
        ApplyWaterfallScale(0f, initialWidthRatio);
        if (puddleMesh != null)
        {
            Vector3 s = _puddleSavedLocalScale;
            s.x = 0f;
            s.z = 0f;
            puddleMesh.localScale = s;
        }
    }

    /// <summary>gameObject.SetActive(true) 직후에 호출. 애니메이션 코루틴 시작.</summary>
    public void BeginAnimation(bool preview)
    {
        StartCoroutine(AnimateRoutine(preview));
    }

    /// <summary>미리보기 종료 시 호출. 코루틴 중단, 메쉬 복원.</summary>
    public void StopAnimation()
    {
        StopAllCoroutines();
        RestoreTransforms();
    }

    // ── Transform 관리 ────────────────────────────────────────

    private void SaveTransformsIfNeeded()
    {
        if (_transformsSaved) return;

        if (waterfallMesh != null)
        {
            _fallSavedLocalPos   = waterfallMesh.localPosition;
            _fallSavedLocalScale = waterfallMesh.localScale;

            var mf = waterfallMesh.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                Bounds b = mf.sharedMesh.bounds;
                _fallMeshMaxY   = b.max.y;
                _fallMeshCenterX = b.center.x;
                _fallMeshCenterZ = b.center.z;
            }
        }

        if (puddleMesh != null)
            _puddleSavedLocalScale = puddleMesh.localScale;

        _transformsSaved = true;
    }

    // s_y:  세로 scale (0→1). 수도관 출구 최상단 고정 (Y 보정)
    // s_xz: 가로 scale (initialWidthRatio→1). 메쉬 중심 X/Z 고정 (X/Z 보정)
    // 보정 공식: localPos.? = savedPos.? + (1 - s) * savedScale.? * boundsCenter/Max
    private void ApplyWaterfallScale(float s_y, float s_xz)
    {
        if (waterfallMesh == null) return;

        Vector3 scale = _fallSavedLocalScale;
        scale.y = Mathf.Max(s_y,  0.0001f) * _fallSavedLocalScale.y;
        scale.x = Mathf.Max(s_xz, 0.0001f) * _fallSavedLocalScale.x;
        scale.z = Mathf.Max(s_xz, 0.0001f) * _fallSavedLocalScale.z;
        waterfallMesh.localScale = scale;

        Vector3 pos = _fallSavedLocalPos;
        // Y: 수도관 출구 고정
        if (!float.IsNaN(_fallMeshMaxY))
            pos.y = _fallSavedLocalPos.y + (1f - s_y) * _fallSavedLocalScale.y * _fallMeshMaxY;
        // X: 메쉬 가로 중심 고정
        if (!float.IsNaN(_fallMeshCenterX))
            pos.x = _fallSavedLocalPos.x + (1f - s_xz) * _fallSavedLocalScale.x * _fallMeshCenterX;
        // Z: 메쉬 가로 중심 고정
        if (!float.IsNaN(_fallMeshCenterZ))
            pos.z = _fallSavedLocalPos.z + (1f - s_xz) * _fallSavedLocalScale.z * _fallMeshCenterZ;
        waterfallMesh.localPosition = pos;
    }

    private void RestoreTransforms()
    {
        if (!_transformsSaved) return;

        if (waterfallMesh != null)
        {
            waterfallMesh.localScale    = _fallSavedLocalScale;
            waterfallMesh.localPosition = _fallSavedLocalPos;
        }

        if (puddleMesh != null)
            puddleMesh.localScale = _puddleSavedLocalScale;

        _transformsSaved = false;
    }

    // ── 애니메이션 코루틴 ─────────────────────────────────────

    private IEnumerator AnimateRoutine(bool preview)
    {
        // 웅덩이 병렬 코루틴 시작 (puddleStartProgress * waterfallDuration 후)
        if (puddleMesh != null)
            StartCoroutine(AnimatePuddleWithDelay(puddleStartProgress * waterfallDuration));

        // 물줄기: 파이프 출구에서 바닥까지 (waterfallDuration)
        // s_y: 0→1, s_xz: initialWidthRatio→1 (함께 증가)
        for (float t = 0f; t < waterfallDuration; t += Time.deltaTime)
        {
            float progress = Mathf.SmoothStep(0f, 1f, t / waterfallDuration);
            float s_xz = Mathf.Lerp(initialWidthRatio, 1f, progress);
            ApplyWaterfallScale(progress, s_xz);
            yield return null;
        }
        // 최종 원본 transform 복원
        if (waterfallMesh != null)
        {
            waterfallMesh.localScale    = _fallSavedLocalScale;
            waterfallMesh.localPosition = _fallSavedLocalPos;
        }
    }

    // 웅덩이 병렬 애니메이션 (delay 후 실행, StopAllCoroutines로 중단 가능)
    private IEnumerator AnimatePuddleWithDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        if (puddleMesh == null) yield break;

        for (float t = 0f; t < puddleDuration; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / puddleDuration);
            Vector3 s = _puddleSavedLocalScale;
            s.x *= k;
            s.z *= k;
            puddleMesh.localScale = s;
            yield return null;
        }
        puddleMesh.localScale = _puddleSavedLocalScale;
    }

}
