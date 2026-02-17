#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using CrowSave.Flags.Runtime;
using UnityEditor;
using UnityEngine;

namespace CrowSave.Flags.Editor
{
    public sealed class FlagsWindow : EditorWindow
    {
        [MenuItem("Tools/CrowSave/Flags")]
        private static void Open()
        {
            GetWindow<FlagsWindow>("CrowSave Flags");
        }

        [SerializeField, Min(0.1f)]
        private float refreshSeconds = 0.5f;

        private double _nextRefreshTime;
        private Vector2 _scroll;

        private string _filter = "";
        private bool _sort = true;

        private readonly List<(string scope, string target, string channel, Core.FlagsValue value, int revision)> _rows
            = new List<(string, string, string, Core.FlagsValue, int)>(512);

        private void OnEnable()
        {
            _nextRefreshTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        private void Tick()
        {
            if (EditorApplication.timeSinceStartup < _nextRefreshTime) return;
            _nextRefreshTime = EditorApplication.timeSinceStartup + refreshSeconds;

            // Repaint triggers OnGUI -> RefreshRows
            Repaint();
        }

        private void OnGUI()
        {
            DrawToolbar();

            RefreshRows();

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField($"Rows: {_rows.Count}", EditorStyles.miniLabel);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawTable();

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Refresh (s)", GUILayout.Width(75));
                refreshSeconds = EditorGUILayout.FloatField(refreshSeconds, GUILayout.Width(60));
                refreshSeconds = Mathf.Max(0.1f, refreshSeconds);

                GUILayout.Space(10);

                GUILayout.Label("Filter", GUILayout.Width(40));
                _filter = GUILayout.TextField(_filter ?? "", EditorStyles.toolbarTextField, GUILayout.ExpandWidth(true));

                _sort = GUILayout.Toggle(_sort, "Sort", EditorStyles.toolbarButton, GUILayout.Width(45));

                if (GUILayout.Button("Refresh Now", EditorStyles.toolbarButton, GUILayout.Width(90)))
                    _nextRefreshTime = 0;
            }
        }

        private void RefreshRows()
        {
            _rows.Clear();

            var state = FlagsStatePersistence.Instance != null
                ? FlagsStatePersistence.Instance
                : FindAnyObjectByType<FlagsStatePersistence>(FindObjectsInactive.Include);

            if (state == null || state.Service == null)
                return;

            state.Service.GetSnapshot(_rows);

            string f = (_filter ?? "").Trim();
            if (f.Length > 0)
            {
                for (int i = _rows.Count - 1; i >= 0; i--)
                {
                    var r = _rows[i];
                    if (!Contains(r.scope, f) && !Contains(r.target, f) && !Contains(r.channel, f) && !Contains(r.value.ToString(), f))
                        _rows.RemoveAt(i);
                }
            }

            if (_sort)
            {
                _rows.Sort((a, b) =>
                {
                    int c = string.CompareOrdinal(a.scope, b.scope);
                    if (c != 0) return c;
                    c = string.CompareOrdinal(a.target, b.target);
                    if (c != 0) return c;
                    return string.CompareOrdinal(a.channel, b.channel);
                });
            }
        }

        private void DrawTable()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Header("Scope", 220);
                Header("Target", 260);
                Header("Channel", 180);
                Header("Value", 220);
                Header("Rev", 50);
            }

            EditorGUILayout.Space(2);

            for (int i = 0; i < _rows.Count; i++)
            {
                var r = _rows[i];

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    Cell(r.scope, 220);
                    Cell(r.target, 260);
                    Cell(r.channel, 180);
                    Cell(r.value.ToString(), 220);
                    Cell(r.revision.ToString(), 50);
                }
            }
        }

        private static void Header(string t, float w)
        {
            GUILayout.Label(t, EditorStyles.miniBoldLabel, GUILayout.Width(w));
        }

        private static void Cell(string t, float w)
        {
            GUILayout.Label(t ?? "", EditorStyles.miniLabel, GUILayout.Width(w));
        }

        private static bool Contains(string hay, string needle)
            => (hay ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
#endif
