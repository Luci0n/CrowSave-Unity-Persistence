using System;
using CrowSave.Flags.Runtime;

namespace CrowSave.Flags.IO.Outputs
{
    [Serializable]
    public abstract class FlagsOutputModule
    {
        /// <summary>
        /// Invoke = caused by runtime input.
        /// Stateful outputs should write to FlagsService and apply immediately.
        /// </summary>
        public abstract void Invoke(FlagsIOCause host, FlagsService flags, string scopeKey);

        /// <summary>
        /// Restore = projector pass (scene loaded / disk loaded / rebuilt).
        /// Output should read its stored channels and apply if present.
        /// If no stored value exists, do nothing.
        /// </summary>
        public abstract void Restore(FlagsIOCause host, FlagsService flags, string scopeKey);
    }
}
