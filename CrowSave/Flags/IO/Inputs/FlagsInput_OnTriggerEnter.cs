using System;
using UnityEngine;

namespace CrowSave.Flags.IO.Inputs
{
    [Serializable]
    public sealed class FlagsInput_OnTriggerEnter : FlagsInput_ColliderBase
    {
        public override void OnTriggerEnter(FlagsIOCause host, Collider other)
        {
            if (host == null || other == null) return;
            if (!PassesFilter(other)) return;

            Fire(host);
        }
    }
}
