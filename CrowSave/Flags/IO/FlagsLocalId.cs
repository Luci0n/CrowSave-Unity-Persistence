using UnityEngine;

namespace CrowSave.Flags.IO
{
    [DisallowMultipleComponent]
    public sealed class FlagsLocalId : MonoBehaviour
    {
        [SerializeField] private string id = "";

        public string Id => id ?? "";
    }
}
