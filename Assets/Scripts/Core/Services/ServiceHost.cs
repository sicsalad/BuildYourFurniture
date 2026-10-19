using System;
using System.Collections.Generic;

namespace BuildYourFurniture.Core.Services
{
    /// <summary>
    /// Global static service registry keyed by interface type.
    /// </summary>
    public static class ServiceHost
    {
        private static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        public static void Register<TInterface>(TInterface service, bool overwrite = true) where TInterface : class
        {
            if (service == null)
                throw new ArgumentNullException(nameof(service));

            var key = typeof(TInterface);

            if (!overwrite && Services.ContainsKey(key))
                throw new InvalidOperationException($"Service already registered for {key.FullName}");

            Services[key] = service;
        }

        public static bool TryGet<TInterface>(out TInterface service) where TInterface : class
        {
            if (Services.TryGetValue(typeof(TInterface), out var raw) && raw is TInterface typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        public static TInterface Get<TInterface>() where TInterface : class
        {
            if (TryGet<TInterface>(out var service))
                return service;

            throw new InvalidOperationException($"Service not registered for {typeof(TInterface).FullName}");
        }

        public static bool Unregister<TInterface>() where TInterface : class
        {
            return Services.Remove(typeof(TInterface));
        }

        public static void Clear()
        {
            Services.Clear();
        }
    }
}