using System.Reflection;
using UnityEngine;

/// <summary>
/// 컷씬 동안 화면을 가리는 요소들을 잠시 끄고, 끝나면 되돌린다.
///
/// 안개는 두 겹이다.
///  1) Resonance의 FlatKit 안개 + Bloom/Vignette (ResonanceController가 매 프레임 제어)
///  2) 씬 자체의 RenderSettings 안개 (Stage2는 ExponentialSquared, density 0.015)
/// 둘 다 처리한다.
///
/// ResonanceController의 fog / volume / constraintEnabled 는 private이라
/// Stage1XRCutsceneRigFollower와 동일하게 리플렉션으로 접근한다.
/// (Stage1 쪽도 나중에 이 클래스로 옮기면 중복이 사라진다)
/// </summary>
public class ResonanceCutsceneOverride
{
    private const BindingFlags Flags =
        BindingFlags.Instance |
        BindingFlags.Public |
        BindingFlags.NonPublic;

    private ResonanceController controller;

    private FieldInfo constraintEnabledField;
    private FieldInfo fogField;
    private FieldInfo volumeField;

    private bool originalConstraintEnabled;
    private bool originalBuiltinFog;

    private bool builtinFogOverridden;
    private bool applied;

    // FreezeForFogClear 이후 SetFogAmount가 0~1 배율을 곱할 기준값.
    private FogConstraint frozenFog;
    private float frozenFogIntensity;
    private float originalBuiltinFogDensity;
    private bool builtinFogDensityOverridden;

    public bool Applied => applied;

    // =================================================
    // Resolve
    // =================================================

    private bool Resolve()
    {
        if (controller != null)
            return true;

        controller = Object.FindFirstObjectByType<ResonanceController>();

        if (controller == null)
            return false;

        constraintEnabledField =
            typeof(ResonanceController).GetField("constraintEnabled", Flags);

        fogField =
            typeof(ResonanceController).GetField("fog", Flags);

        volumeField =
            typeof(ResonanceController).GetField("volume", Flags);

        return true;
    }

    // =================================================
    // Disable
    // =================================================

    /// <summary>
    /// 컷씬 시작 시 호출. 화면이 완전히 검을 때 부르는 것이 안전하다.
    /// </summary>
    public void Disable(bool disableBuiltinFog)
    {
        if (applied)
            return;

        applied = true;

        // 1) 씬 자체의 RenderSettings 안개
        if (disableBuiltinFog)
        {
            originalBuiltinFog = RenderSettings.fog;
            RenderSettings.fog = false;
            builtinFogOverridden = true;
        }

        // 2) Resonance 제약
        if (!Resolve())
            return;

        if (constraintEnabledField == null)
            return;

        originalConstraintEnabled =
            (bool)constraintEnabledField.GetValue(controller);

        // Update()가 매 프레임 안개를 다시 밀어넣지 않도록 먼저 멈춘다.
        constraintEnabledField.SetValue(controller, false);

        // FlatKit 안개 강도 0
        object fog = fogField != null
            ? fogField.GetValue(controller)
            : null;

        if (fog != null)
        {
            MethodInfo applyMethod =
                fog.GetType().GetMethod("Apply", Flags);

            if (applyMethod != null)
                applyMethod.Invoke(fog, new object[] { 0f });
        }

        // Bloom / Vignette 완화
        object volume = volumeField != null
            ? volumeField.GetValue(controller)
            : null;

        if (volume != null)
        {
            MethodInfo snapMethod =
                volume.GetType().GetMethod("Snap", Flags);

            if (snapMethod != null)
                snapMethod.Invoke(volume, new object[] { true });

            MethodInfo postFxMethod =
                volume.GetType().GetMethod("ApplyPostFx", Flags);

            if (postFxMethod != null)
                postFxMethod.Invoke(volume, new object[] { true });
        }
    }

    // =================================================
    // 감각 제약 완전 해제 (엔딩용)
    // =================================================

    /// <summary>
    /// 역할별 감각 제약을 끝까지 걷어 낸다. 엔딩처럼 다시 제약으로 돌아가지 않는 연출에서 쓴다.
    ///  - mental: Resonance Global Volume의 weight를 0으로 — 해제 상태 weight(0.6)에 남는
    ///    블러·어두운 색 보정까지 없앤다(Bloom/Vignette만 끄는 <see cref="Disable"/>보다 강하다).
    ///  - ear: 제약 오디오를 일반 믹서 그룹으로 돌리고 스냅샷을 완화 쪽 끝으로 — 먹먹함이 사라진다.
    /// <see cref="FreezeForFogClear"/> 또는 <see cref="Disable"/> 뒤에 부른다(Update가 다시 덮어쓰지 않도록).
    /// </summary>
    public void ClearSensoryConstraints()
    {
        if (!Resolve())
            return;

        if (volumeField?.GetValue(controller) is VolumeConstraint volume)
        {
            FieldInfo resonanceVolumeField = typeof(VolumeConstraint).GetField("resonanceVolume", Flags);
            if (resonanceVolumeField?.GetValue(volume) is UnityEngine.Rendering.Volume resonanceVolume)
                resonanceVolume.weight = 0f;

            volume.ApplyPostFx(true);
        }

        FieldInfo audioField = typeof(ResonanceController).GetField("audioConstraint", Flags);
        if (audioField?.GetValue(controller) is AudioConstraint audio)
        {
            audio.Apply(true);
            audio.UpdateRelief(1f);
        }
    }

    // =================================================
    // Freeze (안개가 걷히는 연출용)
    // =================================================

    /// <summary>
    /// Disable과 달리 안개를 끄지 않고 <b>짙게 깐 채로</b> Resonance 제어만 멈춘다.
    /// 이후 <see cref="SetFogAmount"/>로 안개를 직접 걷어낸다. 화면이 검을 때 부른다.
    /// </summary>
    /// <param name="fogStrength">FlatKit 안개 기본 강도(ActiveIntensity)에 곱할 배율.</param>
    public void FreezeForFogClear(float fogStrength)
    {
        if (applied)
            return;

        applied = true;

        originalBuiltinFog = RenderSettings.fog;
        builtinFogOverridden = true;
        originalBuiltinFogDensity = RenderSettings.fogDensity;
        builtinFogDensityOverridden = true;

        if (Resolve() && constraintEnabledField != null)
        {
            originalConstraintEnabled =
                (bool)constraintEnabledField.GetValue(controller);

            // Update()가 매 프레임 안개를 다시 밀어넣지 않도록 멈춘다.
            constraintEnabledField.SetValue(controller, false);

            frozenFog = fogField != null
                ? fogField.GetValue(controller) as FogConstraint
                : null;

            // 공명이 이미 풀려 안개가 0이어도 연출 시작에는 짙은 안개를 깐다.
            if (frozenFog != null)
                frozenFogIntensity = frozenFog.ActiveIntensity * Mathf.Max(0f, fogStrength);

            // 시야 제약(Bloom/Vignette)은 엔딩에서 걷어 둔다.
            object volume = volumeField != null
                ? volumeField.GetValue(controller)
                : null;

            if (volume != null)
            {
                MethodInfo snapMethod =
                    volume.GetType().GetMethod("Snap", Flags);

                if (snapMethod != null)
                    snapMethod.Invoke(volume, new object[] { true });

                MethodInfo postFxMethod =
                    volume.GetType().GetMethod("ApplyPostFx", Flags);

                if (postFxMethod != null)
                    postFxMethod.Invoke(volume, new object[] { true });
            }
        }

        SetFogAmount(1f);
    }

    /// <summary>
    /// <see cref="FreezeForFogClear"/> 이후에만 의미가 있다. 1 = 시작 안개, 0 = 완전히 걷힘.
    /// FlatKit 안개와 씬 RenderSettings 안개를 같은 배율로 줄인다.
    /// </summary>
    public void SetFogAmount(float amount)
    {
        if (!applied)
            return;

        amount = Mathf.Clamp01(amount);

        if (frozenFog != null)
            frozenFog.Apply(frozenFogIntensity * amount);

        if (builtinFogDensityOverridden)
            RenderSettings.fogDensity = originalBuiltinFogDensity * amount;
    }

    // =================================================
    // Restore
    // =================================================

    /// <summary>
    /// 컷씬 종료 시 호출. 마찬가지로 화면이 검을 때가 안전하다.
    /// 여러 번 불러도 안전하다.
    /// </summary>
    public void Restore()
    {
        if (!applied)
            return;

        applied = false;

        if (builtinFogOverridden)
        {
            RenderSettings.fog = originalBuiltinFog;
            builtinFogOverridden = false;
        }

        if (builtinFogDensityOverridden)
        {
            RenderSettings.fogDensity = originalBuiltinFogDensity;
            builtinFogDensityOverridden = false;
        }

        frozenFog = null;

        if (controller == null || constraintEnabledField == null)
            return;

        constraintEnabledField.SetValue(
            controller,
            originalConstraintEnabled);

        // Bloom/Vignette/오디오 즉시 복구.
        // FlatKit 안개는 constraintEnabled가 다시 켜진 뒤
        // 다음 Update의 Tick에서 서서히 돌아온다.
        MethodInfo applyEffectsMethod =
            typeof(ResonanceController).GetMethod("ApplyConstraintEffects", Flags);

        if (applyEffectsMethod != null)
            applyEffectsMethod.Invoke(controller, null);
    }
}
