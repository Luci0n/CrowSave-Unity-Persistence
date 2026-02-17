namespace CrowSave.Flags.Core
{
    public static class FlagsScope
    {
        public static string Normalize(string scopeKey)
            => FlagsKeyUtil.Normalize(scopeKey);
    }
}
