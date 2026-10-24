using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// <see cref="BodyFollow"/> 의 Room-scale 대응(머리 월드 XZ 추종) 동작을 EditMode 에서 검증한다.
///
/// <b>왜 Editor 폴더에 asmdef 없이 두는가</b> —
/// <see cref="BodyFollow"/>·<see cref="CrouchIK"/>·<see cref="HardwareRig"/> 는 모두
/// 미리 정의된 어셈블리 <c>Assembly-CSharp</c> 에 들어간다. Unity 는 asmdef 기반 어셈블리가
/// <c>Assembly-CSharp</c> 을 참조하는 것을 허용하지 않으므로, 테스트 전용 asmdef 를 만들면
/// 이 타입들을 볼 수 없다. <c>Editor</c> 폴더에 asmdef 없이 두면
/// <c>Assembly-CSharp-Editor</c> 로 들어가고, 그 어셈블리는 <c>Assembly-CSharp</c> 을
/// 자동 참조하므로 테스트가 성립한다.
///
/// <b>EditMode 에서 Update 를 어떻게 돌리는가</b> —
/// Unity 는 EditMode 에서 <c>Update</c> 를 호출하지 않고, <c>BodyFollow.Update</c> 는
/// private 이다. 그래서 리플렉션으로 직접 호출한다. 이렇게 하면 프레임 타이밍에 기대지 않고
/// "한 번 돌렸을 때의 결과"를 결정적으로 볼 수 있다.
///
/// <b>Time.deltaTime 에 의존하지 않는 단정만 쓴다</b> —
/// EditMode 에서 <c>Time.deltaTime</c> 은 0 이거나 불확정이다. 위치 추종은 보간이 없어
/// 영향을 받지 않지만, 회전은 <c>Quaternion.Lerp</c> 라 수렴 속도를 단정할 수 없다.
/// 그래서 회전은 (가) 목표가 Yaw 전용인지 (나) 목표에 도달한 상태가 유지되는지
/// (다) 피치·롤이 생기지 않는지만 본다. 실제 수렴 거동은 PlayMode/기기 확인 항목이다.
///
/// 이 테스트는 씬을 건드리지 않는다. 필요한 오브젝트를 코드에서 만들고 TearDown 에서 모두 지운다.
/// </summary>
public class BodyFollowEditModeTests
{
    private const float Tolerance = 1e-4f;

    private readonly List<GameObject> spawned = new List<GameObject>();

    private GameObject New(string name)
    {
        var go = new GameObject(name);
        spawned.Add(go);
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject go in spawned)
        {
            if (go != null)
                Object.DestroyImmediate(go);
        }

        spawned.Clear();
    }

    // ───────────────────────────── 리플렉션 보조 ─────────────────────────────

    private static void Invoke(object target, string method)
    {
        MethodInfo info = target.GetType().GetMethod(
            method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.IsNotNull(info, $"{target.GetType().Name}.{method} 을 찾지 못했습니다.");
        info.Invoke(target, null);
    }

    private static void Tick(BodyFollow follow) => Invoke(follow, "Update");

    private static void Begin(BodyFollow follow) => Invoke(follow, "Start");

    private static void SetPrivate(object target, string field, object value)
    {
        FieldInfo info = target.GetType().GetField(
            field, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} 필드를 찾지 못했습니다.");
        info.SetValue(target, value);
    }

    private static bool GetPrivateBool(object target, string field)
    {
        FieldInfo info = target.GetType().GetField(
            field, BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.IsNotNull(info, $"{target.GetType().Name}.{field} 필드를 찾지 못했습니다.");
        return (bool)info.GetValue(target);
    }

    /// <summary>머리 타깃과 몸통을 만들어 연결한다. 둘은 서로 부모가 아니다.</summary>
    private BodyFollow BuildPair(out Transform head, out Transform body)
    {
        head = New("HeadTarget").transform;
        GameObject bodyObject = New("Base");
        body = bodyObject.transform;

        var follow = bodyObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        return follow;
    }

    private static void AssertXZ(Transform body, Vector3 expectedFrom, string label)
    {
        Assert.AreEqual(expectedFrom.x, body.position.x, Tolerance, $"{label} — 월드 X");
        Assert.AreEqual(expectedFrom.z, body.position.z, Tolerance, $"{label} — 월드 Z");
    }

    // ───────────────────── 1. 머리 XZ 이동을 몸통이 따라가는가 ─────────────────────

    [Test]
    public void 머리를_XZ로_0_5m_이동하면_몸통_월드XZ가_같아진다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        body.position = Vector3.zero;
        head.position = new Vector3(0.5f, 1.6f, 0f);

        Tick(follow);

        AssertXZ(body, head.position, "0.5 m 측면 이동");
    }

    [Test]
    public void 머리를_XZ로_1m_이동하면_몸통_월드XZ가_같아진다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        body.position = Vector3.zero;
        head.position = new Vector3(1f, 1.6f, 0.75f);

        Tick(follow);

        AssertXZ(body, head.position, "1 m 대각 이동");
    }

    [Test]
    public void 머리를_여러_번_이동해도_매번_따라간다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);
        body.position = Vector3.zero;

        var steps = new[]
        {
            new Vector3(0.5f, 1.6f, 0f),
            new Vector3(1f, 1.6f, 0.75f),
            new Vector3(-0.4f, 1.55f, 1.2f),
            new Vector3(0f, 1.6f, 0f),
        };

        foreach (Vector3 step in steps)
        {
            head.position = step;
            Tick(follow);
            AssertXZ(body, step, $"누적 이동 {step}");
        }
    }

    [Test]
    public void 보간이_없어_한_번의_Update로_즉시_도달한다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        body.position = Vector3.zero;
        head.position = new Vector3(2.5f, 1.6f, -3.25f);

        Tick(follow);

        // Lerp 를 쓰면 한 번에 도달하지 못한다. 즉시 반영이어야 한다.
        AssertXZ(body, head.position, "단일 Update 즉시 도달");
    }

    // ───────────────────── 2. 머리 Y만 바뀌면 몸통 Y는 유지되는가 ─────────────────────

    [Test]
    public void 머리_Y만_바뀌면_몸통_Y는_유지된다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        body.position = new Vector3(3f, 0.25f, -1f);
        head.position = new Vector3(3f, 1.6f, -1f);

        Tick(follow);
        float yAfterFirst = body.position.y;

        head.position = new Vector3(3f, 1.1f, -1f);   // 앉기
        Tick(follow);

        Assert.AreEqual(0.25f, yAfterFirst, Tolerance, "첫 Update 후 몸통 Y");
        Assert.AreEqual(0.25f, body.position.y, Tolerance, "머리 Y가 내려가도 몸통 Y 유지");
    }

    [Test]
    public void 부모가_수직으로_움직이면_몸통_Y가_부모를_따라간다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        GameObject root = New("PlayerRoot");
        body.SetParent(root.transform, true);
        body.localPosition = Vector3.zero;
        head.position = new Vector3(0f, 1.6f, 0f);

        Tick(follow);
        float localYBefore = body.localPosition.y;

        // 루트가 1 m 올라간다(언덕·중력·텔레포트)
        root.transform.position = new Vector3(0f, 1f, 0f);
        Tick(follow);

        Assert.AreEqual(localYBefore, body.localPosition.y, Tolerance,
            "루트가 올라가도 몸통의 로컬 Y 는 변하지 않아야 한다");
        Assert.AreEqual(1f, body.position.y, Tolerance,
            "몸통 월드 Y 는 부모와 함께 올라가야 한다");
    }

    // ───────────────────── 3. 머리 Y축 회전 추적이 유지되는가 ─────────────────────

    [Test]
    public void 회전은_Yaw_전용이라_피치롤이_생기지_않는다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        body.rotation = Quaternion.identity;
        head.rotation = Quaternion.Euler(35f, 70f, -20f);   // 머리를 크게 숙이고 기울임

        for (int i = 0; i < 10; i++)
            Tick(follow);

        Vector3 euler = body.rotation.eulerAngles;
        float pitch = Mathf.DeltaAngle(0f, euler.x);
        float roll = Mathf.DeltaAngle(0f, euler.z);

        Assert.AreEqual(0f, pitch, 1e-3f, "몸통에 피치가 생기면 안 된다");
        Assert.AreEqual(0f, roll, 1e-3f, "몸통에 롤이 생기면 안 된다");
    }

    [Test]
    public void 회전_목표는_머리의_Yaw다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        head.rotation = Quaternion.Euler(35f, 70f, -20f);

        // 목표 자세에 이미 도달한 상태라면 Lerp 계수와 무관하게 그대로 머물러야 한다.
        body.rotation = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
        Quaternion before = body.rotation;

        Tick(follow);

        Assert.AreEqual(0f, Quaternion.Angle(before, body.rotation), 1e-3f,
            "목표(머리 Yaw)에 도달한 상태는 유지되어야 한다 — 목표 계산이 머리 Yaw 임을 뜻한다");
    }

    [Test]
    public void 회전은_목표에서_멀어지지_않는다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        head.rotation = Quaternion.Euler(0f, 90f, 0f);
        body.rotation = Quaternion.identity;

        Quaternion target = Quaternion.Euler(0f, 90f, 0f);
        float before = Quaternion.Angle(body.rotation, target);

        for (int i = 0; i < 5; i++)
            Tick(follow);

        float after = Quaternion.Angle(body.rotation, target);

        // deltaTime 이 0 이면 그대로, 0 보다 크면 가까워진다. 멀어지는 일은 없어야 한다.
        Assert.LessOrEqual(after, before + 1e-3f, "회전이 목표에서 멀어졌다");
    }

    [Test]
    public void 위치_추종이_회전_추종을_막지_않는다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        body.position = Vector3.zero;
        body.rotation = Quaternion.Euler(0f, 90f, 0f);
        head.position = new Vector3(1f, 1.6f, 1f);
        head.rotation = Quaternion.Euler(0f, 90f, 0f);

        Tick(follow);

        AssertXZ(body, head.position, "회전과 위치 동시 적용");
        Assert.AreEqual(0f, Quaternion.Angle(Quaternion.Euler(0f, 90f, 0f), body.rotation), 1e-3f,
            "회전도 함께 유지되어야 한다");
    }

    // ───────── 4. headTarget 미연결(로비·텔레포트 고스트)은 위치 추종에서 제외 ─────────

    [Test]
    public void 로컬리그_폴백이면_위치를_따라가지_않는다()
    {
        // 폴백 상태를 직접 세워 "그 머리는 내 아바타의 것이 아니다" 경로만 본다.
        Transform foreignHead = New("Main Camera (로컬 HMD)").transform;
        GameObject ghost = New("TeleportGhost");
        Transform body = ghost.transform;

        var follow = ghost.AddComponent<BodyFollow>();
        follow.headTarget = foreignHead;
        SetPrivate(follow, "headTargetIsLocalRigFallback", true);

        body.position = new Vector3(10f, 0f, 20f);   // 텔레포트 목적지
        foreignHead.position = new Vector3(0f, 1.6f, 0f);

        Tick(follow);

        Assert.AreEqual(10f, body.position.x, Tolerance, "폴백일 때 X 가 움직였다");
        Assert.AreEqual(0f, body.position.y, Tolerance, "폴백일 때 Y 가 움직였다");
        Assert.AreEqual(20f, body.position.z, Tolerance, "폴백일 때 Z 가 움직였다");
    }

    [Test]
    public void 로컬리그_폴백이어도_회전은_계속_따라간다()
    {
        Transform foreignHead = New("Main Camera (로컬 HMD)").transform;
        GameObject ghost = New("TeleportGhost");

        var follow = ghost.AddComponent<BodyFollow>();
        follow.headTarget = foreignHead;
        SetPrivate(follow, "headTargetIsLocalRigFallback", true);

        foreignHead.rotation = Quaternion.Euler(0f, 45f, 0f);
        ghost.transform.rotation = Quaternion.Euler(0f, 45f, 0f);

        Tick(follow);

        Assert.AreEqual(
            0f,
            Quaternion.Angle(Quaternion.Euler(0f, 45f, 0f), ghost.transform.rotation),
            1e-3f,
            "폴백일 때도 기존 회전 추종은 유지되어야 한다");
    }

    [Test]
    public void headTarget이_연결되어_있으면_폴백_플래그가_꺼진다()
    {
        BodyFollow follow = BuildPair(out Transform head, out Transform body);

        Assert.IsFalse(GetPrivateBool(follow, "headTargetIsLocalRigFallback"),
            "Inspector 연결이 있으면 폴백으로 표시되면 안 된다");
        Assert.AreSame(head, follow.headTarget, "연결된 headTarget 이 바뀌면 안 된다");
    }

    [Test]
    public void headTarget이_비어_있고_리그도_없으면_아무것도_하지_않는다()
    {
        GameObject bodyObject = New("Base");
        var follow = bodyObject.AddComponent<BodyFollow>();
        follow.headTarget = null;

        Begin(follow);

        bodyObject.transform.position = new Vector3(5f, 1f, 5f);
        Tick(follow);   // headTarget == null 이면 early return

        Assert.AreEqual(new Vector3(5f, 1f, 5f), bodyObject.transform.position);
    }

    // ───────── 5. Base 이동이 Base_end_1 로컬 Y / CrouchIK 를 방해하지 않는가 ─────────

    [Test]
    public void 몸통_XZ_이동이_자식의_로컬좌표를_바꾸지_않는다()
    {
        GameObject root = New("PlayerRoot");
        Transform head = New("HeadTarget").transform;
        head.SetParent(root.transform, true);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(root.transform, false);

        GameObject child = new GameObject("Base_end_1");
        child.transform.SetParent(baseObject.transform, false);
        child.transform.localPosition = new Vector3(0f, -0.18f, 0f);   // CrouchIK 가 내려 둔 값

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        Vector3 childLocalBefore = child.transform.localPosition;

        head.position = new Vector3(1.2f, 1.6f, -0.8f);
        Tick(follow);

        Assert.AreEqual(childLocalBefore, child.transform.localPosition,
            "부모 이동은 자식 로컬 좌표를 바꾸지 않아야 한다 (CrouchIK 담당 영역)");
        Assert.AreEqual(1.2f, child.transform.position.x, Tolerance, "자식도 함께 XZ 이동");
        Assert.AreEqual(-0.8f, child.transform.position.z, Tolerance, "자식도 함께 XZ 이동");
    }

    [Test]
    public void CrouchIK의_머리높이_계산이_몸통_XZ_이동에_영향받지_않는다()
    {
        GameObject root = New("PlayerRoot");
        Transform head = New("HeadTarget").transform;
        head.SetParent(root.transform, true);
        head.position = new Vector3(0f, 1.6f, 0f);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(root.transform, false);

        GameObject body = new GameObject("Base_end_1");
        body.transform.SetParent(baseObject.transform, false);

        GameObject leftFoot = new GameObject("LeftFootTarget");
        leftFoot.transform.SetParent(body.transform, false);
        leftFoot.transform.localPosition = new Vector3(-0.11f, 0.082f, -0.011f);

        GameObject rightFoot = new GameObject("RightFootTarget");
        rightFoot.transform.SetParent(body.transform, false);
        rightFoot.transform.localPosition = new Vector3(0.11f, 0.082f, -0.011f);

        var crouch = baseObject.AddComponent<CrouchIK>();
        crouch.headTarget = head;
        crouch.body = body.transform;
        crouch.leftFootTarget = leftFoot.transform;
        crouch.rightFootTarget = rightFoot.transform;
        crouch.playerRoot = root.transform;

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        Invoke(crouch, "Start");
        Invoke(crouch, "LateUpdate");   // 1회차: 기준 키 저장(initialized)

        // 몸통을 XZ 로 크게 옮긴다
        head.position = new Vector3(1.5f, 1.6f, 1.5f);
        Tick(follow);
        Invoke(crouch, "LateUpdate");

        float standing = (float)typeof(CrouchIK)
            .GetField("standingHeadHeight", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(crouch);

        // 서 있는 키는 머리 Y - 루트 Y 이고, XZ 이동과 무관해야 한다.
        Assert.AreEqual(1.6f, standing, 1e-2f,
            "CrouchIK 의 기준 키가 XZ 이동에 흔들리면 안 된다");

        // 발 월드 Y 는 CrouchIK 가 루트 높이에 고정한다
        Assert.AreEqual(
            root.transform.position.y + 0.082f, leftFoot.transform.position.y, 1e-2f,
            "왼발 월드 Y 는 CrouchIK 가 계속 고정해야 한다");
    }

    // ───────── 6. 부모가 회전·이동해도 월드 기준 추종이 올바른가 ─────────

    [Test]
    public void 부모가_회전해도_월드_XZ를_정확히_따라간다()
    {
        Transform head = New("HeadTarget").transform;

        GameObject parent = New("RotatedParent");
        parent.transform.position = new Vector3(-7f, 2f, 13f);
        parent.transform.rotation = Quaternion.Euler(0f, 37f, 0f);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(parent.transform, false);

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        head.position = new Vector3(4.25f, 1.6f, -2.5f);

        Tick(follow);

        AssertXZ(baseObject.transform, head.position, "회전·이동된 부모 아래");
    }

    [Test]
    public void 머리와_몸통의_부모가_서로_달라도_정확히_따라간다()
    {
        GameObject headParent = New("HeadParent");
        headParent.transform.position = new Vector3(20f, 5f, -11f);
        headParent.transform.rotation = Quaternion.Euler(0f, -120f, 0f);

        Transform head = new GameObject("HeadTarget").transform;
        head.SetParent(headParent.transform, false);
        head.localPosition = new Vector3(0.3f, 1.6f, 0.2f);

        GameObject bodyParent = New("BodyParent");
        bodyParent.transform.position = new Vector3(-4f, 1f, 8f);
        bodyParent.transform.rotation = Quaternion.Euler(0f, 64f, 0f);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(bodyParent.transform, false);

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        Tick(follow);

        AssertXZ(baseObject.transform, head.position, "서로 다른 부모 좌표계");
    }

    [Test]
    public void 부모가_비균일_스케일이어도_월드_XZ를_따라간다()
    {
        Transform head = New("HeadTarget").transform;

        GameObject parent = New("ScaledParent");
        parent.transform.position = new Vector3(2f, 0f, 2f);
        parent.transform.localScale = new Vector3(1f, 0.5f, 1f);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(parent.transform, false);

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        head.position = new Vector3(5f, 1.6f, -3f);

        Tick(follow);

        AssertXZ(baseObject.transform, head.position, "비균일 스케일 부모 아래");
    }

    // ───────── 7. 프록시처럼 외부가 HeadTarget 을 움직여도 같은 계산이 되는가 ─────────

    [Test]
    public void 네트워크_루트가_움직여도_머리_월드XZ를_따라간다()
    {
        // 프록시에서는 NetworkTransform 이 루트와 HeadTarget 을 각각 갱신한다.
        // 여기서는 그 결과만 재현한다 — 루트를 옮기고 머리의 로컬 오프셋을 넣는다.
        GameObject networkRoot = New("Player_mental (프록시)");

        Transform head = new GameObject("HeadTarget").transform;
        head.SetParent(networkRoot.transform, false);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(networkRoot.transform, false);

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        // 루트는 조이스틱으로 이동, 머리는 룸스케일로 추가 이동
        networkRoot.transform.position = new Vector3(43.98f, 2.16f, 0.06f);
        networkRoot.transform.rotation = Quaternion.Euler(0f, 25f, 0f);
        head.localPosition = new Vector3(0.9f, 1.6f, -0.4f);

        Tick(follow);

        AssertXZ(baseObject.transform, head.position, "프록시 루트 + 룸스케일 오프셋");

        // 머리의 로컬 오프셋이 남아 있어도 몸통은 머리 밑에 와야 한다
        Vector3 flatDelta = baseObject.transform.position - head.position;
        flatDelta.y = 0f;

        Assert.AreEqual(0f, flatDelta.magnitude, 1e-3f,
            "몸통과 머리의 수평 거리가 0 이어야 팔 IK 가 늘어나지 않는다");
    }

    [Test]
    public void 수정_전_증상인_수평_어긋남이_재현되지_않는다()
    {
        // 회귀 방지: 룸스케일 1.5 m 이동 후 어깨-손 기준점이 어긋나면 안 된다.
        GameObject networkRoot = New("Player_mental");

        Transform head = new GameObject("HeadTarget").transform;
        head.SetParent(networkRoot.transform, false);
        head.localPosition = new Vector3(0f, 1.6f, 0f);

        GameObject baseObject = New("Base");
        baseObject.transform.SetParent(networkRoot.transform, false);

        var follow = baseObject.AddComponent<BodyFollow>();
        follow.headTarget = head;
        Begin(follow);

        Tick(follow);

        // 실제로 1.5 m 걸어감 — 루트는 그대로, 머리만 이동
        head.localPosition = new Vector3(1.5f, 1.6f, 0f);
        Tick(follow);

        Vector3 flatDelta = baseObject.transform.position - head.position;
        flatDelta.y = 0f;

        Assert.AreEqual(0f, flatDelta.magnitude, 1e-3f,
            "룸스케일 이동 후에도 몸통이 머리 밑에 있어야 한다 (수정 전에는 1.5 m 어긋났다)");
    }
}
