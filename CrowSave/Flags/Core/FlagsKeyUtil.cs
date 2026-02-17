using System;

namespace CrowSave.Flags.Core
{
    public static class FlagsKeyUtil
    {
        public static string Normalize(string s)
        {
            s = (s ?? "").Trim();
            return s;
        }

        public static bool IsValidNonEmpty(string s)
            => !string.IsNullOrWhiteSpace(s);

        public static string NormalizeChannel(string channel)
        {
            channel = Normalize(channel);
            return channel;
        }
    }
}
