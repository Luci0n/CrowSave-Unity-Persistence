using System;
using UnityEngine;

namespace CrowSave.Persistence.Runtime
{
    /// <summary>
    /// Static facade to access the container. Keeps gameplay code simple.
    /// Container is owned by the Bootstrap.
    /// </summary>
    public static class PersistenceServices
    {
        private static ServiceContainer _container;

        public static bool IsReady => _container != null;

        public static void Bind(ServiceContainer container)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
        }

        public static T Get<T>() where T : class
        {
            if (_container == null)
                throw new InvalidOperationException("PersistenceServices not initialized. Ensure Bootstrap exists in the first loaded scene.");

            return _container.Get<T>();
        }

        public static bool TryGet<T>(out T instance) where T : class
        {
            instance = null;
            if (_container == null) return false;
            return _container.TryGet(out instance);
        }

        /// <summary>
        /// Helpful for debugging: if Bootstrap got destroyed unexpectedly.
        /// </summary>
        public static void AssertReady(MonoBehaviour ctx)
        {
            if (_container == null)
                Debug.LogError("PersistenceServices not ready. Missing Bootstrap/DDOL root.", ctx);
        }
        
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _container = null;
        }
    }
    
}
