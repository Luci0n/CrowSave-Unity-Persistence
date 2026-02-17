using System;

namespace CrowSave.Flags.Core
{
    public static class FlagsTargetKey
    {
        public static string FromPersistentId(string entityId)
        {
            entityId = FlagsKeyUtil.Normalize(entityId);
            return entityId.Length == 0 ? "" : $"pid:{entityId}";
        }

        public static string FromLocalId(string localId)
        {
            localId = FlagsKeyUtil.Normalize(localId);
            return localId.Length == 0 ? "" : $"lid:{localId}";
        }

        public static string FromName(string objectName)
        {
            objectName = FlagsKeyUtil.Normalize(objectName);
            return objectName.Length == 0 ? "" : $"name:{objectName}";
        }

        public static bool TryParse(string targetKey, out string prefix, out string value)
        {
            targetKey = targetKey ?? "";
            int idx = targetKey.IndexOf(':');
            if (idx <= 0 || idx >= targetKey.Length - 1)
            {
                prefix = "";
                value = "";
                return false;
            }

            prefix = targetKey.Substring(0, idx);
            value = targetKey.Substring(idx + 1);
            return true;
        }
    }
}
