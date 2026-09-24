using UnityEngine;

namespace Woodsmen.Utilities
{
    /// <summary>
    /// Extension methods providing defensive, zero-allocation component retrieval.
    /// Ensures null-safe handling across parents and children without throwing exceptions.
    /// </summary>
    public static class ComponentExtensions
    {
        /// <summary>
        /// Attempts to get a component in the parent hierarchy safely.
        /// </summary>
        public static bool TryGetComponentInParent<T>(this GameObject gameObject, out T component) where T : class
        {
            component = gameObject.GetComponentInParent<T>();
            return component != null;
        }

        /// <summary>
        /// Attempts to get a component in the parent hierarchy safely from a Component.
        /// </summary>
        public static bool TryGetComponentInParent<T>(this Component source, out T component) where T : class
        {
            if (source == null)
            {
                component = null;
                return false;
            }

            component = source.GetComponentInParent<T>();
            return component != null;
        }

        /// <summary>
        /// Attempts to get a component in the children hierarchy safely.
        /// </summary>
        public static bool TryGetComponentInChildren<T>(this GameObject gameObject, out T component, bool includeInactive = false) where T : class
        {
            component = gameObject.GetComponentInChildren<T>(includeInactive);
            return component != null;
        }

        /// <summary>
        /// Attempts to get a component in the children hierarchy safely from a Component.
        /// </summary>
        public static bool TryGetComponentInChildren<T>(this Component source, out T component, bool includeInactive = false) where T : class
        {
            if (source == null)
            {
                component = null;
                return false;
            }

            component = source.GetComponentInChildren<T>(includeInactive);
            return component != null;
        }
    }
}
