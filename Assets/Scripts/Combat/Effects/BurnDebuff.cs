using System.Collections;
using UnityEngine;

namespace Woodsmen.Combat.Effects
{
    /// <summary>
    /// Status effect applied to enemies struck by Fire Infusion.
    /// Manages:
    /// - Spawning and anchoring the Fire VFX to the enemy's body.
    /// - Procedural fire ember fallback if a custom VFX prefab is not yet assigned.
    /// - Periodic damage-over-time (DoT) fire ticks.
    /// - Automatic cleanup when extinguished or on enemy death.
    /// </summary>
    [DisallowMultipleComponent]
    public class BurnDebuff : MonoBehaviour
    {
        [Header("Burn Parameters")]
        [SerializeField] private float duration = 3.5f;
        [SerializeField] private float tickInterval = 0.5f;
        [SerializeField] private float damagePerTick = 5f;

        [Header("Visual Effects")]
        [Tooltip("Per-hit impact flash VFX (spawned once on hit, not parented).")]
        [SerializeField] private GameObject hitFlashVfxPrefab;

        [Tooltip("Body burn VFX parented to the enemy and kept alive for the full burn duration.")]
        [SerializeField] private GameObject burnBodyVfxPrefab;

        private float _remainingDuration;
        private GameObject _spawnedHitFlash;
        private GameObject _spawnedBodyVfx;
        private Coroutine _burnCoroutine;
        private CharacterHealth _targetHealth;
        private GameObject _attacker;

        /// <summary>
        /// Applies or refreshes the BurnDebuff on a target GameObject.
        /// </summary>
        /// <param name="hitFlashVfx">Impact flash spawned once on hit (not parented to enemy).</param>
        /// <param name="burnBodyVfx">Body fire VFX parented to the enemy for the full burn duration.</param>
        public static BurnDebuff Apply(GameObject target, GameObject hitFlashVfx, GameObject burnBodyVfx, float burnDuration, float tickDamage, GameObject attacker = null)
        {
            if (target == null) return null;

            var debuff = target.GetComponent<BurnDebuff>();
            if (debuff == null)
            {
                debuff = target.AddComponent<BurnDebuff>();
            }

            debuff.Initialize(hitFlashVfx, burnBodyVfx, burnDuration, tickDamage, attacker);
            return debuff;
        }

        private void Initialize(GameObject hitFlashVfx, GameObject burnBodyVfx, float burnDuration, float tickDamage, GameObject attacker)
        {
            duration = burnDuration > 0 ? burnDuration : 3.5f;
            damagePerTick = tickDamage > 0 ? tickDamage : 5f;
            _attacker = attacker;
            _remainingDuration = duration;

            if (hitFlashVfx != null) hitFlashVfxPrefab = hitFlashVfx;
            if (burnBodyVfx != null) burnBodyVfxPrefab = burnBodyVfx;

            // Spawn the one-off impact flash (not parented so it doesn't move weirdly)
            if (hitFlashVfxPrefab != null)
            {
                var flash = Instantiate(hitFlashVfxPrefab, transform.position + Vector3.up * 1f, Quaternion.identity);
                Destroy(flash, 2.0f); // Fallback cleanup in case prefab lacks auto-destroy
            }

            _targetHealth = GetComponent<CharacterHealth>();

            // Spawn the body fire VFX as a persistent child of the enemy
            AttachBodyBurnVfx();

            // Start or refresh burning coroutine
            if (_burnCoroutine != null)
            {
                StopCoroutine(_burnCoroutine);
            }
            _burnCoroutine = StartCoroutine(BurnRoutine());
        }

        /// <summary>Spawns the body-attached burn VFX as a child of the enemy at its center.</summary>
        private void AttachBodyBurnVfx()
        {
            // Don't re-spawn if already burning
            if (_spawnedBodyVfx != null) return;

            if (burnBodyVfxPrefab != null)
            {
                // Parent it directly to the enemy so it follows all movement
                _spawnedBodyVfx = Instantiate(burnBodyVfxPrefab, transform.position, Quaternion.identity, transform);
                _spawnedBodyVfx.transform.localPosition = Vector3.zero;
            }
            else
            {
                // Procedural fallback if no prefab is assigned
                _spawnedBodyVfx = CreateProceduralFireVfx();
            }
        }

        private GameObject CreateProceduralFireVfx()
        {
            var vfxGo = new GameObject("ProceduralFireVFX");
            vfxGo.transform.SetParent(transform, false);
            vfxGo.transform.localPosition = new Vector3(0f, 0.8f, 0f);

            var ps = vfxGo.AddComponent<ParticleSystem>();
            var psRenderer = vfxGo.GetComponent<ParticleSystemRenderer>();
            if (psRenderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Particles/Standard Unlit")
                             ?? Shader.Find("Mobile/Particles/Additive")
                             ?? Shader.Find("Legacy Shaders/Particles/Additive")
                             ?? Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.color = new Color(1f, 0.45f, 0.1f, 1f);
                    psRenderer.material = mat;
                }
            }

            var main = ps.main;
            main.startLifetime = 0.5f;
            main.startSpeed = 1.2f;
            main.startSize = 0.35f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.45f, 0.1f, 1f), new Color(1f, 0.15f, 0.05f, 1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = ps.emission;
            emission.rateOverTime = 25f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(1.5f);

            // Add faint orange point light for glowing ambiance
            var light = vfxGo.AddComponent<Light>();
            light.color = new Color(1f, 0.5f, 0.1f);
            light.range = 3f;
            light.intensity = 1.8f;

            return vfxGo;
        }

        private IEnumerator BurnRoutine()
        {
            while (_remainingDuration > 0f)
            {
                yield return new WaitForSeconds(tickInterval);
                _remainingDuration -= tickInterval;

                if (_targetHealth != null && !_targetHealth.IsDead)
                {
                    // Apply DoT tick damage
                    _targetHealth.TakeDamage(damagePerTick, transform.position + Vector3.up * 0.8f, Vector3.up, _attacker);
                }

                // If target died during burn, stop immediately
                if (_targetHealth != null && _targetHealth.IsDead)
                {
                    break;
                }
            }

            Extinguish();
        }

        private void Extinguish()
        {
            // Destroy body burn VFX
            if (_spawnedBodyVfx != null)
            {
                var ps = _spawnedBodyVfx.GetComponentInChildren<ParticleSystem>();
                if (ps != null)
                {
                    ps.Stop();
                    Destroy(_spawnedBodyVfx, 1.0f);
                }
                else
                {
                    Destroy(_spawnedBodyVfx);
                }
                _spawnedBodyVfx = null;
            }

            // Destroy hit flash VFX if still alive
            if (_spawnedHitFlash != null)
            {
                Destroy(_spawnedHitFlash);
                _spawnedHitFlash = null;
            }

            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_burnCoroutine != null)
            {
                StopCoroutine(_burnCoroutine);
            }
            if (_spawnedBodyVfx != null)
            {
                Destroy(_spawnedBodyVfx);
            }
            if (_spawnedHitFlash != null)
            {
                Destroy(_spawnedHitFlash);
            }
        }
    }
}
