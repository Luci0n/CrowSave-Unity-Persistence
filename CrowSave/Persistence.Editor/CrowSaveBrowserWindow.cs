#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CrowSave.Persistence.Save;
using UnityEditor;
using UnityEngine;

public sealed class CrowSaveSaveBrowserWindow : EditorWindow
{
    private const string DefaultFolderName = "saves";

    private Vector2 _scroll;
    private string _folderName = DefaultFolderName;

    // Per-file clone targets so editing one card doesn't affect every card.
    private readonly Dictionary<string, int> _cloneTargets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static GUIStyle _cardStyle;
    private static GUIStyle _slotHeaderStyle;
    private static GUIStyle _mutedStyle;

    private static readonly Color AccentColor = new Color(0.2f, 0.6f, 1f);

    [MenuItem("Tools/CrowSave/Save Browser")]
    public static void Open()
    {
        var w = GetWindow<CrowSaveSaveBrowserWindow>("CrowSave Browser");
        w.minSize = new Vector2(460, 320);
        w.Show();
    }

    private string RootDir => Path.Combine(Application.persistentDataPath, _folderName);

    private void OnEnable() => InitStyles();

    private void InitStyles()
    {
        if (_cardStyle == null)
        {
            _cardStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(10, 10, 6, 6)
            };
        }

        if (_slotHeaderStyle == null)
        {
            _slotHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
        }

        if (_mutedStyle == null)
        {
            _mutedStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(1f, 1f, 1f, 0.65f) }
            };
        }
    }

    private void OnGUI()
    {
        InitStyles();

        DrawTopBanner();
        DrawToolbar();

        if (!Directory.Exists(RootDir))
        {
            EditorGUILayout.HelpBox($"Path not found:\n{RootDir}", MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Create Directory", GUILayout.Height(24)))
                {
                    Directory.CreateDirectory(RootDir);
                    AssetDatabase.Refresh();
                    Repaint();
                }

                if (GUILayout.Button("Open Folder", GUILayout.Height(24)))
                {
                    EditorUtility.RevealInFinder(RootDir);
                }
            }
            return;
        }

        var files = Directory
            .GetFiles(RootDir, "slot_*.sav")
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        GUILayout.Space(6);

        if (files.Length == 0)
        {
            GUILayout.Space(20);
            EditorGUILayout.LabelField("No save slots found.", EditorStyles.centeredGreyMiniLabel);
        }

        foreach (var file in files)
            DrawSaveCard(file);

        GUILayout.Space(10);
        EditorGUILayout.EndScrollView();

        DrawImportFooter();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Space(6);
            GUILayout.Label("Folder:", EditorStyles.miniLabel);

            _folderName = EditorGUILayout.TextField(
                _folderName,
                EditorStyles.toolbarTextField,
                GUILayout.Width(140));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(Icon("Folder Icon", "d_FolderOpened Icon"), EditorStyles.toolbarButton))
                EditorUtility.RevealInFinder(RootDir);

            if (GUILayout.Button(Icon("Refresh", "d_Refresh"), EditorStyles.toolbarButton))
                Repaint();
        }
    }

    private void DrawTopBanner()
    {
        var headerRect = EditorGUILayout.GetControlRect(false, 46);
        EditorGUI.DrawRect(headerRect, new Color(0.12f, 0.12f, 0.12f, 1f));
        EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.yMax - 2, headerRect.width, 2), AccentColor);

        var titleRect = new Rect(headerRect.x + 14, headerRect.y + 7, headerRect.width - 28, 20);
        var subRect = new Rect(headerRect.x + 14, headerRect.y + 25, headerRect.width - 28, 18);

        EditorGUI.LabelField(titleRect, "CROWSAVE PERSISTENCE", _slotHeaderStyle);
        EditorGUI.LabelField(subRect, "Disk Storage & Slot Management", _mutedStyle);
    }

    private void DrawSaveCard(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var info = new FileInfo(filePath);

        EnsureCloneTargetDefault(filePath, fileName);

        using (new EditorGUILayout.VerticalScope(_cardStyle))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(Icon("SaveAs", "d_SaveAs"), GUILayout.Width(20), GUILayout.Height(20));
                EditorGUILayout.LabelField(fileName.ToUpperInvariant(), _slotHeaderStyle);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(Icon("d_SaveAs", "SaveAs"), GUILayout.Width(28), GUILayout.Height(20)))
                    ExportSave(filePath, fileName);

                var prev = GUI.color;
                try
                {
                    GUI.color = new Color(1f, 0.45f, 0.45f);
                    if (GUILayout.Button(Icon("TreeEditor.Trash", "d_TreeEditor.Trash"), GUILayout.Width(28), GUILayout.Height(20)))
                        DeleteSave(filePath, fileName);
                }
                finally
                {
                    GUI.color = prev;
                }
            }

            GUILayout.Space(4);
            DrawSeparator();

            DrawSaveMetadata(filePath, info);

            GUILayout.Space(8);

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Clone to Slot:", EditorStyles.miniLabel);

                int target = _cloneTargets[filePath];
                target = EditorGUILayout.IntField(target, GUILayout.Width(44));
                _cloneTargets[filePath] = Mathf.Clamp(target, 0, 999);

                if (GUILayout.Button("CLONE", EditorStyles.toolbarButton))
                {
                    var dst = Path.Combine(RootDir, $"slot_{_cloneTargets[filePath]:D2}.sav");
                    File.Copy(filePath, dst, true);
                    AssetDatabase.Refresh();
                    Repaint();
                }

                GUILayout.FlexibleSpace();
            }
        }
    }

    private void DrawSaveMetadata(string filePath, FileInfo info)
    {
        try
        {
            var bytes = File.ReadAllBytes(filePath);

            var header = SaveHeaderReader.TryReadHeader(bytes);
            if (!header.IsValid)
            {
                EditorGUILayout.HelpBox("Failed to parse save header.", MessageType.Info);
                return;
            }

            // Compatible with BOTH layouts:
            // - older: ActiveScene
            // - newer: ActiveSceneId + ActiveSceneLoad
            string sceneLoad = TryGetHeaderString(header, "ActiveSceneLoad");
            string sceneId   = TryGetHeaderString(header, "ActiveSceneId");
            string sceneOld  = TryGetHeaderString(header, "ActiveScene");

            // Decide what to show as the primary "Scene" line
            string scenePrimary = !string.IsNullOrWhiteSpace(sceneLoad)
                ? sceneLoad
                : (!string.IsNullOrWhiteSpace(sceneOld) ? sceneOld : "(none)");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    DrawStat("Scene", scenePrimary, "SceneAsset Icon");

                    // If we have a separate id, show it too (nice when debugging scope keys)
                    if (!string.IsNullOrWhiteSpace(sceneId))
                        DrawStat("Scene Id", sceneId, "d_FilterByLabel");

                    DrawStat("Version", header.Version.ToString(), "d_FilterByLabel");
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    DrawStat("Size", $"{header.TotalBytes / 1024f:0.0} KB", "TextAsset Icon");
                    DrawStat("Modified", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm"), "d_Valid");
                }
            }

            GUILayout.Space(6);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Count Objects (slow)", GUILayout.Width(160)))
                {
                    try
                    {
                        var pkg = SaveSerializer.Deserialize(bytes);
                        int entityCount = pkg.Scopes.Sum(s => s.Entities.Count);
                        EditorUtility.DisplayDialog("CrowSave", $"Objects (entities) in save: {entityCount}", "OK");
                    }
                    catch
                    {
                        EditorUtility.DisplayDialog("CrowSave", "Failed to deserialize full save package.", "OK");
                    }
                }
            }
        }
        catch
        {
            EditorGUILayout.HelpBox("Failed to read save file.", MessageType.Info);
        }
    }

    private void DrawStat(string label, string value, string iconName)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(Icon(iconName, "d_DefaultAsset Icon"), GUILayout.Width(16), GUILayout.Height(16));
            EditorGUILayout.LabelField(label + ":", GUILayout.Width(70));
            EditorGUILayout.LabelField(string.IsNullOrWhiteSpace(value) ? "(none)" : value, EditorStyles.boldLabel);
        }
    }

    private void DrawSeparator()
    {
        var r = EditorGUILayout.GetControlRect(false, 1);
        EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.08f));
        GUILayout.Space(6);
    }

    private void DrawImportFooter()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(Icon("Toolbar Plus", "d_Toolbar Plus"), GUILayout.Width(20));
            GUILayout.Label("Import external:", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Browse .sav file", GUILayout.Width(160), GUILayout.Height(22)))
            {
                var src = EditorUtility.OpenFilePanel("Import Save", "", "sav");
                if (!string.IsNullOrEmpty(src))
                {
                    var dst = Path.Combine(RootDir, "slot_99.sav");
                    File.Copy(src, dst, true);
                    AssetDatabase.Refresh();
                    Repaint();
                }
            }
        }
    }

    private void ExportSave(string filePath, string fileName)
    {
        var dst = EditorUtility.SaveFilePanel("Export", "", fileName, "sav");
        if (!string.IsNullOrEmpty(dst))
            File.Copy(filePath, dst, true);
    }

    private void DeleteSave(string filePath, string fileName)
    {
        if (!EditorUtility.DisplayDialog("Delete Save", $"Permanently delete {fileName}?", "Delete", "Cancel"))
            return;

        try
        {
            File.Delete(filePath);
            AssetDatabase.Refresh();
            GUIUtility.ExitGUI();
        }
        catch (Exception ex)
        {
            EditorUtility.DisplayDialog("CrowSave", $"Failed to delete:\n{ex.Message}", "OK");
        }
    }

    private void EnsureCloneTargetDefault(string filePath, string fileName)
    {
        if (_cloneTargets.ContainsKey(filePath))
            return;

        int parsed = 0;
        try
        {
            var digits = new string(fileName.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digits))
                int.TryParse(digits, out parsed);
        }
        catch { /* ignore */ }

        _cloneTargets[filePath] = Mathf.Clamp(parsed, 0, 999);
    }

    /// <summary>
    /// Reads a field or property by name from SaveHeaderReader.Header without hard-coding the header layout.
    /// This keeps the editor tool compatible across header migrations (ActiveScene -> ActiveSceneId/Load, etc.).
    /// </summary>
    private static string TryGetHeaderString(object headerStruct, string memberName)
    {
        var v = GetHeaderFieldOrProperty(headerStruct, memberName);
        return v as string ?? "";
    }

    private static object GetHeaderFieldOrProperty(object headerStruct, string memberName)
    {
        if (headerStruct == null) return null;

        var t = headerStruct.GetType();

        // Field
        var f = t.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (f != null)
            return f.GetValue(headerStruct);

        // Property
        var p = t.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (p != null && p.CanRead)
            return p.GetValue(headerStruct);

        return null;
    }

    /// <summary>Icon helper that never throws / never logs "Unable to load icon".</summary>
    private static GUIContent Icon(string primary, string fallback)
    {
        var a = EditorGUIUtility.IconContent(primary);
        if (a != null && a.image != null) return a;

        var b = EditorGUIUtility.IconContent(fallback);
        if (b != null && b.image != null) return b;

        return GUIContent.none;
    }
}
#endif
