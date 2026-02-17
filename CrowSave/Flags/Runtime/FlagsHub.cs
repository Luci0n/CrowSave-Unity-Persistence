using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using CrowSave.Flags.IO;

namespace CrowSave.Flags.Runtime
{
    /// <summary>
    /// Projector coordinator: listens for store rebuild + scene loads and requests a reapply pass.
    /// Part 2 will implement the actual pass (finding FlagsIO causes/receivers and applying).
    /// </summary>
    [DefaultExecutionOrder(-9990)]
    [DisallowMultipleComponent]
    public sealed class FlagsHub : MonoBehaviour
    {
        [SerializeField] private bool autoReapply = true;
        [SerializeField, Min(0)] private int delayFrames = 1;

        [Header("Debug")]
        [SerializeField] private bool debugLogs = false;

        private FlagsService _flags;
        private Coroutine _routine;

        private int _requestedRevision = -1;
        private int _lastAppliedRevision = -1;

        private void OnEnable()
        {
            Bind();

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;

            if (_flags != null)
            {
                _flags.StateRebuilt -= HandleStateRebuilt;
                _flags.StateRebuilt += HandleStateRebuilt;
            }

            if (autoReapply)
                RequestReapply();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            if (_flags != null)
                _flags.StateRebuilt -= HandleStateRebuilt;

            if (_routine != null) StopCoroutine(_routine);
            _routine = null;
        }

        private void Bind()
        {
            _flags = FlagsStatePersistence.Instance != null ? FlagsStatePersistence.Instance.Service : null;

            if (_flags == null)
            {
                var p = FindAnyObjectByType<FlagsStatePersistence>(FindObjectsInactive.Include);
                _flags = p != null ? p.Service : null;
            }

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][Hub] Bind -> {(_flags != null ? "OK" : "NULL")}", this);
        }

        private void HandleStateRebuilt()
        {
            if (!autoReapply) return;

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][Hub] StateRebuilt rev={_flags?.Revision}", this);

            RequestReapply();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!autoReapply) return;

            if (debugLogs)
                Debug.Log($"[CrowSave.Flags][Hub] SceneLoaded '{scene.name}' mode={mode}", this);

            RequestReapply();
        }

        public void RequestReapply()
        {
            Bind();
            if (_flags == null) return;

            _requestedRevision = Mathf.Max(_requestedRevision, _flags.Revision);

            if (_routine == null)
                _routine = StartCoroutine(ReapplyLoop());
        }

        private IEnumerator ReapplyLoop()
        {
            while (true)
            {
                for (int i = 0; i < delayFrames; i++)
                    yield return null;

                Bind();
                if (_flags == null)
                {
                    if (debugLogs)
                        Debug.Log("[CrowSave.Flags][Hub] ReapplyLoop: no FlagsService, stopping.", this);

                    _routine = null;
                    yield break;
                }

                int revAtApply = _flags.Revision;

                if (debugLogs)
                    Debug.Log($"[CrowSave.Flags][Hub] Reapply begin (revAtApply={revAtApply}, requested={_requestedRevision}, lastApplied={_lastAppliedRevision})", this);
                    var causes = FindObjectsByType<FlagsIOCause>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None
                    );

                for (int i = 0; i < causes.Length; i++)
                {
                    var c = causes[i];
                    if (c == null) continue;

                    var s = c.gameObject.scene;
                    if (!s.IsValid() || !s.isLoaded) continue;

                    c.ApplyFromStore(_flags);
                }
                _lastAppliedRevision = revAtApply;

                if (_flags.Revision > _lastAppliedRevision || _requestedRevision > _lastAppliedRevision)
                {
                    if (debugLogs)
                        Debug.Log($"[CrowSave.Flags][Hub] Reapply end -> looping (revNow={_flags.Revision})", this);

                    continue;
                }

                if (debugLogs)
                    Debug.Log("[CrowSave.Flags][Hub] Reapply end -> done", this);

                _routine = null;
                yield break;
            }
        }
    }
}
