using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ReBloom.Water
{
    /// <summary>
    /// 간헐천 미션의 흐름 컨트롤러.
    ///
    /// 물결 한 번 = [예고] -> [멈칫] -> [판정 순간] -> [통과 후 마름]
    /// 시각(젖음 셰이더)은 진짜/페이크가 완전히 동일하고,
    /// 차이는 오직 햅틱 펄스에만 존재한다. isReal 은 절대 셰이더로 넘기지 않는다.
    ///
    /// 셰이더 전역값:
    ///   _WetOrigin / _WetFlowDir / _WetFront / _WetAmount / _CrestGain
    /// </summary>
    [AddComponentMenu("ReBloom/Riverbed Flow Controller")]
    public class RiverbedFlowController : MonoBehaviour
    {
        // 제네릭 UnityEvent 는 인스펙터 노출을 위해 구체 서브클래스가 필요하다
        [System.Serializable] public class FloatEvent : UnityEvent<float> { }
        [System.Serializable] public class IntEvent : UnityEvent<int> { }
        [System.Serializable] public class BoolEvent : UnityEvent<bool> { }

        [System.Serializable]
        public class RoundSetup
        {
            public string label = "Round";

            [Tooltip("이 회차에서 페이크(진동 없는 물결)가 섞이는 확률 0~1")]
            [Range(0f, 1f)] public float fakeChance = 0f;

            [Tooltip("판정 창 (초, ± 값)")]
            public float window = 0.45f;

            [Tooltip("물결과 물결 사이 간격 (초)")]
            public float interval = 4.0f;

            [Tooltip("예고 구간 길이 (초). 짧을수록 어렵다")]
            public float approachDuration = 2.4f;

            [Tooltip("성공했을 때 강바닥에 남는 젖음 (0~1). 회차마다 올라간다")]
            [Range(0f, 1f)] public float wetBaselineOnSuccess = 0.2f;
        }

        public enum Phase { Idle, Waiting, WaveIncoming, Resolving, Complete }

        public enum WaveOutcome { Success, TooEarly, TooLate, Missed, FalseAlarm }

        // ---------------------------------------------------------------
        [Header("흐름 지오메트리")]
        [Tooltip("상류 원점. 비워두면 아래 Origin Position 값을 쓴다")]
        public Transform originPoint;
        public Vector3 originPosition = new Vector3(-15f, 1f, 10f);

        [Tooltip("하류 방향")]
        public Vector3 flowDirection = Vector3.right;

        [Tooltip("판정 지점을 오브젝트로 지정한다 (스툴 사이 중점 등).\n넣으면 아래 거리값이 자동 계산되므로 미터를 손으로 적을 필요가 없다")]
        public Transform judgmentAnchor;

        [Tooltip("판정 지점까지의 거리 (m). Judgment Anchor 가 있으면 자동으로 채워진다")]
        public float judgmentDistance = 30f;

        [Tooltip("판정 지점을 지나 계속 흘러가는 거리 (m)")]
        public float runoutDistance = 40f;

        [Tooltip("통과 후 흘러가는 속도 (m/s)")]
        public float exitSpeed = 22f;

        // ---------------------------------------------------------------
        [Header("물결 모양")]
        [Tooltip("켜면 아래 두 값으로 커브를 자동 생성한다. 직접 그리고 싶으면 끈다")]
        public bool autoBuildCurve = true;

        [Tooltip("멈칫이 일어나는 지점. 판정 지점보다 몇 m 앞인가")]
        public float hesitationOffset = 6f;

        [Tooltip("멈칫이 예고 구간의 어느 시점인가 (0~1)")]
        [Range(0.45f, 0.92f)] public float hesitationTimeRatio = 0.80f;

        [Tooltip("예고 구간의 진행 커브. x=시간 0~1, y=판정점까지의 거리 비율 0~1")]
        public AnimationCurve frontShape = new AnimationCurve(
            new Keyframe(0f, 0f, 0.4f, 0.4f),
            new Keyframe(0.62f, 0.72f, 1.1f, 1.1f),
            new Keyframe(0.80f, 0.80f, 0.15f, 0.15f),
            new Keyframe(1f, 1f, 2.2f, 2.2f));

        [Tooltip("물결이 지나간 뒤 마르는 시간 (초)")]
        public float dryDuration = 3.0f;

        // ---------------------------------------------------------------
        [Header("햅틱 — 거리 기반 연속 진동 (ear 전용)")]
        [Tooltip("진동을 느끼는 기준점. 보통 ear 플레이어. 비우면 판정 지점을 쓴다")]
        public Transform hapticListener;

        [Tooltip("이 거리(m) 밖이면 진동이 없다")]
        public float hapticRange = 26f;

        [Tooltip("가까움(0~1) 을 진동 세기(0~1) 로 바꾸는 곡선. 오른쪽 끝의 평탄 구간이 ear 의 판정 창이 된다")]
        public AnimationCurve hapticFalloff = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.15f),
            new Keyframe(0.45f, 0.10f, 0.5f, 0.5f),
            new Keyframe(0.72f, 0.85f, 3.2f, 3.2f),
            new Keyframe(0.80f, 1f, 0f, 0f),
            new Keyframe(1f, 1f, 0f, 0f));

        [Tooltip("최대 진동 세기")]
        [Range(0f, 1f)] public float hapticMaxAmplitude = 1f;

        [Tooltip("페이크 물결의 진동 배율. 0 이면 아예 안 울린다")]
        [Range(0f, 1f)] public float fakeHapticScale = 0f;

        [Tooltip("진동 세기가 이 값 이상이면 ear 가 트리거를 눌러 성공할 수 있다 (평탄 구간)")]
        [Range(0.5f, 1f)] public float earPlateauThreshold = 0.95f;

        // ---------------------------------------------------------------
        [Header("시작 조건 — 두 사람이 발판에 설 때")]
        [Tooltip("여기 등록된 발판이 모두 점유되어야 물결이 오기 시작한다")]
        public StandZone[] requiredStations;

        [Tooltip("끄면 발판과 무관하게 바로 시작한다 (단독 테스트용)")]
        public bool requireStations = true;

        [Tooltip("둘 다 선 뒤 첫 물결까지의 뜸 (초)")]
        public float firstWaveDelay = 2.5f;

        // ---------------------------------------------------------------
        [Header("회차 구성 (3회 성공 = 클리어)")]
        public List<RoundSetup> rounds = new List<RoundSetup>();

        [Tooltip("연속 실패가 이 횟수를 넘으면 판정 창을 조용히 넓힌다")]
        public int assistAfterFailures = 3;

        [Tooltip("어시스트가 켜졌을 때 판정 창에 더해줄 값 (초)")]
        public float assistWindowBonus = 0.12f;

        // ---------------------------------------------------------------
        [Header("대상")]
        [Tooltip("젖음 셰이더를 쓰는 렌더러들. 런타임에 Preview Mode 를 끈다")]
        public Renderer[] riverbedRenderers;

        [Header("입력")]
        [Tooltip("에디터 테스트용 키. VR 에서는 SubmitInput() 을 직접 호출한다")]
        public KeyCode debugKey = KeyCode.Space;

        [Header("디버그")]
        public bool showDebugHud = true;
        public bool logToConsole = true;

        // ---------------------------------------------------------------
        [Header("이벤트")]
        [Tooltip("진짜 물결의 진동 펄스. float = 세기 0~1. XR 햅틱에 연결")]
        public FloatEvent onHapticIntensity;

        [Tooltip("물결 하나가 판정된 직후. int = WaveOutcome")]
        public IntEvent onWaveResolved;

        [Tooltip("한 회차 성공. int = 성공 누적 횟수 (1,2,3)")]
        public IntEvent onRoundCleared;

        [Tooltip("두 사람이 발판에 모두 섰는지 바뀔 때")]
        public BoolEvent onStationsChanged;

        [Tooltip("미션이 실제로 시작된 순간 (둘 다 선 직후)")]
        public UnityEvent onMissionStarted;

        [Tooltip("3회 모두 성공. 여기서 수면 메시를 올린다")]
        public UnityEvent onMissionComplete;

        // ---------------------------------------------------------------
        static readonly int IdOrigin = Shader.PropertyToID("_WetOrigin");
        static readonly int IdFlowDir = Shader.PropertyToID("_WetFlowDir");
        static readonly int IdFront = Shader.PropertyToID("_WetFront");
        static readonly int IdAmount = Shader.PropertyToID("_WetAmount");
        static readonly int IdCrest = Shader.PropertyToID("_CrestGain");
        static readonly int IdPreview = Shader.PropertyToID("_PreviewMode");

        Phase phase = Phase.Idle;
        int roundIndex;
        int successCount;
        int consecutiveFailures;

        bool waveActive;
        bool waveIsReal;
        bool waveInputTaken;
        float waveStartTime;
        float beatTime;
        float nextWaveTime;

        float front;
        float wetBaseline;
        float wetActive;
        float crestGain;

        float hapticTimer;
        float lastPulseStrength;
        float lastPulseTime = -99f;

        bool stationsReadyCached;

        string lastOutcomeText = "";
        float lastOutcomeTime = -99f;

        // ---------------------------------------------------------------
        void Reset()
        {
            BuildDefaultRounds();
        }

        void BuildDefaultRounds()
        {
            rounds = new List<RoundSetup>();

            RoundSetup a = new RoundSetup();
            a.label = "1회차 · 규칙 학습";
            a.fakeChance = 0f;
            a.window = 0.50f;
            a.interval = 4.5f;
            a.approachDuration = 2.8f;
            a.wetBaselineOnSuccess = 0.20f;
            rounds.Add(a);

            RoundSetup b = new RoundSetup();
            b.label = "2회차 · 페이크 등장";
            b.fakeChance = 0.40f;
            b.window = 0.35f;
            b.interval = 3.8f;
            b.approachDuration = 2.4f;
            b.wetBaselineOnSuccess = 0.45f;
            rounds.Add(b);

            RoundSetup c = new RoundSetup();
            c.label = "3회차 · 소통 압박";
            c.fakeChance = 0.55f;
            c.window = 0.25f;
            c.interval = 3.0f;
            c.approachDuration = 2.0f;
            c.wetBaselineOnSuccess = 0.70f;
            rounds.Add(c);
        }

        void OnValidate()
        {
            ResolveJudgmentDistance();
            RebuildFrontShape();
        }

        void Awake()
        {
            if (rounds == null || rounds.Count == 0) BuildDefaultRounds();
            ResolveJudgmentDistance();
            RebuildFrontShape();

            if (riverbedRenderers != null)
            {
                for (int i = 0; i < riverbedRenderers.Length; i++)
                {
                    Renderer r = riverbedRenderers[i];
                    if (r == null) continue;
                    Material[] mats = r.materials;
                    for (int m = 0; m < mats.Length; m++)
                    {
                        if (mats[m] != null && mats[m].HasProperty(IdPreview))
                            mats[m].SetFloat(IdPreview, 0f);
                    }
                }
            }
        }

        void OnEnable()
        {
            ResetMission();
        }

        // ---------------------------------------------------------------
        /// <summary>Judgment Anchor 가 있으면 흐름축에 투영해서 거리를 자동으로 채운다.</summary>
        public void ResolveJudgmentDistance()
        {
            if (judgmentAnchor == null) return;
            float d = Vector3.Dot(judgmentAnchor.position - Origin, Flow);
            judgmentDistance = Mathf.Max(1f, d);
        }

        /// <summary>멈칫 위치(m)와 시점(0~1)으로 진행 커브를 다시 만든다.</summary>
        public void RebuildFrontShape()
        {
            if (!autoBuildCurve) return;

            float d = Mathf.Max(judgmentDistance, 0.01f);
            float hesY = Mathf.Clamp(1f - (hesitationOffset / d), 0.05f, 0.97f);
            float t = Mathf.Clamp(hesitationTimeRatio, 0.45f, 0.92f);
            float preT = Mathf.Clamp(t - 0.20f, 0.05f, t - 0.02f);
            float preY = Mathf.Clamp(hesY - 0.12f, 0.02f, hesY - 0.005f);

            float[] xs = new float[] { 0f, preT, t, 1f };
            float[] ys = new float[] { 0f, preY, hesY, 1f };

            float[] delta = new float[3];
            for (int i = 0; i < 3; i++)
                delta[i] = (ys[i + 1] - ys[i]) / Mathf.Max(xs[i + 1] - xs[i], 0.0001f);

            float[] m = new float[4];
            m[0] = delta[0] * 0.35f;                        // 상류에서 서서히 붙는다
            m[1] = Mathf.Max(delta[0], delta[1]) * 1.15f;   // 가장 빠른 구간
            m[2] = delta[2] * 0.10f;                        // <- 멈칫
            m[3] = delta[2];

            // Fritsch-Carlson 단조 조건: 전선이 뒤로 물러서는 언더슈트를 막는다
            for (int i = 0; i < 4; i++)
            {
                float lim = float.MaxValue;
                if (i > 0) lim = Mathf.Min(lim, delta[i - 1]);
                if (i < 3) lim = Mathf.Min(lim, delta[i]);
                m[i] = Mathf.Clamp(m[i], 0f, 3f * lim);
            }

            Keyframe[] keys = new Keyframe[4];
            for (int i = 0; i < 4; i++) keys[i] = new Keyframe(xs[i], ys[i], m[i], m[i]);
            frontShape = new AnimationCurve(keys);
        }

        public Vector3 Origin
        {
            get { return originPoint != null ? originPoint.position : originPosition; }
        }

        public Vector3 Flow
        {
            get
            {
                Vector3 f = flowDirection;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.right;
            }
        }

        /// <summary>멈칫이 실제로 일어나는 월드 위치. 기즈모와 확인용.</summary>
        public Vector3 HesitationPoint
        {
            get { return Origin + Flow * Mathf.Max(0f, judgmentDistance - hesitationOffset); }
        }

        public Vector3 JudgmentPoint
        {
            get { return Origin + Flow * judgmentDistance; }
        }

        public RoundSetup CurrentRound
        {
            get
            {
                if (rounds == null || rounds.Count == 0) return null;
                return rounds[Mathf.Clamp(roundIndex, 0, rounds.Count - 1)];
            }
        }

        float CurrentWindow
        {
            get
            {
                RoundSetup r = CurrentRound;
                float w = r != null ? r.window : 0.35f;
                if (consecutiveFailures >= assistAfterFailures) w += assistWindowBonus;
                return w;
            }
        }

        public bool StationsReady()
        {
            if (!requireStations) return true;
            if (requiredStations == null || requiredStations.Length == 0) return true;
            for (int i = 0; i < requiredStations.Length; i++)
            {
                if (requiredStations[i] == null) continue;
                if (!requiredStations[i].IsOccupied) return false;
            }
            return true;
        }

        // ---------------------------------------------------------------
        public void ResetMission()
        {
            phase = Phase.Idle;
            roundIndex = 0;
            successCount = 0;
            consecutiveFailures = 0;
            waveActive = false;
            front = -999f;
            wetBaseline = 0f;
            wetActive = 0f;
            crestGain = 0f;
            stationsReadyCached = false;
            nextWaveTime = Time.time + firstWaveDelay;
            PushGlobals();
        }

        void Update()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (debugKey != KeyCode.None && Input.GetKeyDown(debugKey)) SubmitInput();
#endif
            float t = Time.time;
            float dt = Time.deltaTime;

            bool ready = StationsReady();
            if (ready != stationsReadyCached)
            {
                stationsReadyCached = ready;
                if (onStationsChanged != null) onStationsChanged.Invoke(ready);
            }

            if (phase != Phase.Complete)
            {
                if (!ready)
                {
                    if (waveActive) { waveActive = false; crestGain = 0f; }
                    phase = Phase.Idle;
                    nextWaveTime = t + firstWaveDelay;
                }
                else if (phase == Phase.Idle)
                {
                    phase = Phase.Waiting;
                    nextWaveTime = t + firstWaveDelay;
                    if (onMissionStarted != null) onMissionStarted.Invoke();
                    if (logToConsole) Debug.Log("[Riverbed] both players are on their stools - mission start");
                }
            }

            // 안전망: 어떤 이유로든 물결 없이 Resolving 에 멈춰 있으면 대기로 돌려놓는다
            if (phase == Phase.Resolving && !waveActive)
            {
                phase = Phase.Waiting;
                RoundSetup rw = CurrentRound;
                if (nextWaveTime <= t) nextWaveTime = t + (rw != null ? rw.interval : 4f);
            }

            if (phase == Phase.Waiting && t >= nextWaveTime) SpawnWave();

            if (waveActive) UpdateWave(t, dt);
            else crestGain = Mathf.MoveTowards(crestGain, 0f, dt * 3f);

            UpdateHapticIntensity(dt);
            if (onHapticIntensity != null) onHapticIntensity.Invoke(hapticIntensity);

            if (!waveActive && wetActive > wetBaseline)
            {
                float rate = dryDuration > 0.01f ? (1f / dryDuration) : 10f;
                wetActive = Mathf.MoveTowards(wetActive, wetBaseline, rate * dt);
            }

            PushGlobals();
        }

        void SpawnWave()
        {
            RoundSetup r = CurrentRound;
            if (r == null) return;

            ResolveJudgmentDistance();

            waveIndex++;
            waveActive = true;
            waveInputTaken = false;
            // 난수 대신 해시를 쓴다. 나중에 시드와 인덱스만 동기화하면
            // 두 클라이언트가 같은 진짜/페이크 순서를 뽑게 된다
            waveIsReal = Hash01(waveSeed + waveIndex) >= r.fakeChance;
            waveStartTime = Time.time;
            beatTime = waveStartTime + r.approachDuration;
            phase = Phase.WaveIncoming;

            if (logToConsole)
                Debug.Log("[Riverbed] wave #" + waveIndex + " (" + (waveIsReal ? "REAL" : "FAKE")
                    + ") beat in " + r.approachDuration.ToString("F2") + "s");
        }

        [Header("물결 순서")]
        [Tooltip("진짜/페이크 순서의 시드. 네트워크 동기화 때 이 값만 맞추면 양쪽이 같아진다")]
        public int waveSeed = 12345;

        int waveIndex;

        static float Hash01(int n)
        {
            unchecked
            {
                uint x = (uint)(n * 747796405 + 2891336453);
                x = ((x >> ((int)(x >> 28) + 4)) ^ x) * 277803737;
                x = (x >> 22) ^ x;
                return x / 4294967295f;
            }
        }

        void UpdateWave(float t, float dt)
        {
            RoundSetup r = CurrentRound;
            float approach = r != null ? r.approachDuration : 2.4f;
            float u = approach > 0.01f ? (t - waveStartTime) / approach : 1f;

            if (u <= 1f) front = frontShape.Evaluate(Mathf.Clamp01(u)) * judgmentDistance;
            else front = judgmentDistance + (t - beatTime) * exitSpeed;

            wetActive = Mathf.Max(wetActive, Mathf.Clamp01(wetBaseline + 0.9f * Mathf.Clamp01(u * 1.6f)));
            crestGain = Mathf.MoveTowards(crestGain, 1f, dt * 5f);

            // 판정 창이 닫혔는데 입력이 없었다.
            // mental 은 부 기준 창, ear 는 평탄 구간이 끝나면 기회가 사라진다
            bool mentalWindowClosed = t > beatTime + CurrentWindow;
            bool earWindowClosed = hapticIntensity < earPlateauThreshold;
            if (!waveInputTaken && mentalWindowClosed && earWindowClosed)
            {
                waveInputTaken = true;
                if (waveIsReal) Resolve(WaveOutcome.Missed, t - beatTime);
            }

            if (front > judgmentDistance + runoutDistance) EndWave();
        }

        float hapticIntensity;

        // 물결이 없을 때 마구 누르는 걸 한 번으로 묶기 위한 쿨다운
        float lastEmptyPressTime = -99f;
        const float emptyPressCooldown = 0.5f;

        /// <summary>지금 ear 가 느끼는 진동 세기 0~1. 페이크 물결은 fakeHapticScale 만큼만 울린다.</summary>
        public float CurrentHapticIntensity { get { return hapticIntensity; } }

        /// <summary>진동이 평탄 구간에 들어왔는가. ear 는 이 동안 아무 때나 눌러도 성공한다.</summary>
        public bool IsInEarWindow
        {
            get { return waveActive && !waveInputTaken && hapticIntensity >= earPlateauThreshold; }
        }

        void UpdateHapticIntensity(float dt)
        {
            if (!waveActive)
            {
                hapticIntensity = Mathf.MoveTowards(hapticIntensity, 0f, dt * 4f);
                return;
            }

            Vector3 o = Origin;
            Vector3 f = Flow;

            // 듣는 사람의 흘름축 위치. 미지정이면 판정 지점을 쓴다
            float listenerAxial = hapticListener != null
                ? Vector3.Dot(hapticListener.position - o, f)
                : judgmentDistance;

            float d = Mathf.Abs(listenerAxial - front);
            float proximity = 1f - Mathf.Clamp01(d / Mathf.Max(hapticRange, 0.01f));

            float shaped = (hapticFalloff != null && hapticFalloff.length > 0)
                ? hapticFalloff.Evaluate(proximity)
                : proximity;

            float scale = waveIsReal ? 1f : fakeHapticScale;
            hapticIntensity = Mathf.Clamp01(shaped * hapticMaxAmplitude * scale);
        }

        void EndWave()
        {
            waveActive = false;
            if (phase != Phase.Complete) phase = Phase.Waiting;

            RoundSetup r = CurrentRound;
            nextWaveTime = Time.time + (r != null ? r.interval : 4f);
        }

        // ---------------------------------------------------------------
        /// <summary>VR 입력에서 호출. 물결을 '지금'이라고 찍는 동작.</summary>
        // role unknown (editor key) -> judged like mental
        public void SubmitInput() { SubmitInput(Role.none); }

        // mental : narrow window around the beat (they can see it)
        // ear    : anywhere inside the haptic plateau (intensity alone cannot pinpoint the beat)
        public void SubmitInput(Role role)
        {
            if (phase == Phase.Complete || phase == Phase.Idle) return;

            float t = Time.time;

            if (!waveActive || waveInputTaken)
            {
                if (Time.time - lastEmptyPressTime < emptyPressCooldown) return;
                lastEmptyPressTime = Time.time;
                Resolve(WaveOutcome.FalseAlarm, 0f);
                return;
            }

            float err = t - beatTime;
            waveInputTaken = true;

            if (role == Role.ear)
            {
                if (!waveIsReal) { Resolve(WaveOutcome.FalseAlarm, err); return; }
                if (hapticIntensity >= earPlateauThreshold) Resolve(WaveOutcome.Success, err);
                else Resolve(err < 0f ? WaveOutcome.TooEarly : WaveOutcome.TooLate, err);
                return;
            }

            if (Mathf.Abs(err) <= CurrentWindow)
                Resolve(waveIsReal ? WaveOutcome.Success : WaveOutcome.FalseAlarm, err);
            else if (err < 0f) Resolve(WaveOutcome.TooEarly, err);
            else Resolve(WaveOutcome.TooLate, err);
        }

        void Resolve(WaveOutcome outcome, float errorSeconds)
        {
            lastOutcomeTime = Time.time;

            if (outcome == WaveOutcome.Success)
            {
                consecutiveFailures = 0;
                successCount++;

                RoundSetup r = CurrentRound;
                if (r != null) wetBaseline = Mathf.Max(wetBaseline, r.wetBaselineOnSuccess);

                lastOutcomeText = "성공  (" + (errorSeconds >= 0 ? "+" : "") + errorSeconds.ToString("F3") + "s)";
                if (onRoundCleared != null) onRoundCleared.Invoke(successCount);

                if (roundIndex < rounds.Count - 1)
                {
                    roundIndex++;
                    phase = Phase.Resolving;
                }
                else
                {
                    phase = Phase.Complete;
                    wetBaseline = Mathf.Max(wetBaseline, 0.7f);
                    lastOutcomeText = "물길이 이어졌다";
                    if (onMissionComplete != null) onMissionComplete.Invoke();
                }
            }
            else
            {
                consecutiveFailures++;
                phase = Phase.Resolving;

                if (outcome == WaveOutcome.TooEarly)
                    lastOutcomeText = "너무 빠름  (" + errorSeconds.ToString("F3") + "s) — 물이 튕겨 되돌아감";
                else if (outcome == WaveOutcome.TooLate)
                    lastOutcomeText = "너무 늦음  (+" + errorSeconds.ToString("F3") + "s) — 흐름이 지나가버림";
                else if (outcome == WaveOutcome.Missed)
                    lastOutcomeText = "놓침 — 진짜 물결이었다";
                else
                    lastOutcomeText = "헛침 — 진동이 없는 물결이었다";
            }

            // 물결이 없는 상태에서 판정이 난 경우(헛침 등) 여기서 대기 상태로 되돌린다.
            // EndWave() 는 waveActive 가 true 일 때만 불리므로, 이게 없으면
            // phase 가 Resolving 에 갇혀서 다음 물결이 영영 안 온다.
            if (!waveActive && phase != Phase.Complete)
            {
                phase = Phase.Waiting;
                RoundSetup rr = CurrentRound;
                if (nextWaveTime <= Time.time)
                    nextWaveTime = Time.time + (rr != null ? rr.interval : 4f);
            }

            if (logToConsole) Debug.Log("[Riverbed] " + outcome + " : " + lastOutcomeText);
            if (onWaveResolved != null) onWaveResolved.Invoke((int)outcome);
        }

        // ---------------------------------------------------------------
        void PushGlobals()
        {
            Vector3 o = Origin;
            Vector3 f = Flow;
            Shader.SetGlobalVector(IdOrigin, new Vector4(o.x, o.y, o.z, 0f));
            Shader.SetGlobalVector(IdFlowDir, new Vector4(f.x, f.y, f.z, 0f));
            Shader.SetGlobalFloat(IdFront, front);
            Shader.SetGlobalFloat(IdAmount, Mathf.Clamp01(Mathf.Max(wetActive, wetBaseline)));
            Shader.SetGlobalFloat(IdCrest, Mathf.Clamp01(crestGain));
        }

        // ---------------------------------------------------------------
        void OnDrawGizmos()
        {
            Vector3 o = Origin;
            Vector3 f = Flow;
            Vector3 side = Vector3.Cross(f, Vector3.up).normalized;

            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(o, 0.6f);
            Gizmos.DrawLine(o, o + f * (judgmentDistance + runoutDistance));

            // 멈칫 지점 (주황)
            Vector3 hes = HesitationPoint;
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawLine(hes - side * 9f, hes + side * 9f);
            Gizmos.DrawWireSphere(hes, 0.8f);

            // 판정 지점 (노랑)
            Vector3 judge = JudgmentPoint;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(judge, 1.2f);
            Gizmos.DrawLine(judge - side * 10f, judge + side * 10f);

            if (Application.isPlaying && waveActive)
            {
                Gizmos.color = Color.white;
                Vector3 fr = o + f * front;
                Gizmos.DrawLine(fr - side * 11f, fr + side * 11f);
            }
        }

        void OnGUI()
        {
            if (!showDebugHud) return;
            GUI.skin.label.richText = true;

            const int W = 430;
            const int H = 235;
            GUI.Box(new Rect(10, 10, W, H), "");
            GUILayout.BeginArea(new Rect(22, 20, W - 24, H - 20));

            RoundSetup r = CurrentRound;
            GUILayout.Label("<b>Riverbed Flow</b>   " + phase.ToString());

            // 발판 상태
            string st = "";
            if (requiredStations != null)
            {
                for (int i = 0; i < requiredStations.Length; i++)
                {
                    bool occ = requiredStations[i] != null && requiredStations[i].IsOccupied;
                    st += (occ ? "<color=#7fff9f>●</color>" : "<color=#ff7f7f>○</color>") + " ";
                }
            }
            GUILayout.Label("발판 : " + (requireStations ? st : "<i>무시됨</i>"));

            GUILayout.Label("회차 : " + (r != null ? r.label : "-") + "   |   성공 " + successCount + " / " + rounds.Count);
            GUILayout.Label("판정 창 : ±" + CurrentWindow.ToString("F2") + "s"
                + (consecutiveFailures >= assistAfterFailures ? "  (어시스트 ON)" : ""));

            if (waveActive)
            {
                float toBeat = beatTime - Time.time;
                GUILayout.Label("전선 : " + front.ToString("F1") + "m / " + judgmentDistance.ToString("F1")
                    + "m   |   판정까지 " + toBeat.ToString("F2") + "s");

                float since = Time.time - lastPulseTime;
                string bar = since < 0.06f ? new string('█', Mathf.RoundToInt(lastPulseStrength * 20f)) : "";
                GUILayout.Label("<color=#7fd8ff>진동</color> : " + bar);
            }
            else
            {
                GUILayout.Label(phase == Phase.Idle
                    ? "두 사람이 발판에 서기를 기다리는 중"
                    : "다음 물결까지 " + Mathf.Max(0f, nextWaveTime - Time.time).ToString("F1") + "s");
                GUILayout.Label("");
            }

            GUILayout.Space(4);
            GUILayout.Label(Time.time - lastOutcomeTime < 2.5f ? "<b>" + lastOutcomeText + "</b>" : "");

            GUILayout.Space(4);
            GUILayout.Label("젖음 : " + Mathf.Max(wetActive, wetBaseline).ToString("F2")
                + "   (누적 " + wetBaseline.ToString("F2") + ")");
            GUILayout.Label("<i>" + debugKey + " 키로 입력</i>");

            GUILayout.EndArea();
        }
    }
}
