using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using Woodsmen.AI;

namespace Woodsmen.Combat.Effects
{
    /// <summary>
    /// Status effect applied to enemies struck by Frostbite Rune or frost-infused weapons.
    /// Manages:
    /// - Slowing enemy movement speed by a percentage (e.g. 25% reduction).
    /// - Spawning an icy chill visual aura on the enemy's body.
    /// - Procedural frost snowflake / mist fallback if no custom VFX prefab is assigned.
    /// - Restoring original movement speed and cleaning itself up when expired or on enemy death.
    /// </summary>
    [DisallowMultipleComponent]
    public class FrostDebuff : MonoBehaviour
    {
        [Header("Frost Parameters")]
        [SerializeField] private float duration = 4.0f;
        [SerializeField] private float slowPercentage = 0.25f;

        [Header("Visual Effects")]
        [SerializeField] private GameObject frostVfxPrefab;

        private float _remainingDuration;
        private GameObject _spawnedVfx;
        private Coroutine _frostCoroutine;
        private NavMeshAgent _navMeshAgent;
        private float _originalSpeed;
        private CharacterHealth _targetHealth;

        /// <summary>
        /// Applies or refreshes the FrostDebuff on a target GameObject.
        /// </summary>
        public static FrostDebuff Apply(GameObject target, GameObject vfxPrefab, float frostDuration, float slow, GameObject attacker = null)
        {
            if (target == null) return null;

            var debuff = target.GetComponent<FrostDebuff>();
            if (debuff == null)
            {
                debuff = target.AddComponent<FrostDebuff>();
            }

            debuff.Initialize(vfxPrefab, frostDuration, slow);
            return debuff;
        }

        private void Initialize(GameObject vfxPrefab, float frostDuration, float slow)
        {
            duration = frostDuration > 0f ? frostDuration : 4.0f;
            slowPercentage = Mathf.Clamp(slow > 0f ? slow : 0.25f, 0.05f, 0.9f);
            _remainingDuration = duration;

            if (vfxPrefab != null)
            {
                frostVfxPrefab = vfxPrefab;
            }

            _targetHealth = GetComponent<CharacterHealth>();
            if (_targetHealth != null)
            {
                _targetHealth.ApplyPersistentTint(Color.cyan);
            }

            _navMeshAgent = GetComponent<NavMeshAgent>();

            // Capture initial un-slowed speed
            if (_navMeshAgent != null && _originalSpeed <= 0.01f)
            {
                _originalSpeed = _navMeshAgent.speed;
            }

            // Apply slow penalty
            if (_navMeshAgent != null && _originalSpeed > 0f)
            {
                _navMeshAgent.speed = _originalSpeed * (1f - slowPercentage);
            }

            // Attach icy VFX
            AttachFrostVfx();

            // Start or refresh duration coroutine
            if (_frostCoroutine != null)
            {
                StopCoroutine(_frostCoroutine);
            }
            _frostCoroutine = StartCoroutine(FrostRoutine());
        }

        private void AttachFrostVfx()
        {
            if (_spawnedVfx != null) return;

            if (frostVfxPrefab != null)
            {
                Vector3 spawnPos = transform.position + Vector3.up * 0.8f;
                _spawnedVfx = Instantiate(frostVfxPrefab, spawnPos, Quaternion.identity, transform);
            }
            // Procedural VFX (particles & light) disabled. The persistent cyan skin tint is sufficient.
        }



        private IEnumerator FrostRoutine()
        {
            while (_remainingDuration > 0f)
            {
                yield return null;
                _remainingDuration -= Time.deltaTime;

                if (_targetHealth != null && _targetHealth.IsDead)
                {
                    break;
                }
            }

            RemoveDebuff();
        }

        private void RemoveDebuff()
        {
            // Restore original speed
            if (_navMeshAgent != null && _originalSpeed > 0f)
            {
                _navMeshAgent.speed = _originalSpeed;
            }

            if (_spawnedVfx != null)
            {
                var ps = _spawnedVfx.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    ps.Stop();
                    Destroy(_spawnedVfx, 0.8f);
                }
                else
                {
                    Destroy(_spawnedVfx);
                }
                _spawnedVfx = null;
            }

            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_targetHealth != null)
            {
                _targetHealth.ClearPersistentTint();
            }

            if (_navMeshAgent != null && _originalSpeed > 0f)
            {
                _navMeshAgent.speed = _originalSpeed;
            }
            if (_frostCoroutine != null)
            {
                StopCoroutine(_frostCoroutine);
            }
            if (_spawnedVfx != null)
            {
                Destroy(_spawnedVfx);
            }
        }
    }
}
