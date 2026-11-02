using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view handles for MovingPlatform: drag A and B to set the range of movement.
/// Hold Ctrl (Cmd on Mac) while dragging to snap to the grid.
/// </summary>
[CustomEditor(typeof(MovingPlatform))]
public class MovingPlatformEditor : Editor
{
    private SerializedProperty startOffset;
    private SerializedProperty endOffset;
    private SerializedProperty speed;
    private SerializedProperty pauseAtEnds;

    private void OnEnable()
    {
        startOffset = serializedObject.FindProperty("startOffset");
        endOffset = serializedObject.FindProperty("endOffset");
        speed = serializedObject.FindProperty("speed");
        pauseAtEnds = serializedObject.FindProperty("pauseAtEnds");
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var platform = (MovingPlatform)target;
        float length = platform.PathLength;
        float oneWay = speed.floatValue > 0f ? length / speed.floatValue : 0f;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            $"Path length: {length:0.##} units\n" +
            $"One way: {oneWay:0.##} s   Full loop: {(oneWay + pauseAtEnds.floatValue) * 2f:0.##} s\n" +
            "Drag the A / B handles in the Scene view to change the range.",
            MessageType.None);
    }

    private void OnSceneGUI()
    {
        var platform = (MovingPlatform)target;
        serializedObject.Update();

        Vector2 home = platform.Home;
        float z = platform.transform.position.z;

        DrawPointHandle("A", home, z, startOffset, platform.Axis);
        DrawPointHandle("B", home, z, endOffset, platform.Axis);

        serializedObject.ApplyModifiedProperties(); // records Undo automatically
    }

    private static void DrawPointHandle(string label, Vector2 home, float z,
                                        SerializedProperty offsetProp, MovingPlatform.PathAxis axis)
    {
        Vector2 offset = offsetProp.vector2Value;
        Vector3 worldPos = new Vector3(home.x + offset.x, home.y + offset.y, z);

        Handles.color = new Color(1f, 0.8f, 0.2f, 1f);
        Handles.Label(worldPos + Vector3.up * 0.35f, label, EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        Vector3 newPos = Handles.PositionHandle(worldPos, Quaternion.identity);
        if (!EditorGUI.EndChangeCheck()) return;

        Vector2 newOffset = new Vector2(newPos.x - home.x, newPos.y - home.y);

        if (axis == MovingPlatform.PathAxis.Horizontal) newOffset.y = 0f;
        else if (axis == MovingPlatform.PathAxis.Vertical) newOffset.x = 0f;

        offsetProp.vector2Value = newOffset;
    }
}