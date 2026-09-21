using UnityEngine;

/// <summary>
/// 수로 장애물 판정 영역(WaterwayZone SphereCollider)을 플레이어에게 보여 주는 가이드 라인.
///
/// - 같은 GameObject의 LineRenderer 하나로 XZ 평면의 원(또는 원의 일부 호)을 로컬 좌표로 그린다.
/// - Play Mode: "빛나는 붓자국"(Trail)이 경로를 따라 이동한다.
///   · Arc 모드(useArc): 지정한 호의 시작점에서 Head가 출발 → 끝점 도착 → 꼬리까지 끝점 밖으로 빠져나감 → 반복.
///   · Circle 모드: 360° 원을 끝없이 순환한다.
///   Head가 가장 선명하고 꼬리로 갈수록 알파 0·폭 20%로 사라진다.
/// - Edit Mode 또는 animateTrail OFF: 호(또는 원) 전체를 정적으로 표시해 위치를 맞추기 쉽게 한다.
///
/// 각도 기준 (원 생성 수식 (cos θ, 0, sin θ) 기준, 로컬 좌표):
///   0° = 로컬 +X,  90° = 로컬 +Z,  180° = 로컬 -X,  270° = 로컬 -Z.
///   각도가 커지는 방향은 +X 에서 +Z 로 도는 방향이다. clockwise 는 Trail 진행 방향만 뒤집는다.
///
/// 페이드 구현 메모:
/// - 알파: colorGradient(꼬리 tailAlpha → 머리 headAlpha, SmoothStep)를 정점 알파로 넣는다.
///   LineRenderer에는 정점 색을 읽는 M_WaterwayBoundaryTrail(ReBloom/Waterway Boundary Trail)을 써야 한다.
/// - 폭: widthCurve로 꼬리 20% → 머리 100% 테이퍼.
/// - 호 끝에서 Trail이 잘릴 때도 보이는 구간의 페이드가 원래 Trail 위치에 맞도록,
///   매 프레임 보이는 구간을 Trail 진행률(u)로 되돌려 그라디언트/폭 키를 다시 채운다. (버퍼 재사용, GC Alloc 없음)
///
/// - 시각 표현 전용. WaterMissionManager / SphereCollider 판정 로직과는 완전히 독립적이며
///   반지름은 판정 콜라이더 값(1.4)에 맞춰 Inspector에서 따로 지정한다.
/// - Material은 런타임에 건드리지 않는다. (LineRenderer 정점/폭/그라디언트만 갱신)
/// - ExecuteAlways: Edit Mode에서 Inspector 값을 바꾸면 Scene View에 즉시 반영된다.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(LineRenderer))]
public class WaterwayZoneVisual : MonoBehaviour
{
    // 꼬리 폭 = lineWidth × TailWidthRatio, 머리 폭 = lineWidth
    private const float TailWidthRatio = 0.2f;

    // Gradient 알파 키 개수 (최대 8). 6개면 SmoothStep이 충분히 매끄럽게 샘플된다.
    private const int TrailAlphaKeyCount = 6;

    // 폭 곡선 키 개수. 시간은 0, 0.25, 0.5, 0.75, 1 로 고정.
    private const int TrailWidthKeyCount = 5;

    [Header("Circle")]
    [SerializeField, Min(0.01f), Tooltip("원 반지름(m). WaterwayZone SphereCollider Radius에 맞춘다.")]
    private float radius = 1.4f;

    [SerializeField, Range(3, 256), Tooltip("360° 기준 점 개수. 호는 각도 비율만큼만 사용한다.")]
    private int segments = 64;

    [SerializeField, Min(0.001f), Tooltip("선 두께(m). Trail 머리 쪽 폭이며, 꼬리는 이 값의 20%까지 가늘어진다.")]
    private float lineWidth = 0.03f;

    [Header("Arc")]
    [SerializeField, Tooltip("켜면 원 전체 대신 지정한 호(Arc)만 표시/애니메이션한다.")]
    private bool useArc = true;

    [SerializeField, Range(0f, 360f), Tooltip("호의 중앙 방향(도). 0°=로컬 +X, 90°=로컬 +Z, 180°=로컬 -X, 270°=로컬 -Z.")]
    private float arcCenterAngle = 180f;

    [SerializeField, Range(1f, 360f), Tooltip("호의 전체 각도(도). 180이면 원의 절반.")]
    private float arcAngle = 180f;

    [Header("Trail Animation (Play Mode 전용)")]
    [SerializeField, Tooltip("Trail 애니메이션을 재생한다. 끄면 호/원 전체를 정적으로 표시한다.")]
    private bool animateTrail = true;

    [SerializeField, Min(0.01f), Tooltip("[Arc] Head가 호 시작점에서 끝점까지 가는 시간(초). 꼬리가 빠져나가는 시간은 Trail Length만큼 추가된다.")]
    private float arcTravelDuration = 3f;

    [SerializeField, Min(0f), Tooltip("[Circle] Head가 원을 한 바퀴 도는 속도(바퀴/초). Arc 모드에서는 사용하지 않는다.")]
    private float trailSpeed = 0.08f;

    [SerializeField, Range(0.01f, 1f), Tooltip("Trail 길이. Arc 모드 = 호 길이 대비 비율, Circle 모드 = 원 둘레 대비 비율.")]
    private float trailLength = 0.35f;

    [SerializeField, Range(0f, 1f), Tooltip("머리 쪽 알파(불투명도).")]
    private float headAlpha = 1f;

    [SerializeField, Range(0f, 1f), Tooltip("꼬리 쪽 알파(불투명도). 0이면 꼬리가 완전히 사라진다.")]
    private float tailAlpha = 0f;

    [SerializeField, Tooltip("위에서 봤을 때 시계 방향으로 진행한다. (각도가 줄어드는 방향) 끄면 반시계 방향.")]
    private bool clockwise = true;

    private LineRenderer lineRenderer;

    // 경로 좌표. Circle: segments 개(닫힘은 인덱스 wrap), Arc: pathSegments + 1 개(열린 경로).
    private Vector3[] pathPoints;
    private int pathSegments;
    private bool pathClosed;

    // Trail 머리의 경로 위 위치. Circle: 0~1 순환, Arc: 0 → 1 + trailLength 후 리셋.
    private float headU;

    // 재사용 버퍼 (매 프레임 할당 없음)
    private readonly Gradient trailGradient = new Gradient();
    private readonly GradientColorKey[] whiteColorKeys =
    {
        new GradientColorKey(Color.white, 0f),
        new GradientColorKey(Color.white, 1f)
    };
    private readonly GradientAlphaKey[] trailAlphaKeys = new GradientAlphaKey[TrailAlphaKeyCount];
    private readonly GradientAlphaKey[] opaqueAlphaKeys =
    {
        new GradientAlphaKey(1f, 0f),
        new GradientAlphaKey(1f, 1f)
    };

    private readonly AnimationCurve trailWidthCurve = new AnimationCurve();
    private readonly AnimationCurve fullWidthCurve = AnimationCurve.Constant(0f, 1f, 1f);

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        // Inspector 값이 바뀔 때마다 경로를 다시 만들고 현재 상태를 다시 그린다. (Edit Mode 포함)
        Rebuild();
    }

    private void Reset()
    {
        Rebuild();
    }

    private void Update()
    {
        if (!IsAnimating())
            return;

        if (useArc)
        {
            // Head 0 → 1 이 arcTravelDuration, 이후 꼬리가 끝점 밖으로 빠져나가는 구간(trailLength)까지 진행 후 반복.
            float speed = 1f / Mathf.Max(arcTravelDuration, 0.01f);
            float cycle = 1f + Mathf.Clamp01(trailLength);

            headU += speed * Time.deltaTime;

            if (headU >= cycle)
                headU -= cycle;
        }
        else
        {
            headU = Mathf.Repeat(headU + trailSpeed * Time.deltaTime, 1f);
        }

        ApplyTrail(headU);
    }

    private bool IsAnimating()
    {
        return Application.isPlaying && animateTrail;
    }

    // ------------------------------------------------------------------
    // Build
    // ------------------------------------------------------------------

    /// <summary>현재 Inspector 값으로 경로 좌표와 폭 곡선 키를 준비하고 현재 모습을 그린다.</summary>
    public void Rebuild()
    {
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        RebuildPath();
        EnsureWidthCurveKeys();

        if (lineRenderer == null)
            return;

        lineRenderer.useWorldSpace = false;
        lineRenderer.widthMultiplier = lineWidth;

        if (IsAnimating())
        {
            lineRenderer.loop = false;
            ApplyTrail(headU);
        }
        else
        {
            ApplyFullPath();
        }
    }

    // 경로(원 또는 호) 좌표를 (길이가 바뀔 때만) 새로 만들고 채운다.
    private void RebuildPath()
    {
        float dirSign = clockwise ? -1f : 1f;
        float startAngle;
        float stepDeg;

        if (useArc)
        {
            // 호: 진행 방향 기준 시작점(center ∓ half)에서 끝점(center ± half)까지.
            pathClosed = false;
            pathSegments = Mathf.Max(2, Mathf.RoundToInt(segments * (arcAngle / 360f)));
            stepDeg = arcAngle / pathSegments;
            startAngle = arcCenterAngle - dirSign * arcAngle * 0.5f;
        }
        else
        {
            pathClosed = true;
            pathSegments = segments;
            stepDeg = 360f / segments;
            startAngle = 0f;
        }

        int pointCount = pathClosed ? pathSegments : pathSegments + 1;

        if (pathPoints == null || pathPoints.Length != pointCount)
            pathPoints = new Vector3[pointCount];

        for (int i = 0; i < pointCount; i++)
        {
            float angleRad = (startAngle + dirSign * stepDeg * i) * Mathf.Deg2Rad;
            pathPoints[i] = new Vector3(Mathf.Cos(angleRad) * radius, 0f, Mathf.Sin(angleRad) * radius);
        }
    }

    // 폭 곡선 키를 고정 시간(0, 0.25, 0.5, 0.75, 1)으로 한 번만 만들어 둔다. 값은 매 프레임 MoveKey로 갱신.
    private void EnsureWidthCurveKeys()
    {
        if (trailWidthCurve.length == TrailWidthKeyCount)
            return;

        while (trailWidthCurve.length > 0)
            trailWidthCurve.RemoveKey(trailWidthCurve.length - 1);

        for (int i = 0; i < TrailWidthKeyCount; i++)
        {
            float t = (float)i / (TrailWidthKeyCount - 1);
            trailWidthCurve.AddKey(new Keyframe(t, 1f));
        }
    }

    // ------------------------------------------------------------------
    // Draw
    // ------------------------------------------------------------------

    // Edit Mode / animateTrail OFF: 호(또는 원) 전체, 일정한 폭, 알파 1.
    private void ApplyFullPath()
    {
        lineRenderer.loop = pathClosed;
        lineRenderer.widthCurve = fullWidthCurve;

        trailGradient.SetKeys(whiteColorKeys, opaqueAlphaKeys);
        lineRenderer.colorGradient = trailGradient;

        lineRenderer.positionCount = pathPoints.Length;

        for (int i = 0; i < pathPoints.Length; i++)
            lineRenderer.SetPosition(i, pathPoints[i]);
    }

    // Trail [head - trailLength, head] 중 경로 위에 있는 부분만 그린다. 정점 순서는 꼬리 → 머리.
    // Arc 모드에서는 경로 밖(0 미만, 1 초과)은 잘라내고, 잘린 만큼 페이드 구간(u)을 되돌려 그라디언트/폭에 반영한다.
    private void ApplyTrail(float head)
    {
        if (lineRenderer == null || pathPoints == null)
            return;

        float length = Mathf.Clamp01(trailLength);
        float tail = head - length;

        float visibleStart = tail;
        float visibleEnd = head;

        if (!pathClosed)
        {
            visibleStart = Mathf.Max(tail, 0f);
            visibleEnd = Mathf.Min(head, 1f);

            if (visibleEnd - visibleStart <= 0.0001f)
            {
                lineRenderer.positionCount = 0;
                return;
            }
        }

        // 보이는 구간이 Trail 전체(0~1) 중 어디에 해당하는지. (잘리지 않으면 0~1)
        float uStart = (visibleStart - tail) / length;
        float uEnd = (visibleEnd - tail) / length;

        ApplyTrailFade(uStart, uEnd);
        ApplyTrailPositions(visibleStart, visibleEnd);
    }

    // 보이는 구간의 Trail 진행률(uStart~uEnd)에 맞춰 알파 그라디언트와 폭 곡선을 채운다.
    private void ApplyTrailFade(float uStart, float uEnd)
    {
        for (int i = 0; i < TrailAlphaKeyCount; i++)
        {
            float t = (float)i / (TrailAlphaKeyCount - 1);
            float u = Mathf.Lerp(uStart, uEnd, t);
            trailAlphaKeys[i] = new GradientAlphaKey(TrailAlpha(u), t);
        }

        trailGradient.SetKeys(whiteColorKeys, trailAlphaKeys);
        lineRenderer.colorGradient = trailGradient;

        for (int i = 0; i < TrailWidthKeyCount; i++)
        {
            float t = (float)i / (TrailWidthKeyCount - 1);
            float u = Mathf.Lerp(uStart, uEnd, t);
            trailWidthCurve.MoveKey(i, new Keyframe(t, TrailWidth(u)));
        }

        for (int i = 0; i < TrailWidthKeyCount; i++)
            trailWidthCurve.SmoothTangents(i, 0f);

        lineRenderer.widthCurve = trailWidthCurve;
    }

    // 꼬리(u=0) → 머리(u=1) 알파.
    private float TrailAlpha(float u)
    {
        return Mathf.Lerp(tailAlpha, headAlpha, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u)));
    }

    // 꼬리(u=0) → 머리(u=1) 폭 비율. widthMultiplier(lineWidth)에 곱해진다.
    private static float TrailWidth(float u)
    {
        return Mathf.Lerp(TailWidthRatio, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u)));
    }

    // 경로 위 구간 [start, end](0~1, Circle은 음수·wrap 허용)의 정점을 채운다. 양 끝점은 Lerp.
    private void ApplyTrailPositions(float start, float end)
    {
        float startS = start * pathSegments;
        float endS = end * pathSegments;

        int firstIndex = Mathf.CeilToInt(startS);   // 꼬리 이후 첫 정점
        int lastIndex = Mathf.FloorToInt(endS);     // 머리 이전 마지막 정점
        int innerCount = Mathf.Max(0, lastIndex - firstIndex + 1);

        lineRenderer.positionCount = innerCount + 2;

        int p = 0;
        lineRenderer.SetPosition(p++, PointAt(startS));

        for (int i = firstIndex; i <= lastIndex; i++)
            lineRenderer.SetPosition(p++, pathPoints[PathIndex(i)]);

        lineRenderer.SetPosition(p, PointAt(endS));
    }

    // 경로 위 실수 위치 s(segment 단위)에 해당하는 점. 두 정점 사이를 Lerp 한다.
    private Vector3 PointAt(float s)
    {
        int i = Mathf.FloorToInt(s);
        float fraction = s - i;

        return Vector3.Lerp(pathPoints[PathIndex(i)], pathPoints[PathIndex(i + 1)], fraction);
    }

    // Circle: wrap, Arc: 0 ~ pathSegments 로 클램프.
    private int PathIndex(int i)
    {
        if (pathClosed)
        {
            int r = i % pathSegments;
            return r < 0 ? r + pathSegments : r;
        }

        return Mathf.Clamp(i, 0, pathSegments);
    }
}
