using System;
using UnityEngine;

namespace CrowSave.Flags.IO.Inputs
{
    [Serializable]
    public abstract class FlagsInput_ColliderBase : FlagsInputModule
    {
        [Header("Filter")]
        [SerializeField] private bool requireTag = true;
        [SerializeField] private string requiredTag = "Player";

        [SerializeField] private bool requireLayer = false;
        [SerializeField] private LayerMask requiredLayers = ~0;

        protected bool PassesFilter(Collider other)
        {
            if (other == null) return false;

            if (requireTag && !string.IsNullOrWhiteSpace(requiredTag))
            {
                if (!other.CompareTag(requiredTag))
                    return false;
            }

            if (requireLayer)
            {
                int bit = 1 << other.gameObject.layer;
                if ((requiredLayers.value & bit) == 0)
                    return false;
            }

            return true;
        }
    }
}
