using System;
using UnityEngine;

namespace CrowSave.Flags.IO.Inputs
{
    [Serializable]
    public sealed class FlagsInput_OnTriggerExit : FlagsInput_ColliderBase
    {
        public override void OnTriggerExit(FlagsIOCause host, Collider other)
        {
            if (host == null || other == null) return;
            if (!PassesFilter(other)) return;

            Fire(host);
        }
    }
}
