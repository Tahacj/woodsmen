using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;


namespace JustAGame
{
    /// <summary>
    /// Utility and extension methods for inline null checks, safe component queries, and high-performance operations.
    /// Available across all JustAGame sub-namespaces.
    /// </summary>
    public static class ObjectExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull(this UnityEngine.Object obj)
        {
            return obj == null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNotNull(this UnityEngine.Object obj)
        {
            return obj != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNull<T>(this T obj) where T : class
        {
            if (obj is UnityEngine.Object unityObj)
            {
                return unityObj == null;
            }
            else
            {
                return obj == null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNotNull<T>(this T obj) where T : class
        {
            if (obj is UnityEngine.Object unityObj)
            {
                return unityObj != null;
            }
            else
            {
                return obj != null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent GetInChildrenOrNull<TComponent>(this Component component, bool includeInactive = true) where TComponent : Component
        {
            try
            {
                if (component.IsNull())
                {
                    return null;
                }
                else
                {
                    return component.GetComponentInChildren<TComponent>(includeInactive);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetInChildrenOrNull<{typeof(TComponent).Name}>: {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent GetInChildrenOrNull<TComponent>(this GameObject gameObject, bool includeInactive = true) where TComponent : Component
        {
            try
            {
                if (gameObject.IsNull())
                {
                    return null;
                }
                else
                {
                    return gameObject.GetComponentInChildren<TComponent>(includeInactive);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetInChildrenOrNull<{typeof(TComponent).Name}>: {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent GetComponentOrNull<TComponent>(this Component component) where TComponent : Component
        {
            try
            {
                if (component.IsNull())
                {
                    return null;
                }
                else
                {
                    return component.GetComponent<TComponent>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetComponentOrNull<{typeof(TComponent).Name}>: {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent GetComponentOrNull<TComponent>(this GameObject gameObject) where TComponent : Component
        {
            try
            {
                if (gameObject.IsNull())
                {
                    return null;
                }
                else
                {
                    return gameObject.GetComponent<TComponent>();
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetComponentOrNull<{typeof(TComponent).Name}>: {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent GetComponentInParentOrNull<TComponent>(this Component component, bool includeInactive = true) where TComponent : Component
        {
            try
            {
                if (component.IsNull())
                {
                    return null;
                }
                else
                {
                    return component.GetComponentInParent<TComponent>(includeInactive);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetComponentInParentOrNull<{typeof(TComponent).Name}>: {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent GetComponentInParentOrNull<TComponent>(this GameObject gameObject, bool includeInactive = true) where TComponent : Component
        {
            try
            {
                if (gameObject.IsNull())
                {
                    return null;
                }
                else
                {
                    return gameObject.GetComponentInParent<TComponent>(includeInactive);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetComponentInParentOrNull<{typeof(TComponent).Name}>: {ex.Message}");
                return null;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent[] GetComponentsInChildrenOrEmpty<TComponent>(this Component component, bool includeInactive = true) where TComponent : Component
        {
            try
            {
                if (component.IsNull())
                {
                    return Array.Empty<TComponent>();
                }
                else
                {
                    return component.GetComponentsInChildren<TComponent>(includeInactive);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetComponentsInChildrenOrEmpty<{typeof(TComponent).Name}>: {ex.Message}");
                return Array.Empty<TComponent>();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TComponent[] GetComponentsInChildrenOrEmpty<TComponent>(this GameObject gameObject, bool includeInactive = true) where TComponent : Component
        {
            try
            {
                if (gameObject.IsNull())
                {
                    return Array.Empty<TComponent>();
                }
                else
                {
                    return gameObject.GetComponentsInChildren<TComponent>(includeInactive);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectExtensions] Failed to GetComponentsInChildrenOrEmpty<{typeof(TComponent).Name}>: {ex.Message}");
                return Array.Empty<TComponent>();
            }
        }
    }
}

namespace JustAGame.Pooling
{
    /// <summary>
    /// Generic, high-performance object pool utilizing LIFO Stack (eliminating FIFO queue overhead and maximizing CPU cache locality).
    /// Prevents GC allocations from repeated Instantiate and Destroy calls.
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _pool;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onReturn;

        public int TotalCount => _pool.IsNotNull() ? _pool.Count : 0;

        public ObjectPool(
            T prefab,
            int initialCapacity = 10,
            Transform parent = null,
            Action<T> onGet = null,
            Action<T> onReturn = null)
        {
            try
            {
                if (prefab.IsNull())
                {
                    Debug.LogError($"[ObjectPool] Cannot initialize ObjectPool<{typeof(T).Name}> with a null prefab.");
                    _prefab = null;
                    _parent = null;
                    _pool = new Stack<T>();
                    _onGet = null;
                    _onReturn = null;
                }
                else
                {
                    _prefab = prefab;
                    _parent = parent;
                    int safeCapacity = initialCapacity > 0 ? initialCapacity : 10;
                    _pool = new Stack<T>(safeCapacity);
                    _onGet = onGet;
                    _onReturn = onReturn;

                    Prewarm(safeCapacity);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectPool] Exception initializing ObjectPool<{typeof(T).Name}>: {ex.Message}");
                _pool = _pool.IsNotNull() ? _pool : new Stack<T>();
            }
        }

        public void Prewarm(int count)
        {
            try
            {
                if (_prefab.IsNull())
                {
                    Debug.LogWarning($"[ObjectPool] Prewarm aborted for {typeof(T).Name}: Prefab is null.");
                }
                else
                {
                    int safeCount = count > 0 ? count : 0;
                    for (int i = 0; i < safeCount; i++)
                    {
                        T instance = CreateNewInstance();
                        if (instance.IsNotNull())
                        {
                            instance.gameObject.SetActive(false);
                            _pool.Push(instance);
                        }
                        else
                        {
                            Debug.LogWarning($"[ObjectPool] Failed to instantiate object during prewarm for {typeof(T).Name}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectPool] Exception in Prewarm for {typeof(T).Name}: {ex.Message}");
            }
        }

        private T CreateNewInstance()
        {
            try
            {
                if (_prefab.IsNull())
                {
                    Debug.LogError($"[ObjectPool] Cannot create instance: Prefab {typeof(T).Name} is null.");
                    return null;
                }
                else
                {
                    T instance = _parent.IsNotNull()
                        ? UnityEngine.Object.Instantiate(_prefab, _parent)
                        : UnityEngine.Object.Instantiate(_prefab);

                    return instance;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectPool] Exception creating instance of {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }

        public T Get(Vector3 position = default, Quaternion rotation = default)
        {
            try
            {
                T instance = null;

                while (_pool.Count > 0)
                {
                    T candidate = _pool.Pop();
                    if (candidate.IsNotNull())
                    {
                        instance = candidate;
                        break;
                    }
                    else
                    {
                        // Clean up reference to destroyed object and continue checking
                        continue;
                    }
                }

                if (instance.IsNull())
                {
                    instance = CreateNewInstance();
                }
                else
                {
                    // Reusing valid instance popped from LIFO stack
                }

                if (instance.IsNotNull())
                {
                    Transform t = instance.transform;
                    t.SetPositionAndRotation(position, rotation);
                    instance.gameObject.SetActive(true);

                    try
                    {
                        _onGet?.Invoke(instance);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[ObjectPool] Error invoking _onGet for {typeof(T).Name}: {ex.Message}");
                    }

                    return instance;
                }
                else
                {
                    Debug.LogError($"[ObjectPool] Failed to acquire or instantiate an instance for {typeof(T).Name}.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectPool] Exception in Get for {typeof(T).Name}: {ex.Message}");
                return null;
            }
        }

        public void Return(T instance)
        {
            try
            {
                if (instance.IsNull())
                {
                    Debug.LogWarning($"[ObjectPool] Return called with null or destroyed instance for {typeof(T).Name}.");
                    return;
                }
                else
                {
                    if (_pool.Contains(instance))
                    {
                        Debug.LogWarning($"[ObjectPool] Instance {instance.name} is already returned to the pool. Double return prevented.");
                        return;
                    }
                    else
                    {
                        try
                        {
                            _onReturn?.Invoke(instance);
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[ObjectPool] Error invoking _onReturn for {typeof(T).Name}: {ex.Message}");
                        }

                        instance.gameObject.SetActive(false);

                        if (_parent.IsNotNull())
                        {
                            instance.transform.SetParent(_parent);
                        }
                        else
                        {
                            // Maintain existing parent hierarchy
                        }

                        _pool.Push(instance);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectPool] Exception in Return for {typeof(T).Name}: {ex.Message}");
            }
        }

        public void Clear()
        {
            try
            {
                while (_pool.Count > 0)
                {
                    T instance = _pool.Pop();
                    if (instance.IsNotNull())
                    {
                        UnityEngine.Object.Destroy(instance.gameObject);
                    }
                    else
                    {
                        // Already destroyed or null reference discarded
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ObjectPool] Exception in Clear for {typeof(T).Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Component-based GameObject pool for scene inspector setup utilizing LIFO Stack.
    /// </summary>
    public class GameObjectPool : MonoBehaviour
    {
        [SerializeField] private GameObject prefab;
        [SerializeField] private int initialPoolSize = 10;

        private readonly Stack<GameObject> _pool = new Stack<GameObject>();

        private void Awake()
        {
            try
            {
                if (prefab.IsNotNull())
                {
                    int safeCount = initialPoolSize > 0 ? initialPoolSize : 10;
                    for (int i = 0; i < safeCount; i++)
                    {
                        GameObject obj = Instantiate(prefab, transform);
                        if (obj.IsNotNull())
                        {
                            obj.SetActive(false);
                            _pool.Push(obj);
                        }
                        else
                        {
                            Debug.LogWarning("[GameObjectPool] Failed to instantiate prefab during Awake.");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("[GameObjectPool] Prefab reference is null on Awake.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameObjectPool] Exception during Awake: {ex.Message}");
            }
        }

        public GameObject Get(Vector3 position = default, Quaternion rotation = default)
        {
            try
            {
                GameObject obj = null;

                while (_pool.Count > 0)
                {
                    GameObject candidate = _pool.Pop();
                    if (candidate.IsNotNull())
                    {
                        obj = candidate;
                        break;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (obj.IsNull())
                {
                    if (prefab.IsNotNull())
                    {
                        obj = Instantiate(prefab, position, rotation, transform);
                    }
                    else
                    {
                        Debug.LogError("[GameObjectPool] Cannot instantiate: prefab is null.");
                        return null;
                    }
                }
                else
                {
                    obj.transform.SetPositionAndRotation(position, rotation);
                }

                if (obj.IsNotNull())
                {
                    obj.SetActive(true);
                    return obj;
                }
                else
                {
                    Debug.LogError("[GameObjectPool] Failed to acquire or create GameObject.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameObjectPool] Exception in Get: {ex.Message}");
                return null;
            }
        }

        public void Return(GameObject obj)
        {
            try
            {
                if (obj.IsNull())
                {
                    Debug.LogWarning("[GameObjectPool] Attempted to return null or destroyed GameObject.");
                    return;
                }
                else
                {
                    if (_pool.Contains(obj))
                    {
                        Debug.LogWarning($"[GameObjectPool] GameObject '{obj.name}' is already in pool. Double return prevented.");
                        return;
                    }
                    else
                    {
                        obj.SetActive(false);
                        obj.transform.SetParent(transform);
                        _pool.Push(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameObjectPool] Exception in Return: {ex.Message}");
            }
        }

        private void OnDestroy()
        {
            try
            {
                while (_pool.Count > 0)
                {
                    GameObject obj = _pool.Pop();
                    if (obj.IsNotNull())
                    {
                        Destroy(obj);
                    }
                    else
                    {
                        // Discard already destroyed instance
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameObjectPool] Exception in OnDestroy: {ex.Message}");
            }
        }
    }
}
