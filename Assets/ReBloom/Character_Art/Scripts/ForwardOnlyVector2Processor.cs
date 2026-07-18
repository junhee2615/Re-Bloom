using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
public sealed class ForwardOnlyVector2Processor : InputProcessor<Vector2>
{
#if UNITY_EDITOR
    static ForwardOnlyVector2Processor()
    {
        RegisterProcessor();
    }
#endif

    [RuntimeInitializeOnLoadMethod(
        RuntimeInitializeLoadType.BeforeSplashScreen)]
    private static void RegisterProcessor()
    {
        InputSystem.RegisterProcessor<ForwardOnlyVector2Processor>();
    }

    public override Vector2 Process(
        Vector2 value,
        InputControl control)
    {
        return new Vector2(
            0f,
            Mathf.Max(0f, value.y)
        );
    }
}