using ReBloom.Water;
using UnityEditor;
using UnityEngine;

/// <summary>RiverbedMissionNet 인스펙터에 디버그 버튼을 추가한다.</summary>
[CustomEditor(typeof(RiverbedMissionNet))]
public class RiverbedMissionNetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("디버그", EditorStyles.boldLabel);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("RiverbedFlow 클리어 + 텔레포터 활성화", GUILayout.Height(28)))
                ((RiverbedMissionNet)target).DebugClearAndActivateTeleporter();
        }

        EditorGUILayout.HelpBox(
            Application.isPlaying
                ? "미션을 클리어 처리하고 WaterfrontToFinalTeleporter를 컷씬 없이 바로 엽니다. " +
                  "멀티플레이에서는 호스트 에디터에서 눌러야 합니다."
                : "Play 모드에서만 사용할 수 있습니다.",
            MessageType.Info);
    }
}
