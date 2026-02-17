#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using CrowSave.Persistence.Runtime;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PersistentId))]
[CanEditMultipleObjects]
public sealed class PersistentIdInspector : Editor
{
    private static GUIStyle _headerStyle;
    private static GUIStyle _boxStyle;
    private static readonly Color Accent = new Color(0.2f, 0.6f, 1f);
    private static readonly Color Danger = new Color(1f, 0.35f, 0.35f);
    private static readonly Color Warn   = new Color(1f, 0.85f, 0.4f);

    private SerializedProperty _entityIdProp;
    private SerializedProperty _globalScopeProp;
    private SerializedProperty _scopeOverrideProp;

    private float _copyFeedbackTime;

    private void OnEnable()
    {
        _entityIdProp = serializedObject.FindProperty("entityId");
        _globalScopeProp = serializedObject.FindProperty("globalScope");
        _scopeOverrideProp = serializedObject.FindProperty("scopeOverride");
    }

    private static void InitStyles()
    {
        if (_headerStyle != null) return;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12, normal = { textColor = Color.white } };
        _boxStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 8, 8) };
    }

    public override void OnInspectorGUI()
    {
        InitStyles();
        serializedObject.Update();

        DrawIdSection();
        DrawScopeSection();
        DrawToolsStrip();

        // Quick validation summary (simple): missing IDs + duplicate IDs (global scan)
        if (targets.Length == 1)
        {
            DrawQuickValidationSingle((PersistentId)target);
        }
        else
        {
            DrawQuickValidationMulti(targets.Cast<PersistentId>().ToArray());
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ---------------- Sections ----------------

    private void DrawIdSection()
    {
        var anyMissing = targets.Any(o => ((PersistentId)o) != null && !((PersistentId)o).HasValidId);

        using (new EditorGUILayout.VerticalScope(_boxStyle))
        {
            var bar = EditorGUILayout.GetControlRect(false, 2);
            EditorGUI.DrawRect(bar, anyMissing ? Danger : Accent);
            GUILayout.Space(4);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("DATA IDENTITY", _headerStyle);

                if (Time.realtimeSinceStartup < _copyFeedbackTime + 1.5f)
                {
                    var feedback = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        normal = { textColor = Accent }
                    };
                    EditorGUILayout.LabelField("COPIED TO CLIPBOARD", feedback);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PropertyField(_entityIdProp, new GUIContent("ID String"));

                using (new EditorGUI.DisabledScope(targets.Length != 1))
                {
                    if (GUILayout.Button(new GUIContent("COPY", "Copy ID to Clipboard"),
                        EditorStyles.miniButton, GUILayout.Width(50), GUILayout.Height(18)))
                    {
                        var pid = (PersistentId)target;
                        EditorGUIUtility.systemCopyBuffer = pid.EntityId ?? "";
                        _copyFeedbackTime = Time.realtimeSinceStartup;
                        EditorApplication.delayCall += Repaint;
                    }
                }
            }
        }
    }

    private void DrawScopeSection()
    {
        using (new EditorGUILayout.VerticalScope(_boxStyle))
        {
            EditorGUILayout.LabelField("SCOPE", _headerStyle);
            GUILayout.Space(2);

            EditorGUILayout.PropertyField(_globalScopeProp, new GUIContent("Global Scope"));
            EditorGUILayout.PropertyField(_scopeOverrideProp, new GUIContent("Scope Override"));

            GUILayout.Space(4);

            if (targets.Length == 1)
            {
                var pid = (PersistentId)target;

                string note;
                if (!string.IsNullOrEmpty(_scopeOverrideProp.stringValue))
                    note = "Override is set: it takes precedence over Global/Scene scope.";
                else if (_globalScopeProp.boolValue)
                    note = $"Global scope enabled: entities register under '{PersistentId.GlobalScopeKey}'.";
                else
                    note = "Scene scope: key is computed from SaveOrchestrator config (scene identity mode).";

                EditorGUILayout.HelpBox($"Effective ScopeKey: {SafeScopeKey(pid)}\n\n{note}", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("Multi-edit: Effective ScopeKey depends on runtime config & active scene.", MessageType.None);
            }
        }
    }

    private void DrawToolsStrip()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            // Generate (for missing only)
            if (GUILayout.Button(new GUIContent(" Generate", EditorGUIUtility.IconContent("d_CreateAddNew").image), GUILayout.Height(22)))
            {
                foreach (var o in targets)
                {
                    var pid = (PersistentId)o;
                    if (pid == null) continue;

                    Undo.RecordObject(pid, "Generate Persistent ID");
                    pid.EditorGenerateIfMissing();
                    EditorUtility.SetDirty(pid);
                }
            }

            // Validate Scene -> open validator only if issues
            if (GUILayout.Button(new GUIContent(" Quick Scan", EditorGUIUtility.IconContent("d_FilterSelectedOnly").image), GUILayout.Height(22)))
            {
                var report = ScanOpenScene();
                if (report.HasIssues)
                {
                    if (EditorUtility.DisplayDialog(
                            "CrowSave: Issues Detected",
                            $"{report.Summary}\n\nOpen Tools/CrowSave/Validator?",
                            "Open Validator",
                            "Close"))
                    {
                        EditorApplication.ExecuteMenuItem("Tools/CrowSave/Validator");
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("CrowSave", "✔ No missing IDs and no duplicate IDs found in open scene.", "OK");
                }
            }

            // Regenerate (danger)
            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Refresh"), GUILayout.Width(30), GUILayout.Height(22)))
            {
                if (EditorUtility.DisplayDialog("Regenerate ID?",
                        "This will BREAK existing saves for this object. Continue?",
                        "Regenerate", "Cancel"))
                {
                    foreach (var o in targets)
                    {
                        var pid = (PersistentId)o;
                        if (pid == null) continue;

                        Undo.RecordObject(pid, "Regenerate Persistent ID");
                        pid.EditorRegenerate();
                        EditorUtility.SetDirty(pid);
                    }
                }
            }
        }
    }

    // ---------------- Quick Validation UI ----------------

    private void DrawQuickValidationSingle(PersistentId pid)
    {
        if (pid == null) return;

        // Missing ID warning (inline)
        if (!pid.HasValidId)
        {
            EditorGUILayout.HelpBox("Missing Entity ID. This object will be ignored by the save system.", MessageType.Error);
            DrawOpenValidatorHint();
            return;
        }

        // Duplicate warning (simple global duplicate scan)
        var dup = FindAnyDuplicate(pid);
        if (dup != null)
        {
            EditorGUILayout.HelpBox(
                $"Duplicate EntityId detected in open scene.\n" +
                $"This object conflicts with '{dup.name}'.\n\n" +
                $"Use Tools/CrowSave/Validator to inspect and fix conflicts.",
                MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping Other", GUILayout.Height(18)))
                {
                    EditorGUIUtility.PingObject(dup);
                    Selection.activeObject = dup;
                }

                if (GUILayout.Button("Open Validator", GUILayout.Height(18)))
                {
                    EditorApplication.ExecuteMenuItem("Tools/CrowSave/Validator");
                }
            }

            return;
        }

        // Small info line: your scope now depends on runtime config
        EditorGUILayout.HelpBox(
            "ScopeKey is computed using SaveOrchestrator's SaveConfig.sceneIdentityMode (unless overridden/global).",
            MessageType.Info);
    }

    private void DrawQuickValidationMulti(PersistentId[] pids)
    {
        int missing = pids.Count(p => p != null && !p.HasValidId);
        int dupes = CountDuplicateIdsInOpenScene();

        if (missing > 0)
            EditorGUILayout.HelpBox($"{missing} selected object(s) are missing EntityId.", MessageType.Error);

        if (dupes > 0)
            EditorGUILayout.HelpBox($"Duplicate IDs exist in the open scene ({dupes} conflict group(s)). Use the Validator to inspect.", MessageType.Warning);

        if (missing > 0 || dupes > 0)
            DrawOpenValidatorHint();
    }

    private void DrawOpenValidatorHint()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Tools/CrowSave/Validator", GUILayout.Height(20)))
                EditorApplication.ExecuteMenuItem("Tools/CrowSave/Validator");
            GUILayout.FlexibleSpace();
        }
    }

    // ---------------- Scanning ----------------

    private sealed class ScanReport
    {
        public int Missing;
        public int DuplicateGroups;
        public bool HasIssues => Missing > 0 || DuplicateGroups > 0;
        public string Summary =>
            $"Missing IDs: {Missing}\nDuplicate ID groups: {DuplicateGroups}";
    }

    private static ScanReport ScanOpenScene()
    {
        // Matches your validator approach: open, loaded scene objects only (not prefab assets)
        var all = Resources.FindObjectsOfTypeAll<PersistentId>()
            .Where(p => p != null && p.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(p))
            .ToList();

        int missing = all.Count(p => !p.HasValidId);

        int dupGroups = all
            .Where(p => p.HasValidId)
            .GroupBy(p => p.EntityId)
            .Count(g => g.Count() > 1);

        return new ScanReport { Missing = missing, DuplicateGroups = dupGroups };
    }

    private static int CountDuplicateIdsInOpenScene()
    {
        var all = Resources.FindObjectsOfTypeAll<PersistentId>()
            .Where(p => p != null && p.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(p))
            .Where(p => p.HasValidId)
            .ToList();

        return all.GroupBy(p => p.EntityId).Count(g => g.Count() > 1);
    }

    private static PersistentId FindAnyDuplicate(PersistentId self)
    {
        if (self == null || !self.HasValidId) return null;

        var all = Resources.FindObjectsOfTypeAll<PersistentId>()
            .Where(p => p != null && p.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(p))
            .Where(p => p.HasValidId && p.EntityId == self.EntityId)
            .ToList();

        return all.FirstOrDefault(p => p != self);
    }

    private static string SafeScopeKey(PersistentId pid)
    {
        try { return pid != null ? (pid.ScopeKey ?? "") : ""; }
        catch { return ""; }
    }
}
#endif
