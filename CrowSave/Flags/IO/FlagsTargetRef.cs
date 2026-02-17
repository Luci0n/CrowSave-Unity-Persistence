using System;
using UnityEngine;
using CrowSave.Persistence.Runtime;

namespace CrowSave.Flags.IO
{
    [Serializable]
    public struct FlagsTargetRef
    {
        public enum Mode
        {
            DirectGameObject = 0,
            LocalId = 1,
            PersistentIdComponent = 2,
            ObjectName = 3,
            PersistentEntityIdString = 4
        }

        [SerializeField] private Mode mode;

        [SerializeField] private GameObject direct;
        [SerializeField] private string localId;
        [SerializeField] private PersistentId persistentId;
        [SerializeField] private string objectName;

        [SerializeField, Tooltip("Optional scene name filter for LocalId/ObjectName/PersistentEntityId.")]
        private string sceneName;

        [SerializeField, Tooltip("Resolve by PersistentId entityId string.")]
        private string persistentEntityId;

        public Mode RefMode => mode;

        public string SceneNameFilter => sceneName ?? "";

        public GameObject Direct => direct;
        public string LocalId => localId ?? "";
        public PersistentId PersistentId => persistentId;
        public string ObjectName => objectName ?? "";
        public string PersistentEntityId => persistentEntityId ?? "";
    }
}
