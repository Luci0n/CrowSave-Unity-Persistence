#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using CrowSave.Persistence.Runtime;
using UnityEditor;
using UnityEngine;

public sealed class PersistenceValidatorWindow : EditorWindow
{
    private Vector2 _scroll;
    private string _searchFilter = "";
    private readonly List<ValidationResult> _results = new List<ValidationResult>();
    private string _status = "Ready to audit.";

    private static GUIStyle _headerStyle;
    private static GUIStyle _rowStyle;
    private static GUIStyle _idLabelStyle;
    private static Color _blueAccent = new Color(0.2f, 0.6f, 1f);
    private static Color _errorAccent = new Color(1f, 0.35f, 0.35f);
    private static Color _warnAccent = new Color(1f, 0.85f, 0.4f);

    private class ValidationResult
    {
        public PersistentId target;
        public string idString;
        public MessageType type;
        public string conflictName; // Stores the name of the object it's clashing with
    }

    [MenuItem("Tools/CrowSave/Validator")]
    public static void Open() => GetWindow<PersistenceValidatorWindow>("ID Auditor").minSize = new Vector2(400, 500);

    private void InitStyles()
    {
        if (_headerStyle != null) return;
        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, normal = { textColor = Color.white } };
        _rowStyle = new GUIStyle(EditorStyles.helpBox) { margin = new RectOffset(10, 10, 4, 4), padding = new RectOffset(8, 8, 8, 8) };
        _idLabelStyle = new GUIStyle(EditorStyles.label) 
        { 
            fontSize = 10, 
            fontStyle = FontStyle.Italic,
            normal = { textColor = new Color(0.7f, 0.7f, 0.7f) } 
        };
    }

    private void OnGUI()
    {
        InitStyles();
        DrawBanner();

        bool currentHasErrors = _results.Any(r => r.type == MessageType.Error || r.type == MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(new GUIContent(" Scan Scene", EditorGUIUtility.IconContent("d_SceneAsset Icon").image), EditorStyles.toolbarButton))
                Validate(false);

            if (GUILayout.Button(new GUIContent(" Scan Selection", EditorGUIUtility.IconContent("d_Prefab Icon").image), EditorStyles.toolbarButton))
                Validate(true);
            
            GUILayout.FlexibleSpace();
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            var searchIcon = EditorGUIUtility.IconContent("Search Icon") ?? EditorGUIUtility.IconContent("d_SearchOverlay");
            GUILayout.Label(searchIcon, GUILayout.Width(20));
            _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField);
            
            if (!string.IsNullOrEmpty(_searchFilter))
            {
                if (GUILayout.Button("", "ToolbarSearchCancelButton")) { _searchFilter = ""; GUI.FocusControl(null); }
            }
            else { GUILayout.Label("", "ToolbarSearchCancelButtonEmpty"); }

            if (GUILayout.Button(new GUIContent(" Clear", EditorGUIUtility.IconContent("d_Refresh").image), EditorStyles.toolbarButton, GUILayout.Width(60))) 
            { 
                _results.Clear(); 
                _status = "Cleared."; 
            }
        }

        if (_results.Any(r => r.type == MessageType.Error))
        {
            GUI.backgroundColor = _errorAccent;
            if (GUILayout.Button("FIX ALL MISSING IDENTIFIERS", GUILayout.Height(30))) FixAllMissing();
            GUI.backgroundColor = Color.white;
        }

        Rect statusRect = EditorGUILayout.GetControlRect(false, 22);
        EditorGUI.DrawRect(statusRect, currentHasErrors ? new Color(0.4f, 0.15f, 0.15f, 1f) : new Color(0, 0, 0, 0.3f));
        var statusStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
        if (currentHasErrors) statusStyle.normal.textColor = Color.white;
        GUI.Label(statusRect, _status.ToUpper(), statusStyle);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        GUILayout.Space(10);

        var filtered = _results.Where(r => 
            r.target != null && (
            string.IsNullOrEmpty(_searchFilter) || 
            r.target.name.ToLower().Contains(_searchFilter.ToLower()) || 
            r.idString.ToLower().Contains(_searchFilter.ToLower()))).ToList();

        if (filtered.Count > 0)
        {
            foreach (var res in filtered) DrawResultRow(res);
        }
        else if (_results.Count > 0)
        {
            GUILayout.Space(20);
            EditorGUILayout.LabelField("Search returned no results.", EditorStyles.centeredGreyMiniLabel);
        }
        else
        {
            DrawEmptyState();
        }

        GUILayout.Space(20);
        EditorGUILayout.EndScrollView();
    }

    private void DrawResultRow(ValidationResult res)
    {
        using (new EditorGUILayout.VerticalScope(_rowStyle))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string iconName = "d_FilterSelectedOnly";
                if (res.type == MessageType.Error) iconName = "d_console.erroricon";
                else if (res.type == MessageType.Warning) iconName = "d_console.warnicon";
                
                GUILayout.Label(EditorGUIUtility.IconContent(iconName), GUILayout.Width(24), GUILayout.Height(24));
                EditorGUILayout.ObjectField(res.target, typeof(PersistentId), true, GUILayout.Height(22));

                GUILayout.Space(10);

                if (GUILayout.Button("PING", EditorStyles.miniButtonLeft, GUILayout.Width(45), GUILayout.Height(22)))
                    EditorGUIUtility.PingObject(res.target);

                bool isError = res.type == MessageType.Error || res.type == MessageType.Warning;
                GUI.color = res.type == MessageType.Error ? Color.green : (res.type == MessageType.Warning ? _warnAccent : Color.white);
                if (GUILayout.Button(res.type == MessageType.Error ? "GEN" : "REGEN", EditorStyles.miniButtonRight, GUILayout.Width(50), GUILayout.Height(22)))
                    HandleAction(res.target, res.type == MessageType.Error);
                GUI.color = Color.white;
            }

            GUILayout.Space(6);
            Rect idRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(new Rect(idRect.x + 28, idRect.y, idRect.width - 28, 18), new Color(0, 0, 0, 0.25f));
            GUILayout.Space(34);
            
            if (res.type == MessageType.Error)
            {
                var style = new GUIStyle(_idLabelStyle) { normal = { textColor = _errorAccent }, fontStyle = FontStyle.Bold };
                EditorGUILayout.LabelField("CRITICAL: ID MISSING - OBJECT WILL NOT SAVE", style);
            }
            else if (res.type == MessageType.Warning)
            {
                var style = new GUIStyle(_idLabelStyle) { normal = { textColor = _warnAccent }, fontStyle = FontStyle.Bold };
                EditorGUILayout.LabelField($"CONFLICT: Duplicate ID used by '{res.conflictName}'", style);
            }
            else
            {
                EditorGUILayout.LabelField($"Persistent ID: {res.idString}", _idLabelStyle);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawBanner()
    {
        Rect headerRect = EditorGUILayout.GetControlRect(false, 45);
        EditorGUI.DrawRect(headerRect, new Color(0.12f, 0.12f, 0.12f, 1f));
        EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 2, headerRect.width, 2), _blueAccent);
        GUI.Label(new Rect(headerRect.x + 15, headerRect.y + 8, 300, 20), "PERSISTENCE AUDITOR", _headerStyle);
        GUI.Label(new Rect(headerRect.x + 15, headerRect.y + 24, 300, 20), "Global Conflict Validation", EditorStyles.miniLabel);
    }

    private void DrawEmptyState()
    {
        GUILayout.Space(50);
        EditorGUILayout.LabelField("Ready to Scan", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
        EditorGUILayout.LabelField("Scan scene or prefabs to audit identity integrity.", EditorStyles.centeredGreyMiniLabel);
    }

    private void FixAllMissing()
    {
        var missing = _results.Where(r => r.type == MessageType.Error).ToList();
        if (!EditorUtility.DisplayDialog("Bulk Fix", $"Generate IDs for {missing.Count} objects?", "Confirm", "Cancel")) return;
        
        Undo.RecordObjects(missing.Select(m => (Object)m.target).ToArray(), "Bulk Generate IDs");
        foreach (var m in missing)
        {
            m.target.EditorGenerateIfMissing();
            EditorUtility.SetDirty(m.target);
        }
        Validate(false);
    }

    private void HandleAction(PersistentId pid, bool isMissing)
    {
        if (!isMissing && !EditorUtility.DisplayDialog("Confirm Change", "This will invalidate existing save files.", "Regenerate", "Cancel")) return;
        Undo.RecordObject(pid, "ID Audit Change");
        if (isMissing) pid.EditorGenerateIfMissing(); else pid.EditorRegenerate();
        EditorUtility.SetDirty(pid);
        Validate(pid.gameObject.scene.name == null); 
    }

    private void Validate(bool selectionOnly)
    {
        _results.Clear();

        // 1. Get ALL IDs in the scene for global conflict checking
        var globalTargets = Resources.FindObjectsOfTypeAll<PersistentId>()
            .Where(p => p != null && p.gameObject.scene.isLoaded && !EditorUtility.IsPersistent(p))
            .ToList();

        // 2. Decide what to actually SHOW in the list
        var displayTargets = selectionOnly 
            ? Selection.gameObjects.SelectMany(go => go.GetComponentsInChildren<PersistentId>(true)).Distinct().ToList()
            : globalTargets;

        // 3. Create a map for quick conflict lookup
        var idMap = globalTargets.Where(p => p.HasValidId).GroupBy(p => p.EntityId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var p in displayTargets)
        {
            var res = new ValidationResult {
                target = p,
                idString = p.HasValidId ? p.EntityId : "NULL"
            };

            if (!p.HasValidId)
            {
                res.type = MessageType.Error;
            }
            else if (idMap.ContainsKey(p.EntityId) && idMap[p.EntityId].Count > 1)
            {
                res.type = MessageType.Warning;
                // Find the name of the OTHER object (not this one)
                var other = idMap[p.EntityId].FirstOrDefault(o => o != p);
                res.conflictName = other != null ? other.name : "Unknown Object";
            }
            else
            {
                res.type = MessageType.None;
            }
            _results.Add(res);
        }
        
        bool hasIssues = _results.Any(r => r.type != MessageType.None);
        _status = hasIssues ? "Audit Failed: Issues Detected" : $"Audit Passed: {displayTargets.Count} Objects Validated";
    }
}
#endif