using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    /// Ensures the Bootstrap prefab exists even if we start play from any scene.
    /// Place one in every scene (or use RuntimeInitializeOnLoadMethod version later).
    public sealed class BootstrapSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject bootstrapPrefab;

        private void Awake()
        {
            if (DdolRoot.Instance != null) return;

            if (bootstrapPrefab == null)
            {
                Debug.LogError("BootstrapSpawner missing Bootstrap prefab reference.", this);
                return;
            }

            Instantiate(bootstrapPrefab);
        }
    }
}
