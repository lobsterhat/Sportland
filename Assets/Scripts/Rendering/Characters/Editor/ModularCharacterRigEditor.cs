using UnityEngine;
using UnityEditor;

namespace Sportland.Rendering.Characters.Editor
{
    /// <summary>
    /// Custom inspector for ModularCharacterRig.
    /// Adds buttons to rebuild / preview the rig directly in the Editor.
    /// </summary>
    [CustomEditor(typeof(ModularCharacterRig))]
    public class ModularCharacterRigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var rig = (ModularCharacterRig)target;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Preview Tools", EditorStyles.boldLabel);

            using (new EditorGUI.DisabledGroupScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Rebuild Rig (Play Mode Only)"))
                {
                    var appearance = serializedObject.FindProperty("appearance")
                        .objectReferenceValue as CharacterAppearance;

                    if (appearance != null)
                        rig.ApplyAppearance(appearance);
                    else
                        Debug.LogWarning("[ModularCharacterRig] No appearance assigned.");
                }

                EditorGUILayout.HelpBox(
                    "Drag a different CharacterAppearance into the 'Appearance' field " +
                    "while in Play Mode to hot-swap the character's look.",
                    MessageType.Info);
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter Play Mode to see the rig assemble and animate.",
                    MessageType.None);
            }
        }
    }
}
