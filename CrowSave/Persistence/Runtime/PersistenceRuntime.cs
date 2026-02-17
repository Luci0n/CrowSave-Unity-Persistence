namespace CrowSave.Persistence.Runtime
{
    /// <summary>
    /// Back-compat shim. Do NOT allocate services here.
    /// </summary>
    public static class PersistenceRuntime
    {
        public static class Services
        {
            public static PersistenceRegistry Registry => PersistenceServices.Get<PersistenceRegistry>();
            public static WorldStateService WorldState => PersistenceServices.Get<WorldStateService>();
            public static CaptureApplyService CaptureApply => PersistenceServices.Get<CaptureApplyService>();

            public static bool TryRegistry(out PersistenceRegistry r) => PersistenceServices.TryGet(out r);
            public static bool TryWorldState(out WorldStateService w) => PersistenceServices.TryGet(out w);
            public static bool TryCaptureApply(out CaptureApplyService c) => PersistenceServices.TryGet(out c);
        }
    }
}
