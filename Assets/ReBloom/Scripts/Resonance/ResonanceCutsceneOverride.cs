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
