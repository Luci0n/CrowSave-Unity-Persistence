using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    public sealed class DdolRoot : MonoBehaviour
    {
        private static DdolRoot _instance;

        public static DdolRoot Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
    }
}
