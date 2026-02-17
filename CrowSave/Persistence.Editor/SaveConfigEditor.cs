#if UNITY_EDITOR
using CrowSave.Persistence.Runtime;
using CrowSave.Persistence.Save;
using UnityEditor;
using UnityEngine;

namespace CrowSave.Persistence.Save.Editor
{
    [CustomEditor(typeof(SaveConfig))]
    public sealed class SaveConfigEditor : UnityEditor.Editor
    {
        private bool _showQuickHelp = false;
        private bool _showRuntime = true;

        private static GUIStyle _headerStyle;
        private static Color _blueAccent = new Color(0.2f, 0.6f, 1f);

        private void InitStyles()
        {
            if (_headerStyle != null) return;
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Color.white } };
        }

        public override void OnInspectorGUI()
        {
            InitStyles();
            var cfg = (SaveConfig)target;

            DrawBanner();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _showQuickHelp = EditorGUILayout.Foldout(_showQuickHelp, "SYSTEM HELP", true, EditorStyles.foldoutHeader);
                if (_showQuickHelp)
                {
                    GUILayout.Space(2);
                    DrawHelpLine("RAM (Always)", "Backtracking between scenes in current session.", "d_FilterByLabel");
                    DrawHelpLine("Disk", "Slots, checkpoints, and persistent file storage.", "SaveAs");
                    DrawHelpLine("Global Scope", $"Use PersistentId.Global Scope -> '{PersistentId.GlobalScopeKey}' to persist across scenes.", "d_Favorite");
                    DrawHelpLine("Policy", "Decides if loading triggers a scene transition.", "SceneAsset Icon");
                    GUILayout.Space(4);
                }
            }

            GUILayout.Space(10);
            DrawSeparatorThin();

            DrawDefaultInspector();

            GUILayout.Space(10);
            DrawWarnings(cfg);

            GUILayout.Space(10);
            DrawSceneGuidManagerLink(); // <-- NEW

            GUILayout.Space(15);
            DrawRuntimeButtons();

            if (GUI.changed)
                EditorUtility.SetDirty(cfg);
        }

        private void DrawBanner()
        {
            Rect headerRect = EditorGUILayout.GetControlRect(false, 40);
            EditorGUI.DrawRect(headerRect, new Color(0.15f, 0.15f, 0.15f, 1f));
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 2, headerRect.width, 2), _blueAccent);
            GUI.Label(new Rect(headerRect.x + 10, headerRect.y + 10, 300, 20), "SAVE ORCHESTRATOR CONFIG", _headerStyle);
        }

        private void DrawHelpLine(string title, string desc, string icon)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(15);
                var content = EditorGUIUtility.IconContent(icon);
                if (content == null || content.image == null) content = EditorGUIUtility.IconContent("d_DefaultSorting");
                GUILayout.Label(content, GUILayout.Width(18), GUILayout.Height(18));
                EditorGUILayout.LabelField($"<b>{title}:</b> {desc}", new GUIStyle(EditorStyles.wordWrappedMiniLabel) { richText = true });
            }
        }

        private static void DrawWarnings(SaveConfig cfg)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(5);
                using (new EditorGUILayout.VerticalScope())
                {
                    if (cfg.captureMode == CaptureMode.DirtyOnly)
                        EditorGUILayout.HelpBox("DirtyOnly: Entities must manually call MarkDirty().", MessageType.Info);

                    if (cfg.checkpointOnSceneTransition && cfg.checkpointRingSize == 0)
                        EditorGUILayout.HelpBox("Checkpoints enabled, but Ring Size is 0.", MessageType.Warning);

                    if (cfg.autosaveOnTransition && cfg.autosaveSlot < 0)
                        EditorGUILayout.HelpBox("Invalid Autosave Slot (must be >= 0).", MessageType.Error);

                    if (cfg.minLoadingScreenTime < 0f || cfg.holdAtFullTime < 0f)
                        EditorGUILayout.HelpBox("Loading times cannot be negative.", MessageType.Warning);
                }
                GUILayout.Space(5);
            }
        }

        private void DrawSceneGuidManagerLink()
        {
            bool guidMode = IsGuidSceneIdModeSelected(serializedObject);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(EditorGUIUtility.IconContent("SceneAsset Icon"), GUILayout.Width(18), GUILayout.Height(18));
                    EditorGUILayout.LabelField("Scene GUID Tools", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Open Scene GUID Manager…", GUILayout.Height(22), GUILayout.Width(200)))
                        CrowSaveSceneGuidManagerWindow.Open();
                }

                if (guidMode)
                {
                    EditorGUILayout.HelpBox(
                        "You have Scene GUID mode selected. Ensure all Build Settings scenes contain a SceneGuid component (use the manager window to add missing).",
                        MessageType.Warning
                    );
                }
                else
                {
                    EditorGUILayout.LabelField(
                        "Optional: If you switch to Scene GUID mode later, use the manager window to add SceneGuid to scenes in Build Settings.",
                        EditorStyles.wordWrappedMiniLabel
                    );
                }
            }
        }

        // Heuristic detector so we don’t hard-depend on your exact field name.
        private static bool IsGuidSceneIdModeSelected(SerializedObject so)
        {
            // Common patterns:
            // - enum field: sceneIdMode / sceneKeyMode / sceneIdStrategy
            // - bool field: useSceneGuid / useSceneGuidComponent
            // - string field: sceneKeyMode = "Guid" (less common)

            // bool flags
            if (TryFindBool(so, out bool b,
                    "useSceneGuid", "useSceneGuidComponent", "useSceneGuidMode", "useSceneGuidForScenes"))
                return b;

            // enum fields: treat index/name containing "guid" as GUID mode
            if (TryFindEnum(so, out string enumName,
                    "sceneIdMode", "sceneKeyMode", "sceneIdStrategy", "sceneScopeIdMode", "sceneKeyStrategy"))
            {
                if (!string.IsNullOrWhiteSpace(enumName) && enumName.ToLowerInvariant().Contains("guid"))
                    return true;
            }

            return false;
        }

        private static bool TryFindBool(SerializedObject so, out bool value, params string[] names)
        {
            value = false;
            for (int i = 0; i < names.Length; i++)
            {
                var p = so.FindProperty(names[i]);
                if (p != null && p.propertyType == SerializedPropertyType.Boolean)
                {
                    value = p.boolValue;
                    return true;
                }
            }
            return false;
        }

        private static bool TryFindEnum(SerializedObject so, out string enumDisplayName, params string[] names)
        {
            enumDisplayName = null;

            for (int i = 0; i < names.Length; i++)
            {
                var p = so.FindProperty(names[i]);
                if (p == null) continue;
                if (p.propertyType != SerializedPropertyType.Enum) continue;

                int idx = p.enumValueIndex;

                // Prefer the nice display names (Unity Inspector labels)
                var display = p.enumDisplayNames;
                if (display != null && idx >= 0 && idx < display.Length)
                {
                    enumDisplayName = display[idx];
                    return true;
                }

                // Fallback: raw enum names
                var raw = p.enumNames;
                if (raw != null && idx >= 0 && idx < raw.Length)
                {
                    enumDisplayName = raw[idx];
                    return true;
                }

                // Last resort (should be rare)
                enumDisplayName = idx.ToString();
                return true;
            }

            return false;
        }

        private void DrawRuntimeButtons()
        {
            _showRuntime = EditorGUILayout.Foldout(_showRuntime, "DEBUG RUNTIME CONTROLS", true, EditorStyles.foldoutHeader);
            if (!_showRuntime) return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (!Application.isPlaying)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.LabelField("Play mode required for debug controls.", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.Space(5);
                    return;
                }

                if (!PersistenceServices.IsReady)
                {
                    EditorGUILayout.HelpBox("System is not bootstrapped.", MessageType.Warning);
                    return;
                }

                var orch = SaveOrchestrator.Instance;
                if (orch == null) return;

                GUILayout.Space(5);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("CHECKPOINT", EditorStyles.miniButtonLeft, GUILayout.Height(24))) orch.Checkpoint("editor");
                    if (GUILayout.Button("SAVE SLOT 0", EditorStyles.miniButtonMid, GUILayout.Height(24))) orch.SaveSlot(0);
                    if (GUILayout.Button("LOAD SLOT 0", EditorStyles.miniButtonRight, GUILayout.Height(24))) orch.LoadSlot(0);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("LOAD (STAY)", EditorStyles.miniButtonLeft)) orch.LoadSlot(0, false);
                    if (GUILayout.Button("LOAD (TRAVEL)", EditorStyles.miniButtonRight)) orch.LoadSlot(0, true);
                }

                GUILayout.Space(5);
                DrawSeparatorThin();

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(" TOGGLE HUD", EditorGUIUtility.IconContent("d_ViewToolOrbit").image)))
                    {
                        var hud = FindFirstObjectByType<PersistenceDebugHUD>();
                        if (hud != null) hud.visible = !hud.visible;
                    }

                    if (GUILayout.Button(new GUIContent(" CLEAR LOG", EditorGUIUtility.IconContent("d_TreeEditor.Trash").image)))
                        PersistenceLog.ClearEvents();
                }

                GUILayout.Space(2);
            }
        }

        private void DrawSeparatorThin()
        {
            Rect r = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(r, new Color(1, 1, 1, 0.1f));
            GUILayout.Space(5);
        }
    }
}
#endif
