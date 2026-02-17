using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CrowSave.Persistence.Runtime
{
    public sealed class PersistenceDebugHUD : MonoBehaviour
    {
        [Header("HUD Settings")]
        public bool visible = true;
        public KeyCode toggleKey = KeyCode.F1;
        [Range(0.5f, 2.0f)] public float uiScale = 1.0f;

        [Header("Styling")]
        public Color accentColor = new Color(0.3f, 0.65f, 1f); 
        public int fontSize = 14;

        private static PersistenceDebugHUD _instance;
        private GUIStyle _panelStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _headerStyle;

        private Vector2 _recentScroll;
        private string _cachedSummary;
        private float _nextUpdate;
        private int _lastEventCount = -1;

        private void Awake()
        {
            if (_instance != null) { Destroy(gameObject); return; }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }

        private void InitStyles()
        {
            if (_panelStyle != null) return;

            Texture2D tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, new Color(0.1f, 0.1f, 0.12f, 0.95f));
            tex.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box) { normal = { background = tex } };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = fontSize, richText = true, wordWrap = true };
            _headerStyle = new GUIStyle(_labelStyle) { fontSize = fontSize + 2, fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            if (!visible) return;
            InitStyles();

            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(Vector3.one * uiScale);

            float margin = 15f;
            float width = 560f;
            
            // Build summary 5x per second
            if (Time.time > _nextUpdate)
            {
                _cachedSummary = BuildSummaryText();
                _nextUpdate = Time.time + 0.2f;
            }

            // 1. TOP PANEL: Auto-calculates height based on text
            float topInnerW = width - 30f;
            float neededTopH = _labelStyle.CalcHeight(new GUIContent(_cachedSummary), topInnerW) + 50f;
            DrawModernPanel(new Rect(margin, margin, width, neededTopH), "SYSTEM STATUS", _cachedSummary);

            // 2. BOTTOM PANEL: Event Log
            float botH = Screen.height / uiScale - neededTopH - (margin * 3);
            DrawEventLog(new Rect(margin, margin + neededTopH + 10f, width, botH));

            GUI.matrix = oldMatrix;
        }

        private void DrawModernPanel(Rect rect, string title, string content)
        {
            GUI.Box(rect, "", _panelStyle);
            GUI.color = accentColor;
            GUI.DrawTexture(new Rect(rect.x, rect.y, 3, rect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(rect.x + 15, rect.y + 10, rect.width - 30, 25), 
                $"<color=#{ColorUtility.ToHtmlStringRGB(accentColor)}>{title}</color>", _headerStyle);
            GUI.Label(new Rect(rect.x + 15, rect.y + 40, rect.width - 30, rect.height - 50), content, _labelStyle);
        }

        private void DrawEventLog(Rect rect)
        {
            GUI.Box(rect, "", _panelStyle);
            GUI.Label(new Rect(rect.x + 15, rect.y + 10, rect.width - 30, 25), 
                $"<color=#{ColorUtility.ToHtmlStringRGB(accentColor)}>RECENT ACTIVITY</color>", _headerStyle);

            float viewW = rect.width - 30;
            float viewH = rect.height - 50;
            Rect viewArea = new Rect(rect.x + 15, rect.y + 45, viewW, viewH);

            // Calculate total content height (accounting for word-wrap)
            float totalContentH = 0;
            var events = PersistenceLog.Events;
            foreach (var e in events) totalContentH += _labelStyle.CalcHeight(new GUIContent(e), viewW - 20) + 4;

            Rect contentArea = new Rect(0, 0, viewW - 20, totalContentH);

            if (events.Count != _lastEventCount)
            {
                _recentScroll.y = totalContentH; // Auto-snap to bottom
                _lastEventCount = events.Count;
            }

            _recentScroll = GUI.BeginScrollView(viewArea, _recentScroll, contentArea);
            
            float currentY = 0;
            int i = 0;
            foreach (var logEntry in events)
            {
                float entryH = _labelStyle.CalcHeight(new GUIContent(logEntry), contentArea.width);
                Rect lineRect = new Rect(0, currentY, contentArea.width, entryH + 4);

                if (i % 2 == 0)
                {
                    GUI.color = new Color(1, 1, 1, 0.04f);
                    GUI.DrawTexture(lineRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }

                GUI.Label(lineRect, logEntry, _labelStyle);
                currentY += entryH + 4;
                i++;
            }

            GUI.EndScrollView();
        }

        private string BuildSummaryText()
        {
            var sb = new StringBuilder();
            var scene = SceneManager.GetActiveScene().name;
            bool ready = PersistenceServices.IsReady;

            sb.AppendLine($"Scene: <b>{scene}</b>");
            sb.AppendLine($"Bootstrap: {(ready ? "<color=#4CAF50>READY</color>" : "<color=#FFA726>INITIALIZING...</color>")}");

            if (ready)
            {
                var reg = PersistenceServices.Get<PersistenceRegistry>();
                var world = PersistenceServices.Get<WorldStateService>();
                world.State.TryGet(scene, out var scopeState);

                sb.AppendLine($"Live Entities: <b>{reg.GetAllInScope(scene).Count}</b>");
                sb.AppendLine($"Snapshots: <b>{scopeState?.EntityBlobs.Count ?? 0}</b> | Rev: <b>{scopeState?.Revision ?? 0}</b>");
                sb.AppendLine($"Last Activity: <i>{PersistenceLog.LastEvent}</i>");
                sb.Append($"\n<color=#888888><b>Controls:</b>  [U] Toggle Tester  [T] Transition  [F5] Save  [F9] Load  [{toggleKey}] HUD</color>");
            }
            return sb.ToString();
        }
    }
}