#if UNITY_EDITOR
using System;
using System.Linq;
using CrowSave.Flags.IO;
using CrowSave.Flags.IO.Inputs;
using CrowSave.Flags.IO.Outputs;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace CrowSave.Flags.Editor
{
    [CustomEditor(typeof(FlagsIOCause))]
    public sealed class FlagsIOCauseEditor : UnityEditor.Editor
    {
        private SerializedProperty _scopeMode, _customScopeKey, _flagsState, _bindMaxFrames;
        private SerializedProperty _routes, _debugLogs, _causeId;

        private ReorderableList _routesList;

        private static Type[] _inputTypes, _outputTypes;

        private static class Styles
        {
            public static bool Ready;

            public static GUIStyle MonoHeader;
            public static GUIStyle MonoSub;
            public static GUIStyle Card;
            public static GUIStyle RouteBar;
            public static GUIStyle MiniButton;
            public static GUIStyle GhostLabel;

            public static Color AccentInput = new Color(0.25f, 0.55f, 0.95f, 1f);
            public static Color AccentOutput = new Color(0.20f, 0.85f, 0.55f, 1f);

            public static void Ensure()
            {
                if (Ready) return;

                var mono = EditorStyles.label.font;

                MonoHeader = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    font = mono,
                    normal = { textColor = new Color(0.92f, 0.92f, 0.92f, 1f) }
                };

                MonoSub = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    fontStyle = FontStyle.Bold,
                    font = mono,
                    normal = { textColor = new Color(0.65f, 0.65f, 0.65f, 1f) }
                };

                GhostLabel = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    font = mono,
                    normal = { textColor = new Color(0.55f, 0.55f, 0.55f, 1f) }
                };

                Card = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(10, 10, 8, 10)
                };

                RouteBar = new GUIStyle("HeaderButton")
                {
                    fixedHeight = 22,
                    alignment = TextAnchor.MiddleLeft,
                    fontStyle = FontStyle.Bold
                };

                MiniButton = new GUIStyle(EditorStyles.miniButton)
                {
                    fixedHeight = 18
                };

                Ready = true;
            }
        }

        private void OnEnable()
        {
            _scopeMode = serializedObject.FindProperty("scopeMode");
            _customScopeKey = serializedObject.FindProperty("customScopeKey");
            _flagsState = serializedObject.FindProperty("flagsState");
            _bindMaxFrames = serializedObject.FindProperty("bindMaxFrames");
            _routes = serializedObject.FindProperty("routes");
            _debugLogs = serializedObject.FindProperty("debugLogs");
            _causeId = serializedObject.FindProperty("causeId");

            CacheTypes();

            _routesList = new ReorderableList(serializedObject, _routes, draggable: true, displayHeader: false, displayAddButton: true, displayRemoveButton: true)
            {
                headerHeight = 0,
                footerHeight = 8,
                elementHeightCallback = idx => GetRouteHeight(_routes.GetArrayElementAtIndex(idx)),
                drawElementCallback = (rect, idx, active, focused) => DrawRoute(rect, _routes.GetArrayElementAtIndex(idx), idx),
                onAddCallback = OnAddRoute,
                onRemoveCallback = OnRemoveRoute
            };
        }

        public override void OnInspectorGUI()
        {
            Styles.Ensure();
            serializedObject.Update();

            using (new EditorGUILayout.VerticalScope(Styles.Card))
            {
                EditorGUILayout.LabelField("FLAGS I/O", Styles.MonoHeader);
                EditorGUILayout.LabelField("Scope + Binding + Routes", Styles.GhostLabel);
                EditorGUILayout.Space(6);

                EditorGUILayout.PropertyField(_scopeMode);
                if (_scopeMode.enumValueIndex == (int)FlagsIOCause.ScopeMode.CustomScopeKey)
                    EditorGUILayout.PropertyField(_customScopeKey);

                EditorGUILayout.Space(6);
                EditorGUILayout.PropertyField(_flagsState);
                EditorGUILayout.PropertyField(_bindMaxFrames);

                EditorGUILayout.Space(6);
                EditorGUILayout.PropertyField(_debugLogs);

                // Show CauseId as read-only (use context menu in cause to regenerate)
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(_causeId, new GUIContent("CauseId"));
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Routes", Styles.MonoSub);
            _routesList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnAddRoute(ReorderableList list)
        {
            serializedObject.Update();

            int idx = _routes.arraySize;
            _routes.arraySize++;

            var el = _routes.GetArrayElementAtIndex(idx);

            var nameProp = el.FindPropertyRelative("name");
            if (nameProp != null) nameProp.stringValue = $"Route {idx}";

            var inputProp = el.FindPropertyRelative("input");
            if (inputProp != null) inputProp.managedReferenceValue = null;

            var outputsProp = el.FindPropertyRelative("outputs");
            if (outputsProp != null) outputsProp.ClearArray();

            serializedObject.ApplyModifiedProperties();
        }

        private void OnRemoveRoute(ReorderableList list)
        {
            serializedObject.Update();
            if (list.index >= 0 && list.index < _routes.arraySize)
                DeleteArrayElementSafe(_routes, list.index);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRoute(Rect rect, SerializedProperty routeProp, int index)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float y = rect.y + 2;

            var header = new Rect(rect.x, y, rect.width, 22);
            GUI.Box(header, GUIContent.none, Styles.RouteBar);

            EditorGUI.LabelField(
                new Rect(header.x + 8, header.y, header.width - 40, header.height),
                $"Route #{index}",
                Styles.MonoSub
            );

            if (GUI.Button(new Rect(header.x + header.width - 22, header.y + 2, 18, 18), "×", Styles.MiniButton))
            {
                serializedObject.Update();
                DeleteArrayElementSafe(_routes, index);
                serializedObject.ApplyModifiedProperties();
                return;
            }

            y += 26;

            float x = rect.x + 10;
            float w = rect.width - 14;

            var nameProp = routeProp.FindPropertyRelative("name");
            if (nameProp != null)
            {
                EditorGUI.PropertyField(new Rect(x, y, w, line), nameProp);
                y += line + 8;
            }

            var inputProp = routeProp.FindPropertyRelative("input");
            DrawModuleBlock(ref y, x, w, "Trigger", inputProp, _inputTypes, Styles.AccentInput, allowDelete: false);

            y += 8;

            EditorGUI.LabelField(new Rect(x, y, w, line), "Actions", Styles.MonoSub);
            y += line + 4;

            var outputsProp = routeProp.FindPropertyRelative("outputs");
            if (outputsProp != null)
            {
                for (int i = 0; i < outputsProp.arraySize; i++)
                {
                    int capturedIndex = i;
                    var oProp = outputsProp.GetArrayElementAtIndex(i);

                    DrawModuleBlock(ref y, x, w, $"Action {i}", oProp, _outputTypes, Styles.AccentOutput, allowDelete: true, onDelete: () =>
                    {
                        serializedObject.Update();
                        DeleteArrayElementSafe(outputsProp, capturedIndex);
                        serializedObject.ApplyModifiedProperties();
                    });

                    y += 6;
                }

                var btn = new Rect(x + w * 0.30f, y, w * 0.40f, line + 2);
                if (GUI.Button(btn, "+ Add Action", EditorStyles.miniButton))
                {
                    serializedObject.Update();

                    int newIndex = outputsProp.arraySize;
                    outputsProp.arraySize++;

                    var newProp = outputsProp.GetArrayElementAtIndex(newIndex);
                    newProp.managedReferenceValue = null;

                    serializedObject.ApplyModifiedProperties();
                }
            }
        }

        private void DrawModuleBlock(
            ref float y,
            float x,
            float w,
            string label,
            SerializedProperty prop,
            Type[] options,
            Color accent,
            bool allowDelete,
            Action onDelete = null)
        {
            float line = EditorGUIUtility.singleLineHeight;

            float bodyH = GetPropertyHeightSafe(prop);
            float totalH = line + 6 + (bodyH > 0 ? bodyH + 6 : 10);

            EditorGUI.DrawRect(new Rect(x, y, 3, totalH), accent);

            var header = new Rect(x + 8, y, w - 8, line);

            float labelW = 64;
            EditorGUI.LabelField(new Rect(header.x, header.y, labelW, line), label, Styles.MonoSub);

            float deleteW = allowDelete ? 22 : 0;
            float pickerW = header.width - labelW - deleteW;

            DrawPicker(new Rect(header.x + labelW, header.y, pickerW, line), prop, options);

            if (allowDelete)
            {
                var b = new Rect(header.x + header.width - 20, header.y, 20, line);
                if (GUI.Button(b, "×", Styles.MiniButton))
                {
                    onDelete?.Invoke();
                    return;
                }
            }

            y += line + 6;

            if (prop != null && prop.managedReferenceValue != null)
            {
                var body = new Rect(x + 10, y, w - 14, bodyH);
                int prevIndent = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
                EditorGUI.PropertyField(body, prop, GUIContent.none, includeChildren: true);
                EditorGUI.indentLevel = prevIndent;
                y += bodyH + 6;
            }
            else
            {
                EditorGUI.LabelField(new Rect(x + 10, y, w - 14, line), "None", Styles.GhostLabel);
                y += line + 6;
            }
        }

        private float GetPropertyHeightSafe(SerializedProperty prop)
        {
            if (prop == null) return 0f;
            if (prop.managedReferenceValue == null) return 0f;
            return EditorGUI.GetPropertyHeight(prop, includeChildren: true);
        }

        private float GetRouteHeight(SerializedProperty routeProp)
        {
            float line = EditorGUIUtility.singleLineHeight;
            float h = 0;

            h += 26;

            var nameProp = routeProp.FindPropertyRelative("name");
            if (nameProp != null) h += line + 8;

            var inputProp = routeProp.FindPropertyRelative("input");
            h += (line + 6) + (GetPropertyHeightSafe(inputProp) > 0 ? GetPropertyHeightSafe(inputProp) + 12 : line + 12);

            h += 8 + line + 4;

            var outputsProp = routeProp.FindPropertyRelative("outputs");
            if (outputsProp != null)
            {
                for (int i = 0; i < outputsProp.arraySize; i++)
                {
                    var o = outputsProp.GetArrayElementAtIndex(i);
                    h += (line + 6) + (GetPropertyHeightSafe(o) > 0 ? GetPropertyHeightSafe(o) + 12 : line + 12);
                    h += 6;
                }
                h += line + 10;
            }

            return Mathf.Max(h, 40f);
        }

        private void DrawPicker(Rect rect, SerializedProperty prop, Type[] options)
        {
            string typeName = prop != null && prop.managedReferenceValue != null
                ? prop.managedReferenceValue.GetType().Name
                : "None";

            if (!GUI.Button(rect, typeName, EditorStyles.popup)) return;

            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("None"), typeName == "None", () =>
            {
                serializedObject.Update();
                prop.managedReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
            });

            menu.AddSeparator("");

            foreach (var t in options)
            {
                var captured = t;
                menu.AddItem(new GUIContent(captured.Name), typeName == captured.Name, () =>
                {
                    serializedObject.Update();
                    prop.managedReferenceValue = Activator.CreateInstance(captured);
                    serializedObject.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }

        private static void DeleteArrayElementSafe(SerializedProperty arrayProp, int index)
        {
            if (arrayProp == null) return;
            if (index < 0 || index >= arrayProp.arraySize) return;

            var el = arrayProp.GetArrayElementAtIndex(index);

            if (el != null && el.propertyType == SerializedPropertyType.ManagedReference && el.managedReferenceValue != null)
            {
                arrayProp.DeleteArrayElementAtIndex(index);
                if (index < arrayProp.arraySize)
                    arrayProp.DeleteArrayElementAtIndex(index);
            }
            else
            {
                arrayProp.DeleteArrayElementAtIndex(index);
            }
        }

        private void CacheTypes()
        {
            if (_inputTypes == null) _inputTypes = GetTypes<FlagsInputModule>();
            if (_outputTypes == null) _outputTypes = GetTypes<FlagsOutputModule>();
        }

        private static Type[] GetTypes<T>()
        {
            var baseType = typeof(T);

            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => baseType.IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                .OrderBy(t => t.Name, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
#endif
