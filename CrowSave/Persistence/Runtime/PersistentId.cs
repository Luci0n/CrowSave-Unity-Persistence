using UnityEngine;
using UnityEngine.SceneManagement;
using CrowSave.Persistence.Save;

namespace CrowSave.Persistence.Runtime
{
    [DisallowMultipleComponent]
    public sealed class PersistentId : MonoBehaviour
    {
        // Public so other systems (orchestrator/docs) can reference the canonical key.
        public const string GlobalScopeKey = "__global__";

        [SerializeField] private string entityId;

        [Header("Scope")]

        [Tooltip(
            "If enabled, this entity registers into the GLOBAL scope so it persists across scenes.\n" +
            "Use this for things like the Player, inventory, meta progression, managers, etc."
        )]
        [SerializeField] private bool globalScope = false;

        [Tooltip(
            "Optional explicit scope key override.\n" +
            "If set, it overrides both Scene scope and Global scope.\n" +
            "Leave empty unless you know you need a custom scope namespace."
        )]
        [SerializeField] private string scopeOverride; // optional: force scope key

        public string EntityId => entityId;
        public bool IsGlobalScope => globalScope;

        public string ScopeKey
        {
            get
            {
                if (!string.IsNullOrEmpty(scopeOverride))
                    return scopeOverride;

                if (globalScope)
                    return GlobalScopeKey;

                // IMPORTANT: must match SaveOrchestrator's scope keys
                var mode = SceneIdentityMode.SceneName;

                var orch = SaveOrchestrator.Instance;
                if (orch != null)
                {
                    var cfg = orch.GetConfig();
                    if (cfg != null) mode = cfg.sceneIdentityMode;
                }

                Scene myScene = gameObject.scene;
                return SceneIdentity.GetSceneId(myScene, mode);
            }
        }

        public bool HasValidId => !string.IsNullOrWhiteSpace(entityId);

#if UNITY_EDITOR
        public void EditorGenerateIfMissing()
        {
            if (!HasValidId)
                entityId = System.Guid.NewGuid().ToString("N");
        }

        public void EditorRegenerate()
        {
            entityId = System.Guid.NewGuid().ToString("N");
        }
#endif
    }
}
