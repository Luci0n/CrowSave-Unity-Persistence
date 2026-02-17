#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CrowSave.Persistence.Runtime;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrowSave.Persistence.Save.Editor
{
    public sealed class CrowSaveSceneGuidManagerWindow : EditorWindow
    {
        private const string SceneGuidRootName = "__CrowSave_SceneGuid__";

        // Legacy (pre-prefs) path, used for auto-migration to preserve asset references when possible.
        private const string LegacyRegistryAssetPath = "Assets/CrowSave/SceneGuidRegistry.asset";

        // Prefs
        private const string PrefPrefix = "CrowSave.SceneGuidManager.";
        private const string PrefAutoRescanOnFocus = PrefPrefix + "AutoRescanOnFocus";
        private const string PrefAutoGenerateRegistryOnScan = PrefPrefix + "AutoGenerateRegistryOnScan";
        private const string PrefShowFullGuids = PrefPrefix + "ShowFullGuids";

        private const string PrefRegistryFolder = PrefPrefix + "RegistryFolder";
        private const string PrefRegistryName = PrefPrefix + "RegistryName";
        private const string PrefRegistryFoldout = PrefPrefix + "RegistryFoldout";

        private const string DefaultRegistryFolder = "Assets/CrowSave/Registry";
        private const string DefaultRegistryName = "SceneGuidRegistry";

        // Layout
        private const float SceneNameWidth = 220f;
        private const float InBuildWidth = 60f;
        private const float EnabledWidth = 30f;
        private const float IndexWidth = 35f;
        private const float StatusWidth = 110f;
        private const float OpsWidth = 280f;

        private Vector2 _scroll;
        private readonly List<Row> _rows = new List<Row>();

        private bool _autoRescanOnFocus = true;
        private bool _autoGenerateRegistryOnScan = true;
        private bool _showFullGuids = false;

        // Registry output settings (prefs-backed)
        private string _registryFolder = DefaultRegistryFolder;
        private string _registryName = DefaultRegistryName;
        private bool _registryFoldout = true;

        private bool _pendingRegenerateAfterPrefsChange;

        // Styles
        private GUIStyle _headerStyle;
        private GUIStyle _rowOddStyle;
        private GUIStyle _rowEvenStyle;

        private GUIStyle _chipTextStyle;
        private GUIStyle _kvKeyStyle;
        private GUIStyle _guidFieldStyle;
        private GUIStyle _guidPanelStyle;

        private Texture2D _rowOddTex;
        private Texture2D _rowEvenTex;
        private Texture2D _guidPanelTex;

        private static class Colors
        {
            public static readonly Color HeaderBg = new Color(0.15f, 0.15f, 0.15f, 1f);
            public static readonly Color HeaderAccent = new Color(0.1f, 0.5f, 0.9f, 1f);

            public static readonly Color RowOddTint = new Color(1f, 1f, 1f, 0.04f);
            public static readonly Color RowEvenTint = new Color(0f, 0f, 0f, 0f);

            public static readonly Color AccentDanger = new Color(0.95f, 0.35f, 0.35f, 1f);
            public static readonly Color AccentWarn = new Color(0.95f, 0.75f, 0.20f, 1f);
            public static readonly Color AccentOk = new Color(0.35f, 0.85f, 0.45f, 1f);
            public static readonly Color AccentNeutral = new Color(0.60f, 0.60f, 0.60f, 1f);
        }

        private static class Icons
        {
            public static GUIContent Refresh => SafeIconContent("Refresh");
            public static GUIContent SaveAs => SafeIconContent("SaveAs");
            public static GUIContent Trash => SafeIconContent("TreeEditor.Trash");
            public static GUIContent Search => SafeIconContent("Search Icon");

            public static GUIContent Info => SafeIconContent("console.infoicon");
            public static GUIContent Warning => SafeIconContent("console.warnicon");
            public static GUIContent Error => SafeIconContent("console.erroricon");

            public static GUIContent Ping => SafeIconContent("d_ViewToolZoom");
            public static GUIContent Open => SafeIconContent("SceneAsset Icon");
            public static GUIContent Plus => SafeIconContent("d_Toolbar Plus");
            public static GUIContent Fix => SafeIconContent("d_FilterByType");
            public static GUIContent FoldoutOpen => SafeIconContent("IN Foldout On");
            public static GUIContent FoldoutClosed => SafeIconContent("IN Foldout");
            public static GUIContent Folder => SafeIconContent("Folder Icon");
        }

        private sealed class Row
        {
            public bool selected;
            public bool expanded;

            // Scene asset identity
            public string name;      // filename without extension
            public string path;      // Assets/.../Scene.unity (best known)
            public string assetGuid; // AssetDatabase GUID for the scene file (or registry GUID for orphans)

            // Build settings
            public bool presentInBuildSettings; // listed at all (enabled or disabled)
            public bool enabledInBuild;         // enabled checkbox
            public int buildIndex;              // runtime build index (enabled scenes only, packed). -1 if not enabled / not valid.

            // Inspection result
            public bool sceneFileMissing; // true if path doesn't exist on disk
            public bool hasSceneGuid;     // SceneGuid component exists in the scene
            public string foundGuid;      // guid field found in the component (if any)

            // Registry info
            public bool hasRegistryEntry;
            public bool registryOrphaned;
            public string status;

            public bool HasMismatch =>
                hasSceneGuid &&
                !string.IsNullOrWhiteSpace(assetGuid) &&
                !string.IsNullOrWhiteSpace(foundGuid) &&
                !string.Equals(assetGuid, foundGuid, StringComparison.OrdinalIgnoreCase);

            public bool NeedsAddOrFix => enabledInBuild && (!hasSceneGuid || HasMismatch);
            public bool CanOpenScene => !sceneFileMissing && !string.IsNullOrWhiteSpace(path) && File.Exists(path);
            public bool CanAddToBuild => !sceneFileMissing && !enabledInBuild;
        }

        [MenuItem("Tools/CrowSave/Scene GUID Manager")]
        public static void Open()
        {
            var w = GetWindow<CrowSaveSceneGuidManagerWindow>("CrowSave Scene GUIDs");
            w.minSize = new Vector2(1040, 420);
            w.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            Scan();
        }

        private void OnFocus()
        {
            if (_autoRescanOnFocus) Scan();
        }

        private void OnDisable()
        {
            DestroyTex(ref _rowOddTex);
            DestroyTex(ref _rowEvenTex);
            DestroyTex(ref _guidPanelTex);

            _headerStyle = null;
            _rowOddStyle = null;
            _rowEvenStyle = null;
            _chipTextStyle = null;
            _kvKeyStyle = null;
            _guidFieldStyle = null;
            _guidPanelStyle = null;
        }

        private static void DestroyTex(ref Texture2D t)
        {
            if (t == null) return;
            DestroyImmediate(t);
            t = null;
        }

        private void OnGUI()
        {
            InitStyles();

            DrawStylizedHeader();
            DrawToolbar();
            DrawRegistrySettingsPanel();

            if (_rows.Count == 0)
            {
                EditorGUILayout.HelpBox("No scene assets found in project.", MessageType.Warning);
                return;
            }

            DrawBulkActions();
            DrawTableHeader();

            using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = scroll.scrollPosition;

                for (int i = 0; i < _rows.Count; i++)
                    DrawRow(_rows[i], i);
            }

            DrawFooter();

            // If prefs changed and auto-registry is enabled, regenerate on a delayCall to avoid asset changes mid-IMGUI.
            if (_pendingRegenerateAfterPrefsChange)
            {
                _pendingRegenerateAfterPrefsChange = false;

                if (!EditorApplication.isPlayingOrWillChangePlaymode && _autoGenerateRegistryOnScan)
                {
                    EditorApplication.delayCall += () =>
                    {
                        GenerateRegistryAssetStatic(silent: true);
                        // If the window still exists, refresh the UI.
                        if (this != null)
                        {
                            Scan();
                            Repaint();
                        }
                    };
                }
            }
        }

        private void InitStyles()
        {
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleLeft
            };
            _headerStyle.normal.textColor = Color.white;

            _rowOddTex = CreateTexture(2, 2, Colors.RowOddTint);
            _rowEvenTex = CreateTexture(2, 2, Colors.RowEvenTint);

            _rowOddStyle = new GUIStyle(GUI.skin.box);
            _rowOddStyle.normal.background = _rowOddTex;
            _rowOddStyle.padding = new RectOffset(6, 6, 4, 4);

            _rowEvenStyle = new GUIStyle(GUI.skin.box);
            _rowEvenStyle.normal.background = _rowEvenTex;
            _rowEvenStyle.padding = new RectOffset(6, 6, 4, 4);

            _chipTextStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                margin = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0)
            };
            _chipTextStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.92f, 0.92f, 0.92f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f);

            _kvKeyStyle = new GUIStyle(EditorStyles.miniLabel);
            _kvKeyStyle.normal.textColor = EditorGUIUtility.isProSkin
                ? new Color(0.72f, 0.72f, 0.72f, 1f)
                : new Color(0.30f, 0.30f, 0.30f, 1f);
            _guidFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                padding = new RectOffset(6, 6, 3, 3),
                margin = new RectOffset(0, 0, 0, 0)
            };

            Color normalText = EditorGUIUtility.isProSkin
                ? new Color(0.90f, 0.90f, 0.90f, 1f)
                : new Color(0.12f, 0.12f, 0.12f, 1f);

            Color disabledText = EditorGUIUtility.isProSkin
                ? new Color(0.65f, 0.65f, 0.65f, 1f)
                : new Color(0.35f, 0.35f, 0.35f, 1f);

            // Make ALL states consistent (important)
            _guidFieldStyle.normal.textColor   = normalText;
            _guidFieldStyle.hover.textColor    = normalText;
            _guidFieldStyle.focused.textColor  = normalText;
            _guidFieldStyle.active.textColor   = normalText;
            _guidFieldStyle.padding = new RectOffset(6, 6, 3, 3);
            _guidFieldStyle.margin = new RectOffset(0, 0, 0, 0);

            var panelCol = EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.035f)
                : new Color(0f, 0f, 0f, 0.04f);

            _guidPanelTex = CreateTexture(2, 2, panelCol);
            _guidPanelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 2)
            };
            _guidPanelStyle.normal.background = _guidPanelTex;
        }

        // --------------------
        // Header + Toolbar
        // --------------------

        private void DrawStylizedHeader()
        {
            var headerRect = EditorGUILayout.GetControlRect(false, 50);
            EditorGUI.DrawRect(headerRect, Colors.HeaderBg);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 3, headerRect.width, 3), Colors.HeaderAccent);

            var labelRect = new Rect(headerRect.x + 15, headerRect.y, headerRect.width, headerRect.height);
            GUI.Label(labelRect, "CROWSAVE SCENE GUID MANAGER", _headerStyle);
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(new GUIContent(" Rescan", Icons.Refresh.image), EditorStyles.toolbarButton, GUILayout.Width(80)))
                    Scan();

                GUILayout.Space(10);

                EditorGUI.BeginChangeCheck();
                _autoRescanOnFocus = GUILayout.Toggle(_autoRescanOnFocus, "Auto-Rescan on Focus", EditorStyles.toolbarButton);
                _autoGenerateRegistryOnScan = GUILayout.Toggle(_autoGenerateRegistryOnScan, "Auto-Registry (Enabled Build Scenes)", EditorStyles.toolbarButton);
                _showFullGuids = GUILayout.Toggle(_showFullGuids, "Show Full GUIDs", EditorStyles.toolbarButton);

                if (EditorGUI.EndChangeCheck())
                    SavePrefs();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Generate Registry", Icons.SaveAs.image), EditorStyles.toolbarButton))
                    GenerateRegistryAssetStatic(silent: false);

                if (GUILayout.Button(new GUIContent("Prune Orphaned", Icons.Trash.image), EditorStyles.toolbarButton, GUILayout.Width(130)))
                    PruneOrphanedRegistryEntries(confirm: true);
            }
        }

        private void DrawRegistrySettingsPanel()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUI.BeginChangeCheck();
                _registryFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_registryFoldout, "Registry Output");
                if (EditorGUI.EndChangeCheck())
                    EditorPrefs.SetBool(PrefRegistryFoldout, _registryFoldout);

                EditorGUILayout.EndFoldoutHeaderGroup();

                if (!_registryFoldout)
                    return;

                GUILayout.Space(4);

                string preferred = ComposeRegistryAssetPath(_registryFolder, _registryName);
                string resolved = ResolveExistingRegistryPath(preferred);

                // Folder row
                using (new EditorGUILayout.HorizontalScope())
                {
                    float h = EditorGUIUtility.singleLineHeight;

                    EditorGUILayout.LabelField("Folder", GUILayout.Width(60), GUILayout.Height(h));

                    EditorGUI.BeginChangeCheck();
                    _registryFolder = EditorGUILayout.TextField(
                        _registryFolder,
                        EditorStyles.miniTextField,
                        GUILayout.Height(h)
                    );
                    if (EditorGUI.EndChangeCheck())
                    {
                        _registryFolder = NormalizeAssetsFolder(_registryFolder);
                        SavePrefs();
                        _pendingRegenerateAfterPrefsChange = true;
                    }

                    if (GUILayout.Button(new GUIContent("Pick…", Icons.Folder.image), EditorStyles.miniButton, GUILayout.Width(80), GUILayout.Height(h)))
                        PickRegistryAssetLocation();

                    if (GUILayout.Button("Reset", EditorStyles.miniButton, GUILayout.Width(60), GUILayout.Height(h)))
                        ResetRegistryOutputToDefaults();
                }

                // Asset name row
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Asset", GUILayout.Width(60));

                    EditorGUI.BeginChangeCheck();
                    _registryName = EditorGUILayout.TextField(_registryName);
                    if (EditorGUI.EndChangeCheck())
                    {
                        _registryName = SanitizeAssetName(_registryName);
                        SavePrefs();
                        _pendingRegenerateAfterPrefsChange = true;
                    }
                }

                // Paths (compact, no huge layout)
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField("Preferred", preferred);
                    EditorGUILayout.TextField("Found", resolved);
                }

                if (!string.Equals(preferred, resolved, StringComparison.OrdinalIgnoreCase))
                {
                    EditorGUILayout.HelpBox(
                        "A registry asset was found at a different path than your preferred output. " +
                        "Generating will attempt to MOVE the existing asset to the preferred path (to preserve references).",
                        MessageType.Info);
                }
            }
        }

        private void ResetRegistryOutputToDefaults()
        {
            _registryFolder = DefaultRegistryFolder;
            _registryName = DefaultRegistryName;

            SavePrefs();
            _pendingRegenerateAfterPrefsChange = true;

            Scan();
            GUIUtility.ExitGUI();
        }

        private void PickRegistryAssetLocation()
        {
            string startFolder = NormalizeAssetsFolder(_registryFolder);
            string startName = SanitizeAssetName(_registryName);

            // IMPORTANT: make sure the folder exists so Unity doesn't fall back to Documents.
            EnsureFolderForAsset($"{startFolder}/{startName}.asset");

            // If still not valid (somehow), fall back to Assets
            if (!AssetDatabase.IsValidFolder(startFolder))
                startFolder = "Assets";

            string chosen = EditorUtility.SaveFilePanelInProject(
                "Choose SceneGuidRegistry location",
                $"{startName}.asset",
                "asset",
                "Pick where the SceneGuidRegistry asset should be generated/stored.",
                startFolder
            );

            if (string.IsNullOrWhiteSpace(chosen))
                return;

            chosen = chosen.Replace("\\", "/");
            if (!chosen.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog("CrowSave", "Registry asset must be inside the Assets/ folder.", "OK");
                return;
            }

            _registryFolder = NormalizeAssetsFolder(Path.GetDirectoryName(chosen)?.Replace("\\", "/") ?? DefaultRegistryFolder);
            _registryName = SanitizeAssetName(Path.GetFileNameWithoutExtension(chosen) ?? DefaultRegistryName);

            SavePrefs();
            _pendingRegenerateAfterPrefsChange = true;

            Scan();
            GUIUtility.ExitGUI();
        }

        // --------------------
        // Bulk + Table
        // --------------------

        private void DrawBulkActions()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Bulk Operations", EditorStyles.miniBoldLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button(new GUIContent(" Select Problems (Enabled Build)", Icons.Search.image), GUILayout.Height(22)))
                        SelectBy(r => r.enabledInBuild && (r.sceneFileMissing || r.NeedsAddOrFix));

                    if (GUILayout.Button(new GUIContent(" Add/Fix Selected", Icons.SaveAs.image), GUILayout.Height(22)))
                        AddOrFixGuidToSelected(singleSceneOnly: false);

                    if (GUILayout.Button("Select Not In Build", GUILayout.Height(22)))
                        SelectBy(r => !r.enabledInBuild && !r.sceneFileMissing && !r.registryOrphaned);

                    if (GUILayout.Button("Add Selected To Build", GUILayout.Height(22)))
                        AddSelectedToBuild(confirm: true);

                    if (GUILayout.Button("Select Orphaned Registry", GUILayout.Height(22)))
                        SelectBy(r => r.registryOrphaned);

                    if (GUILayout.Button("Remove Orphaned Selected", GUILayout.Height(22)))
                        RemoveRegistryForSelectedOrphaned(confirm: true);

                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("Select All", GUILayout.Width(90), GUILayout.Height(22)))
                        _rows.ForEach(r => r.selected = true);

                    if (GUILayout.Button("Deselect", GUILayout.Width(90), GUILayout.Height(22)))
                        _rows.ForEach(r => r.selected = false);
                }
            }
        }

        private void DrawTableHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Space(22);

                GUILayout.Label("Scene", EditorStyles.miniBoldLabel, GUILayout.Width(SceneNameWidth));
                GUILayout.Label("In Build", EditorStyles.miniBoldLabel, GUILayout.Width(InBuildWidth));
                GUILayout.Label("En", EditorStyles.miniBoldLabel, GUILayout.Width(EnabledWidth));
                GUILayout.Label("Idx", EditorStyles.miniBoldLabel, GUILayout.Width(IndexWidth));
                GUILayout.Label("Status", EditorStyles.miniBoldLabel, GUILayout.Width(StatusWidth));

                GUILayout.Label("GUIDs", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
                GUILayout.Label("Ops", EditorStyles.miniBoldLabel, GUILayout.Width(OpsWidth));
            }
        }

        private void DrawRow(Row row, int index)
        {
            var bgStyle = (index % 2 == 0) ? _rowEvenStyle : _rowOddStyle;

            using (new EditorGUILayout.VerticalScope(bgStyle))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    DrawRowCore(row);
                }

                if (!string.IsNullOrEmpty(row.status))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Space(25);
                        EditorGUILayout.LabelField($"↳ {row.status}", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                    }
                }
            }
        }

        private void DrawRowCore(Row row)
        {
            row.selected = EditorGUILayout.Toggle(row.selected, GUILayout.Width(18));
            EditorGUILayout.LabelField(new GUIContent(row.name, row.path), GUILayout.Width(SceneNameWidth));

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(row.presentInBuildSettings, GUILayout.Width(InBuildWidth));
                EditorGUILayout.Toggle(row.enabledInBuild, GUILayout.Width(EnabledWidth));
                EditorGUILayout.IntField(row.buildIndex, GUILayout.Width(IndexWidth));
            }

            DrawStatusChip(row);
            DrawGuidsColumn(row);
            DrawRowButtons(row);
        }

        // --------------------
        // Status Chip
        // --------------------

        private void DrawStatusChip(Row row)
        {
            const float h = 18f;
            Rect r = GUILayoutUtility.GetRect(StatusWidth, h, GUILayout.Width(StatusWidth));

            var (label, accent, icon) = GetRowStatus(row);

            bool pro = EditorGUIUtility.isProSkin;
            Color bg = pro ? new Color(0.18f, 0.18f, 0.18f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
            Color border = pro ? new Color(0f, 0f, 0f, 0.35f) : new Color(0f, 0f, 0f, 0.18f);

            EditorGUI.DrawRect(r, bg);
            DrawRectBorder(r, border);

            float dot = 8f;
            Rect dotR = new Rect(r.x + 6f, r.y + (r.height - dot) * 0.5f, dot, dot);
            EditorGUI.DrawRect(dotR, accent);

            float iconSize = 12f;
            float x = dotR.xMax + 4f;
            if (icon != null)
            {
                Rect iconR = new Rect(x, r.y + (r.height - iconSize) * 0.5f, iconSize, iconSize);
                GUI.DrawTexture(iconR, icon, ScaleMode.ScaleToFit, true);
                x = iconR.xMax + 3f;
            }

            Rect textR = new Rect(x, r.y, r.xMax - x - 6f, r.height);
            GUI.Label(textR, label ?? "", _chipTextStyle);
        }

        private (string label, Color color, Texture icon) GetRowStatus(Row row)
        {
            if (row.registryOrphaned) return ("ORPHAN", Colors.AccentDanger, Icons.Error.image);
            if (row.sceneFileMissing) return ("MISSING", Colors.AccentDanger, Icons.Error.image);

            if (row.enabledInBuild && !row.hasSceneGuid) return ("NEEDS ADD", Colors.AccentDanger, Icons.Warning.image);
            if (row.enabledInBuild && row.HasMismatch) return ("MISMATCH", Colors.AccentWarn, Icons.Warning.image);
            if (row.enabledInBuild) return ("OK", Colors.AccentOk, Icons.Info.image);

            return ("DISABLED", Colors.AccentNeutral, Icons.Info.image);
        }

        private static void DrawRectBorder(Rect r, Color border)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), border);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), border);
            EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), border);
        }

        // --------------------
        // GUID Column
        // --------------------

        private void DrawGuidsColumn(Row row)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var foldIcon = row.expanded ? Icons.FoldoutOpen.image : Icons.FoldoutClosed.image;
                    if (GUILayout.Button(new GUIContent(foldIcon, "Show details"), EditorStyles.label, GUILayout.Width(18), GUILayout.Height(18)))
                        row.expanded = !row.expanded;

                    string a = FormatGuidForDisplay(row.assetGuid);
                    string s = row.hasSceneGuid ? FormatGuidForDisplay(row.foundGuid) : "(no SceneGuid)";
                    string summary = _showFullGuids ? $"Asset {a}   |   Scene {s}" : $"{a}   |   {s}";

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.TextField(summary, _guidFieldStyle, GUILayout.ExpandWidth(true));
                    }

                    if (GUILayout.Button("Copy", GUILayout.Width(52), GUILayout.Height(18)))
                        CopyRowGuids(row);

                    var menuRect = GUILayoutUtility.GetRect(18, 18, GUILayout.Width(18), GUILayout.Height(18));
                    if (GUI.Button(menuRect, "⋯", EditorStyles.miniButton))
                        ShowGuidMenu(menuRect, row);
                }

                if (!row.expanded) return;

                using (new EditorGUILayout.VerticalScope(_guidPanelStyle))
                {
                    DrawGuidDetailLine("Asset", row.assetGuid);
                    DrawGuidDetailLine("Scene", row.hasSceneGuid ? row.foundGuid : null, fallback: "(no SceneGuid component)");

                    if (row.hasRegistryEntry)
                        DrawTextDetailLine("Registry", row.registryOrphaned ? "ORPHANED entry" : "has entry");
                }
            }
        }

        private void DrawGuidDetailLine(string key, string guid, string fallback = "")
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(key, _kvKeyStyle, GUILayout.Width(52));

                string value = string.IsNullOrWhiteSpace(guid) ? fallback : guid;

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(value, _guidFieldStyle, GUILayout.ExpandWidth(true));
                }

                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(guid)))
                {
                    if (GUILayout.Button("Copy", GUILayout.Width(52), GUILayout.Height(18)))
                        EditorGUIUtility.systemCopyBuffer = guid ?? "";
                }
            }
        }

        private void DrawTextDetailLine(string key, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(key, _kvKeyStyle, GUILayout.Width(52));
                EditorGUILayout.LabelField(value ?? "", EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            }
        }

        private string FormatGuidForDisplay(string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return "";
            if (_showFullGuids) return guid;

            const int tail = 12;
            if (guid.Length <= tail) return guid;
            return "…" + guid.Substring(guid.Length - tail, tail);
        }

        private static void CopyRowGuids(Row row)
        {
            string asset = row.assetGuid ?? "";
            string scene = row.hasSceneGuid ? (row.foundGuid ?? "") : "";
            string reg = row.hasRegistryEntry ? "Registry: has entry" : "Registry: (none)";

            EditorGUIUtility.systemCopyBuffer =
                $"Asset: {asset}\nScene: {scene}\n{reg}";
        }

        private static void ShowGuidMenu(Rect r, Row row)
        {
            var menu = new GenericMenu();

            if (!string.IsNullOrWhiteSpace(row.assetGuid))
                menu.AddItem(new GUIContent("Copy/Asset GUID"), false, () => EditorGUIUtility.systemCopyBuffer = row.assetGuid);
            else
                menu.AddDisabledItem(new GUIContent("Copy/Asset GUID"));

            if (row.hasSceneGuid && !string.IsNullOrWhiteSpace(row.foundGuid))
                menu.AddItem(new GUIContent("Copy/Scene GUID"), false, () => EditorGUIUtility.systemCopyBuffer = row.foundGuid);
            else
                menu.AddDisabledItem(new GUIContent("Copy/Scene GUID"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy/All (Asset + Scene + Registry)"), false, () => CopyRowGuids(row));

            menu.ShowAsContext();
        }

        // --------------------
        // Ops
        // --------------------

        private void DrawRowButtons(Row row)
        {
            bool canPing = !string.IsNullOrWhiteSpace(row.path);
            bool canOpen = row.CanOpenScene;

            bool canAddFix = row.enabledInBuild && !row.sceneFileMissing && row.NeedsAddOrFix;
            bool canAddToBuild = !row.sceneFileMissing && !row.registryOrphaned && !row.enabledInBuild && File.Exists(row.path);
            bool canRemoveRegistry = row.registryOrphaned && row.hasRegistryEntry;
            bool canRemoveFromBuild = row.presentInBuildSettings;

            using (new EditorGUILayout.VerticalScope(GUILayout.Width(OpsWidth)))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canPing))
                    {
                        if (GUILayout.Button(new GUIContent("Ping", Icons.Ping.image), EditorStyles.miniButtonLeft, GUILayout.Width(60)))
                        {
                            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(row.path);
                            if (asset != null) EditorGUIUtility.PingObject(asset);
                        }
                    }

                    using (new EditorGUI.DisabledScope(!canOpen))
                    {
                        if (GUILayout.Button(new GUIContent("Open", Icons.Open.image), EditorStyles.miniButtonMid, GUILayout.Width(60)))
                            EditorSceneManager.OpenScene(row.path, OpenSceneMode.Single);
                    }

                    using (new EditorGUI.DisabledScope(!canAddFix))
                    {
                        string label = row.hasSceneGuid ? "Fix" : "Add Component";
                        var icon = row.hasSceneGuid ? Icons.Fix.image : Icons.Plus.image;

                        if (GUILayout.Button(new GUIContent(label, icon), EditorStyles.miniButtonRight, GUILayout.Width(150)))
                        {
                            row.selected = true;
                            AddOrFixGuidToSelected(singleSceneOnly: true);
                        }
                    }

                    GUILayout.FlexibleSpace();

                    using (new EditorGUI.DisabledScope(!canAddToBuild))
                    {
                        if (GUILayout.Button("Add to Build", GUILayout.Width(115)))
                            AddSceneToBuild(row.path, enable: true, confirm: true);
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!canRemoveRegistry))
                    {
                        if (GUILayout.Button(new GUIContent("Remove from Registry", Icons.Trash.image), GUILayout.Height(18)))
                            RemoveRegistryEntryForGuid(row.assetGuid, confirm: true);
                    }

                    using (new EditorGUI.DisabledScope(!canRemoveFromBuild))
                    {
                        if (GUILayout.Button(new GUIContent("Remove from Build", Icons.Trash.image), GUILayout.Height(18)))
                            RemoveSceneFromBuild(row.path, confirm: true);
                    }
                }
            }
        }

        private void DrawFooter()
        {
            GUILayout.Space(10);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Notes", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    "- SceneGuid must store the SCENE ASSET GUID (AssetDatabase GUID), not a random Guid.\n" +
                    "- SceneGuidRegistry maps scene asset GUIDs -> (runtime buildIndex/path) for loading.\n" +
                    "- 'Idx' is the RUNTIME build index: enabled scenes only, packed 0..N-1.\n" +
                    "- Build indices change when you reorder/remove enabled scenes; registry regenerates from Build Settings.",
                    EditorStyles.wordWrappedMiniLabel
                );
            }
            GUILayout.Space(5);
        }

        private static Texture2D CreateTexture(int width, int height, Color color)
        {
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;

            var t = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            t.SetPixels(pixels);
            t.Apply(false, true);
            return t;
        }

        private static GUIContent SafeIconContent(string name)
        {
            try
            {
                var c = EditorGUIUtility.IconContent(name);
                return c ?? new GUIContent();
            }
            catch
            {
                return new GUIContent();
            }
        }

        // --------------------
        // Prefs
        // --------------------

        private void LoadPrefs()
        {
            _autoRescanOnFocus = EditorPrefs.GetBool(PrefAutoRescanOnFocus, true);
            _autoGenerateRegistryOnScan = EditorPrefs.GetBool(PrefAutoGenerateRegistryOnScan, true);
            _showFullGuids = EditorPrefs.GetBool(PrefShowFullGuids, false);

            _registryFolder = EditorPrefs.GetString(PrefRegistryFolder, DefaultRegistryFolder);
            _registryName = EditorPrefs.GetString(PrefRegistryName, DefaultRegistryName);

            _registryFoldout = EditorPrefs.GetBool(PrefRegistryFoldout, true);

            _registryFolder = NormalizeAssetsFolder(_registryFolder);
            _registryName = SanitizeAssetName(_registryName);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(PrefAutoRescanOnFocus, _autoRescanOnFocus);
            EditorPrefs.SetBool(PrefAutoGenerateRegistryOnScan, _autoGenerateRegistryOnScan);
            EditorPrefs.SetBool(PrefShowFullGuids, _showFullGuids);

            _registryFolder = NormalizeAssetsFolder(_registryFolder);
            _registryName = SanitizeAssetName(_registryName);

            EditorPrefs.SetString(PrefRegistryFolder, _registryFolder);
            EditorPrefs.SetString(PrefRegistryName, _registryName);

            EditorPrefs.SetBool(PrefRegistryFoldout, _registryFoldout);
        }

        private static string GetPreferredRegistryAssetPathFromPrefs()
        {
            var folder = NormalizeAssetsFolder(EditorPrefs.GetString(PrefRegistryFolder, DefaultRegistryFolder));
            var name = SanitizeAssetName(EditorPrefs.GetString(PrefRegistryName, DefaultRegistryName));
            return ComposeRegistryAssetPath(folder, name);
        }

        private static string ComposeRegistryAssetPath(string folder, string name)
        {
            folder = NormalizeAssetsFolder(folder);
            name = SanitizeAssetName(name);
            return $"{folder}/{name}.asset";
        }

        private static string NormalizeAssetsFolder(string folder)
        {
            folder = (folder ?? "").Replace("\\", "/").Trim();
            if (string.IsNullOrWhiteSpace(folder))
                return DefaultRegistryFolder;

            // Force within Assets/
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                return DefaultRegistryFolder;

            // Normalize exact "Assets" to "Assets/..."
            if (string.Equals(folder, "Assets", StringComparison.OrdinalIgnoreCase))
                return DefaultRegistryFolder;

            // Trim trailing slashes
            while (folder.EndsWith("/"))
                folder = folder.Substring(0, folder.Length - 1);

            return folder;
        }

        private static string SanitizeAssetName(string name)
        {
            name = (name ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = DefaultRegistryName;

            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            // Avoid ".asset.asset" if user typed extension
            if (name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                name = Path.GetFileNameWithoutExtension(name);

            if (string.IsNullOrWhiteSpace(name))
                name = DefaultRegistryName;

            return name;
        }

        // --------------------
        // Scan
        // --------------------

        private void Scan()
        {
            _rows.Clear();

            var buildMap = BuildSettingsMap(); // runtime indices (enabled-only)
            string preferredRegistryPath = ComposeRegistryAssetPath(_registryFolder, _registryName);

            var reg = LoadRegistryAssetResolved(preferredRegistryPath);
            var regMap = RegistryMap(reg);

            var sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
            var scenePaths = new List<string>(sceneGuids.Length);
            for (int i = 0; i < sceneGuids.Length; i++)
            {
                string p = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (!p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) continue;
                scenePaths.Add(p.Replace("\\", "/"));
            }

            var setup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                foreach (var path in scenePaths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    string assetGuid = AssetDatabase.AssetPathToGUID(path);
                    bool fileMissing = !File.Exists(path);

                    if (!buildMap.TryGetValue(path, out var bsi))
                        bsi = (present: false, enabled: false, buildIndex: -1);

                    var row = new Row
                    {
                        expanded = false,
                        path = path,
                        name = Path.GetFileNameWithoutExtension(path),
                        assetGuid = assetGuid,
                        sceneFileMissing = fileMissing,

                        presentInBuildSettings = bsi.present,
                        enabledInBuild = bsi.enabled,
                        buildIndex = bsi.buildIndex,

                        hasRegistryEntry = !string.IsNullOrWhiteSpace(assetGuid) && regMap.ContainsKey(assetGuid),
                        registryOrphaned = false,
                        status = ""
                    };

                    if (!fileMissing)
                    {
                        try
                        {
                            var scene = EditorSceneManager.OpenScene(row.path, OpenSceneMode.Additive);
                            var sg = FindSceneGuid(scene);
                            if (sg != null)
                            {
                                row.hasSceneGuid = true;
                                row.foundGuid = ReadSceneGuidSerialized(sg);
                            }
                            else
                            {
                                row.hasSceneGuid = false;
                                row.foundGuid = "";
                            }
                        }
                        catch (Exception ex)
                        {
                            row.status = $"Scan error: {ex.GetType().Name}";
                        }
                        finally
                        {
                            EditorSceneManager.RestoreSceneManagerSetup(setup);
                        }
                    }
                    else
                    {
                        row.hasSceneGuid = false;
                        row.foundGuid = "";
                        row.status = "Scene file missing on disk.";
                    }

                    if (row.enabledInBuild && !row.hasSceneGuid && string.IsNullOrEmpty(row.status))
                        row.status = "Enabled in build but missing SceneGuid.";

                    if (row.enabledInBuild && row.HasMismatch)
                        row.status = "Enabled in build but SceneGuid.guid != Asset GUID.";

                    _rows.Add(row);
                }

                // Orphans from registry
                if (reg != null)
                {
                    var projectGuidSet = new HashSet<string>(
                        _rows.Select(r => r.assetGuid).Where(g => !string.IsNullOrWhiteSpace(g)),
                        StringComparer.OrdinalIgnoreCase
                    );

                    foreach (var kv in regMap)
                    {
                        string guid = kv.Key;
                        var entry = kv.Value;

                        if (projectGuidSet.Contains(guid))
                            continue;

                        string entryPath = (entry.scenePath ?? "").Replace("\\", "/");
                        if (!buildMap.TryGetValue(entryPath, out var bsi))
                            bsi = (present: false, enabled: false, buildIndex: -1);

                        _rows.Add(new Row
                        {
                            selected = false,
                            expanded = false,
                            name = !string.IsNullOrWhiteSpace(entry.sceneName) ? entry.sceneName : "(missing scene)",
                            path = !string.IsNullOrWhiteSpace(entryPath) ? entryPath : "(missing path)",
                            assetGuid = guid,

                            presentInBuildSettings = bsi.present,
                            enabledInBuild = bsi.enabled,
                            buildIndex = bsi.buildIndex,

                            sceneFileMissing = true,
                            hasSceneGuid = false,
                            foundGuid = "",
                            hasRegistryEntry = true,
                            registryOrphaned = true,
                            status = "Registry contains this GUID, but the scene asset is not found in the project (deleted/moved?)."
                        });
                    }
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(setup);
            }

            _rows.Sort((a, b) =>
            {
                int pa = a.registryOrphaned ? 0 : (a.enabledInBuild ? 1 : 2);
                int pb = b.registryOrphaned ? 0 : (b.enabledInBuild ? 1 : 2);
                if (pa != pb) return pa.CompareTo(pb);
                return string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
            });

            if (_autoGenerateRegistryOnScan)
                GenerateRegistryAssetStatic(silent: true);
        }

        /// <summary>
        /// Build indices are computed from Build Settings enabled order (enabled-only, packed 0..N-1).
        /// This matches runtime "LoadSceneAsync(buildIndex)" behavior in player builds.
        /// </summary>
        private static Dictionary<string, (bool present, bool enabled, int buildIndex)> BuildSettingsMap()
        {
            var map = new Dictionary<string, (bool present, bool enabled, int buildIndex)>(StringComparer.OrdinalIgnoreCase);
            var scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();

            int runtimeIndex = 0;

            for (int i = 0; i < scenes.Length; i++)
            {
                var bs = scenes[i];
                if (bs == null) continue;

                string path = (bs.path ?? "").Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(path)) continue;

                int idx = -1;
                if (bs.enabled && File.Exists(path))
                    idx = runtimeIndex++;

                map[path] = (present: true, enabled: bs.enabled, buildIndex: idx);
            }

            return map;
        }

        private static Dictionary<string, SceneGuidRegistry.Entry> RegistryMap(SceneGuidRegistry reg)
        {
            var map = new Dictionary<string, SceneGuidRegistry.Entry>(StringComparer.OrdinalIgnoreCase);
            if (reg == null) return map;

            var entries = reg.EditorGetEntries();
            if (entries == null) return map;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (string.IsNullOrWhiteSpace(e.sceneAssetGuid)) continue;
                map[e.sceneAssetGuid] = e;
            }

            return map;
        }

        private static SceneGuidRegistry LoadRegistryAssetResolved(string preferredPath)
        {
            preferredPath = (preferredPath ?? "").Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                var a = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(preferredPath);
                if (a != null) return a;
            }

            // Legacy fallback
            var legacy = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(LegacyRegistryAssetPath);
            if (legacy != null) return legacy;

            // Single-asset fallback
            var guids = AssetDatabase.FindAssets("t:SceneGuidRegistry", new[] { "Assets" });
            if (guids != null && guids.Length == 1)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[0]).Replace("\\", "/");
                return AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(p);
            }

            return null;
        }

        private static string ResolveExistingRegistryPath(string preferredPath)
        {
            preferredPath = (preferredPath ?? "").Replace("\\", "/");
            if (!string.IsNullOrWhiteSpace(preferredPath))
            {
                if (AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(preferredPath) != null)
                    return preferredPath;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(LegacyRegistryAssetPath) != null)
                return LegacyRegistryAssetPath;

            var guids = AssetDatabase.FindAssets("t:SceneGuidRegistry", new[] { "Assets" });
            if (guids != null && guids.Length == 1)
                return AssetDatabase.GUIDToAssetPath(guids[0]).Replace("\\", "/");

            return preferredPath;
        }

        // --------------------
        // Build Settings editing
        // --------------------

        private static void AddSceneToBuild(string scenePath, bool enable, bool confirm)
        {
            if (string.IsNullOrWhiteSpace(scenePath) || !File.Exists(scenePath))
                return;

            if (confirm)
            {
                if (!EditorUtility.DisplayDialog("CrowSave", $"Add/Enable scene in Build Settings?\n\n{scenePath}", "Yes", "Cancel"))
                    return;
            }

            var list = (EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>()).ToList();
            int idx = list.FindIndex(s => s != null && string.Equals((s.path ?? "").Replace("\\", "/"), scenePath, StringComparison.OrdinalIgnoreCase));

            if (idx >= 0)
            {
                var s = list[idx];
                if (s != null) s.enabled = enable ? true : s.enabled;
                list[idx] = s;
            }
            else
            {
                list.Add(new EditorBuildSettingsScene(scenePath, enable));
            }

            EditorBuildSettings.scenes = list.ToArray();
            AssetDatabase.SaveAssets();
        }

        private static void RemoveSceneFromBuild(string scenePath, bool confirm)
        {
            if (string.IsNullOrWhiteSpace(scenePath)) return;

            if (confirm)
            {
                if (!EditorUtility.DisplayDialog("CrowSave", $"Remove scene from Build Settings?\n\n{scenePath}", "Remove", "Cancel"))
                    return;
            }

            var list = (EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>()).ToList();
            int before = list.Count;

            list.RemoveAll(s => s != null && string.Equals((s.path ?? "").Replace("\\", "/"), scenePath.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase));

            if (list.Count != before)
                EditorBuildSettings.scenes = list.ToArray();

            AssetDatabase.SaveAssets();
        }

        private void AddSelectedToBuild(bool confirm)
        {
            var targets = _rows.Where(r => r.selected && !r.sceneFileMissing && !r.registryOrphaned && File.Exists(r.path) && !r.enabledInBuild).ToList();
            if (targets.Count == 0) return;

            if (confirm)
            {
                if (!EditorUtility.DisplayDialog("CrowSave", $"Add/Enable {targets.Count} scenes in Build Settings?", "Yes", "Cancel"))
                    return;
            }

            foreach (var r in targets)
                AddSceneToBuild(r.path, enable: true, confirm: false);

            Scan();
        }

        // --------------------
        // Selection
        // --------------------

        private void SelectBy(Func<Row, bool> predicate)
        {
            for (int i = 0; i < _rows.Count; i++)
                _rows[i].selected = predicate(_rows[i]);
        }

        // --------------------
        // Add/Fix SceneGuid
        // --------------------

        private void AddOrFixGuidToSelected(bool singleSceneOnly)
        {
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            int attempted = 0;
            int changed = 0;

            try
            {
                foreach (var row in _rows.Where(r => r.selected).ToList())
                {
                    if (!row.enabledInBuild) continue;
                    if (row.sceneFileMissing) continue;
                    if (!row.NeedsAddOrFix) continue;
                    if (string.IsNullOrWhiteSpace(row.path) || !File.Exists(row.path)) continue;

                    attempted++;

                    var scene = EditorSceneManager.OpenScene(row.path, OpenSceneMode.Single);
                    bool did = EnsureSceneGuidInOpenScene(scene);

                    if (did)
                    {
                        changed++;
                        EditorSceneManager.MarkSceneDirty(scene);
                        EditorSceneManager.SaveScene(scene);
                    }

                    if (singleSceneOnly) break;
                }

                if (attempted > 0)
                    Debug.Log($"[CrowSave] Add/Fix completed. Changed: {changed} / Attempted: {attempted}");
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                AssetDatabase.Refresh();
                Scan();
            }
        }

        private static SceneGuid FindSceneGuid(Scene scene)
        {
            if (!scene.IsValid()) return null;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var sg = roots[i].GetComponentInChildren<SceneGuid>(true);
                if (sg != null) return sg;
            }

            return null;
        }

        private static bool EnsureSceneGuidInOpenScene(Scene scene)
        {
            var sg = FindSceneGuid(scene);
            if (sg == null)
            {
                var go = new GameObject(SceneGuidRootName);

                if (!Application.isBatchMode)
                    Undo.RegisterCreatedObjectUndo(go, "Add SceneGuid Root");

                SceneManager.MoveGameObjectToScene(go, scene);

                sg = go.AddComponent<SceneGuid>();
                if (!Application.isBatchMode)
                    Undo.RegisterCreatedObjectUndo(sg, "Add SceneGuid Component");
            }

            sg.EnsureGuid();
            return true;
        }

        private static string ReadSceneGuidSerialized(SceneGuid sg)
        {
            try
            {
                var so = new SerializedObject(sg);
                return so.FindProperty("guid")?.stringValue ?? "";
            }
            catch
            {
                return "";
            }
        }

        // --------------------
        // Registry generation + removal
        // --------------------

        /// <summary>
        /// Registry buildIndex is rebuilt from current Build Settings enabled order (enabled-only, packed 0..N-1).
        /// Registry asset path/name comes from EditorPrefs (user configurable).
        /// If a registry exists at the legacy path (or only one exists in project), we attempt to MOVE it to preserve references.
        /// </summary>
        private static void GenerateRegistryAssetStatic(bool silent)
        {
            string preferredPath = GetPreferredRegistryAssetPathFromPrefs();
            preferredPath = (preferredPath ?? "").Replace("\\", "/");

            var scenes = EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>();
            var entries = new List<SceneGuidRegistry.Entry>(scenes.Length);

            int enabledIndex = 0;

            for (int i = 0; i < scenes.Length; i++)
            {
                var s = scenes[i];
                if (s == null) continue;
                if (!s.enabled) continue;

                string path = (s.path ?? "").Replace("\\", "/");
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (!File.Exists(path)) continue;

                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrWhiteSpace(guid))
                {
                    if (!silent) Debug.LogWarning($"[CrowSave] Could not get AssetDatabase GUID for scene path '{path}'.");
                    continue;
                }

                entries.Add(new SceneGuidRegistry.Entry
                {
                    sceneAssetGuid = guid,
                    buildIndex = enabledIndex,
                    scenePath = path,
                    sceneName = Path.GetFileNameWithoutExtension(path)
                });

                enabledIndex++;
            }

            if (entries.Count == 0)
            {
                if (!silent) Debug.Log("[CrowSave] No enabled build scenes (or all missing). Registry not generated.");
                return;
            }

            // Ensure folder exists for preferred path
            EnsureFolderForAsset(preferredPath);

            // Get or move existing asset (to preserve references when user changes path/name)
            var reg = GetOrCreateOrMoveRegistryAsset(preferredPath, silent);

            reg.EditorSet(entries);
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!silent)
                Debug.Log($"[CrowSave] Registry updated with {entries.Count} entries at '{AssetDatabase.GetAssetPath(reg)}'.");
        }

        private static SceneGuidRegistry GetOrCreateOrMoveRegistryAsset(string preferredPath, bool silent)
        {
            // 1) Already exists at preferred path
            var reg = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(preferredPath);
            if (reg != null) return reg;

            // 2) If there is an asset at legacy path, try moving it (preserves GUID + references)
            var legacy = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(LegacyRegistryAssetPath);
            if (legacy != null)
            {
                EnsureFolderForAsset(preferredPath);
                string err = AssetDatabase.MoveAsset(LegacyRegistryAssetPath, preferredPath);
                if (string.IsNullOrEmpty(err))
                {
                    var moved = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(preferredPath);
                    if (moved != null) return moved;
                }
                else if (!silent)
                {
                    Debug.LogWarning($"[CrowSave] Could not move registry from legacy path to preferred path:\n{err}\nCreating a new registry at preferred path instead.");
                }
            }

            // 3) If exactly one registry exists in project, try moving it
            var found = AssetDatabase.FindAssets("t:SceneGuidRegistry", new[] { "Assets" });
            if (found != null && found.Length == 1)
            {
                string onlyPath = AssetDatabase.GUIDToAssetPath(found[0]).Replace("\\", "/");
                var only = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(onlyPath);
                if (only != null)
                {
                    EnsureFolderForAsset(preferredPath);
                    string err = AssetDatabase.MoveAsset(onlyPath, preferredPath);
                    if (string.IsNullOrEmpty(err))
                    {
                        var moved = AssetDatabase.LoadAssetAtPath<SceneGuidRegistry>(preferredPath);
                        if (moved != null) return moved;
                    }
                    else if (!silent)
                    {
                        Debug.LogWarning($"[CrowSave] Could not move existing registry to preferred path:\n{err}\nCreating a new registry at preferred path instead.");
                    }
                }
            }

            // 4) Create new
            var created = ScriptableObject.CreateInstance<SceneGuidRegistry>();
            AssetDatabase.CreateAsset(created, preferredPath);
            return created;
        }

        private void PruneOrphanedRegistryEntries(bool confirm)
        {
            string preferredPath = ComposeRegistryAssetPath(_registryFolder, _registryName);
            var reg = LoadRegistryAssetResolved(preferredPath);

            if (reg == null)
            {
                EditorUtility.DisplayDialog("CrowSave", "No SceneGuidRegistry asset found (at preferred path, legacy path, or as a single project asset).", "OK");
                return;
            }

            var entries = reg.EditorGetEntries();
            if (entries == null || entries.Count == 0)
            {
                EditorUtility.DisplayDialog("CrowSave", "Registry is empty.", "OK");
                return;
            }

            var projectSceneGuids = new HashSet<string>(
                AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                    .Select(g => AssetDatabase.GUIDToAssetPath(g))
                    .Where(p => !string.IsNullOrWhiteSpace(p) && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                    .Select(p => AssetDatabase.AssetPathToGUID(p))
                    .Where(g => !string.IsNullOrWhiteSpace(g)),
                StringComparer.OrdinalIgnoreCase
            );

            int orphanCount = entries.Count(e => !string.IsNullOrWhiteSpace(e.sceneAssetGuid) && !projectSceneGuids.Contains(e.sceneAssetGuid));
            if (orphanCount == 0)
            {
                EditorUtility.DisplayDialog("CrowSave", "No orphaned registry entries detected.", "OK");
                return;
            }

            if (confirm)
            {
                if (!EditorUtility.DisplayDialog(
                        "CrowSave",
                        $"Remove {orphanCount} orphaned entries from registry?\n\n(Orphaned = GUID not found among project scene assets.)",
                        "Remove",
                        "Cancel"))
                {
                    return;
                }
            }

            entries.RemoveAll(e => !string.IsNullOrWhiteSpace(e.sceneAssetGuid) && !projectSceneGuids.Contains(e.sceneAssetGuid));

            reg.EditorSet(entries);
            EditorUtility.SetDirty(reg);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Scan();
        }

        private void RemoveRegistryForSelectedOrphaned(bool confirm)
        {
            var orphans = _rows.Where(r => r.selected && r.registryOrphaned && r.hasRegistryEntry).ToList();
            if (orphans.Count == 0) return;

            if (confirm)
            {
                if (!EditorUtility.DisplayDialog("CrowSave", $"Remove {orphans.Count} selected orphaned entries from registry?", "Remove", "Cancel"))
                    return;
            }

            string preferredPath = ComposeRegistryAssetPath(_registryFolder, _registryName);
            var reg = LoadRegistryAssetResolved(preferredPath);
            if (reg == null) return;

            var entries = reg.EditorGetEntries();
            if (entries == null) return;

            var orphanGuids = new HashSet<string>(
                orphans.Select(o => o.assetGuid).Where(g => !string.IsNullOrWhiteSpace(g)),
                StringComparer.OrdinalIgnoreCase
            );

            int removed = entries.RemoveAll(e => !string.IsNullOrWhiteSpace(e.sceneAssetGuid) && orphanGuids.Contains(e.sceneAssetGuid));
            if (removed > 0)
            {
                reg.EditorSet(entries);
                EditorUtility.SetDirty(reg);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Scan();
        }

        private void RemoveRegistryEntryForGuid(string assetGuid, bool confirm)
        {
            if (string.IsNullOrWhiteSpace(assetGuid)) return;

            if (confirm)
            {
                if (!EditorUtility.DisplayDialog("CrowSave", $"Remove registry entry for GUID?\n\n{assetGuid}", "Remove", "Cancel"))
                    return;
            }

            string preferredPath = ComposeRegistryAssetPath(_registryFolder, _registryName);
            var reg = LoadRegistryAssetResolved(preferredPath);
            if (reg == null) return;

            var entries = reg.EditorGetEntries();
            if (entries == null) return;

            int removed = entries.RemoveAll(e => string.Equals(e.sceneAssetGuid, assetGuid, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                reg.EditorSet(entries);
                EditorUtility.SetDirty(reg);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Scan();
        }

        private static void EnsureFolderForAsset(string assetPath)
        {
            assetPath = (assetPath ?? "").Replace("\\", "/");
            var folder = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrWhiteSpace(folder)) return;
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parts = folder.Split('/');
            if (parts.Length == 0) return;

            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{cur}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // --------------------
        // Headless build hook
        // --------------------

        internal void ScanHeadlessAndGenerateRegistry()
        {
            FixEnabledBuildScenesHeadless();
            GenerateRegistryAssetStatic(silent: true);
        }

        private static void FixEnabledBuildScenesHeadless()
        {
            var enabledBuildScenes = (EditorBuildSettings.scenes ?? Array.Empty<EditorBuildSettingsScene>())
                .Where(s => s != null && s.enabled)
                .Select(s => (s.path ?? "").Replace("\\", "/"))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Where(File.Exists)
                .ToList();

            if (enabledBuildScenes.Count == 0) return;

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();

            try
            {
                foreach (var path in enabledBuildScenes)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    EnsureSceneGuidInOpenScene(scene);

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        // --------------------
        // Auto-update registry when Build Settings changes (throttled)
        // --------------------
        [InitializeOnLoad]
        private static class BuildSettingsWatcher
        {
            private static bool _pending;

            static BuildSettingsWatcher()
            {
                EditorBuildSettings.sceneListChanged += Schedule;
            }

            private static void Schedule()
            {
                if (_pending) return;
                _pending = true;

                EditorApplication.delayCall += () =>
                {
                    _pending = false;

                    if (!EditorPrefs.GetBool(PrefAutoGenerateRegistryOnScan, true))
                        return;

                    if (EditorApplication.isPlayingOrWillChangePlaymode)
                        return;

                    GenerateRegistryAssetStatic(silent: true);
                };
            }
        }
    }

    public sealed class CrowSaveSceneGuidPrebuild : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            var w = ScriptableObject.CreateInstance<CrowSaveSceneGuidManagerWindow>();
            try { w.ScanHeadlessAndGenerateRegistry(); }
            finally { ScriptableObject.DestroyImmediate(w); }
        }
    }
}
#endif
