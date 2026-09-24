using System.Collections;
using Mirror;
using PrimeTween;
using UnityEngine;
using Woodsmen.CameraSystem;
using Woodsmen.Players;

namespace Woodsmen.Combat
{
    /// <summary>
    /// Professional, server-authoritative health and damage system for networked characters (Players, AI, Enemies).
    /// Features:
    /// - Mirror Network compliance: Server-authoritative health calculation with SyncVars and ClientRpcs.
    /// - Offline testing support: Full single-player fallback when NetworkClient/Server is inactive (Rule 3).
    /// - Animation dispatch: Automatically triggers Hit and Death animator parameters if present.
    /// - Procedural hit feedback: PrimeTween squash-and-stretch punch and zero-allocation MaterialPropertyBlock flash.
    /// - Camera shake isolation: Screen shake only triggers for the local player being damaged.
    /// - Clean component shutdown: Disables movement, colliders, and actions on death.
    /// - Extensible C# event hooks for UI health bars, floating damage numbers, and death screens.
    /// </summary>
    public class CharacterHealth : NetworkBehaviour, IDamageable
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float defense = 0f;
        [Tooltip("Invulnerability time in seconds after taking a hit to prevent multi-hit spam.")]
        [SerializeField] private float invulnerabilityDuration = 0.2f;

        [Header("Animation Parameters")]
        [SerializeField] private string hitTriggerName = "Hit";
        [SerializeField] private string deathTriggerName = "Death";
        [SerializeField] private string isDeadBoolName = "IsDead";

        [Header("Impact Feedback (Visual & Audio)")]
        [SerializeField] private ParticleSystem hitVFX;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip[] hurtAudioClips;
        [SerializeField] private AudioClip deathAudioClip;

        [Header("Procedural Impact (PrimeTween)")]
        [SerializeField] private bool useProceduralPunch = true;
        [SerializeField] private Vector3 punchScale = new Vector3(0.08f, -0.06f, 0.08f);
        [SerializeField] private float punchDuration = 0.2f;

        [Header("Damage Flash (Zero-GC MaterialPropertyBlock)")]
        [SerializeField] private bool useDamageFlash = true;
        [SerializeField] private Renderer[] flashRenderers;
        [SerializeField] private Color flashColor = new Color(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private float flashDuration = 0.12f;

        [Header("Lifecycle & Death")]
        [Tooltip("If true, destroys the GameObject after a delay on death (recommended for mobs/monsters). Keep false for players.")]
        [SerializeField] private bool destroyOnDeath = false;
        [SerializeField] private float destroyDelay = 3.0f;

        // --- Synchronized Network State ---
        [SyncVar(hook = nameof(OnHealthSyncChanged))]
        private float _currentHealth;

        [SyncVar(hook = nameof(OnDeadSyncChanged))]
        private bool _isDead;

        private float _lastHitTime = -100f;
        private bool _isInvulnerable;

        // Component References
        private Animator _animator;
        private CharacterController _characterController;
        private LocomotionController _locomotionController;
        private LumberjackChopping _choppingController;
        private Vector3 _originalScale;
        private Tween _punchTween;
        private Tween _flashTween;
        private MaterialPropertyBlock _propBlock;

        // Cached Animator Parameter Hashes
        private int _hitTriggerHash;
        private int _deathTriggerHash;
        private int _isDeadBoolHash;
        private bool _hasHitTrigger;
        private bool _hasDeathTrigger;
        private bool _hasIsDeadBool;

        private static readonly int BaseColorHash = Shader.PropertyToID("_BaseColor");

        #region Public Properties & Events

        public float CurrentHealth => _currentHealth;
        public float MaxHealth => maxHealth;
        public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(_currentHealth / maxHealth) : 0f;
        public bool IsDead => _isDead;
        public bool IsInvulnerable => _isInvulnerable;

        /// <summary>
        /// Invoked when current health changes. Arguments: (currentHealth, maxHealth).
        /// </summary>
        public event System.Action<float, float> OnHealthChanged;

        /// <summary>
        /// Invoked on clients when damage is received. Arguments: (damage, hitPoint, hitDirection).
        /// </summary>
        public event System.Action<float, Vector3, Vector3> OnDamaged;

        /// <summary>
        /// Invoked when this character dies.
        /// </summary>
        public event System.Action OnDeath;

        /// <summary>
        /// Invoked when this character is revived.
        /// </summary>
        public event System.Action OnRevived;

        #endregion

        private void Awake()
        {
            _originalScale = transform.localScale;
            _currentHealth = maxHealth;

            InitializeComponents();
            CacheAnimatorParameters();
        }

        private void InitializeComponents()
        {
            gameObject.TryGetComponent(out _animator);
            gameObject.TryGetComponent(out _characterController);
            gameObject.TryGetComponent(out _locomotionController);
            gameObject.TryGetComponent(out _choppingController);

            if (audioSource == null)
            {
                gameObject.TryGetComponent(out audioSource);
            }

            if (flashRenderers == null || flashRenderers.Length == 0)
            {
                flashRenderers = GetComponentsInChildren<Renderer>();
            }

            _propBlock = new MaterialPropertyBlock();
        }

        private void CacheAnimatorParameters()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            _hitTriggerHash = Animator.StringToHash(hitTriggerName);
            _deathTriggerHash = Animator.StringToHash(deathTriggerName);
            _isDeadBoolHash = Animator.StringToHash(isDeadBoolName);

            foreach (var param in _animator.parameters)
            {
                if (param.nameHash == _hitTriggerHash && param.type == AnimatorControllerParameterType.Trigger)
                {
                    _hasHitTrigger = true;
                }
                else if (param.nameHash == _deathTriggerHash && param.type == AnimatorControllerParameterType.Trigger)
                {
                    _hasDeathTrigger = true;
                }
                else if (param.nameHash == _isDeadBoolHash && param.type == AnimatorControllerParameterType.Bool)
                {
                    _hasIsDeadBool = true;
                }
            }
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _currentHealth = maxHealth;
            _isDead = false;
        }

        private void OnDestroy()
        {
            if (_punchTween.isAlive) _punchTween.Stop();
            if (_flashTween.isAlive) _flashTween.Stop();
        }

        #region Damage Processing (Server Authoritative & Offline Safe)

        /// <summary>
        /// Applies damage to this character. Safe to call from anywhere (weapons, hitboxes, projectiles).
        /// Routes through server in Mirror networking, or processes directly in offline mode.
        /// </summary>
        public void TakeDamage(float damage, Vector3 hitPoint = default, Vector3 hitDirection = default, GameObject attacker = null)
        {
            if (_isDead || _isInvulnerable) return;

            // Debounce i-frame guard
            if (Time.time - _lastHitTime < invulnerabilityDuration) return;
            _lastHitTime = Time.time;

            if (isServer)
            {
                ServerApplyDamage(damage, hitPoint, hitDirection, attacker);
            }
            else if (!NetworkClient.active && !NetworkServer.active)
            {
                // Offline / Standalone Testing Mode (Rule 3)
                float finalDamage = Mathf.Max(1f, damage - defense);
                _currentHealth = Mathf.Max(0f, _currentHealth - finalDamage);

                PlayHitFeedback(finalDamage, hitPoint, hitDirection);
                OnDamaged?.Invoke(finalDamage, hitPoint, hitDirection);
                OnHealthChanged?.Invoke(_currentHealth, maxHealth);

                if (_currentHealth <= 0f)
                {
                    _isDead = true;
                    HandleDeath(hitDirection);
                    OnDeath?.Invoke();
                }
            }
        }

        [Server]
        private void ServerApplyDamage(float damage, Vector3 hitPoint, Vector3 hitDirection, GameObject attacker)
        {
            if (_isDead || _isInvulnerable) return;

            float finalDamage = Mathf.Max(1f, damage - defense);
            _currentHealth = Mathf.Max(0f, _currentHealth - finalDamage);

            // Notify all clients of the hit event
            RpcOnDamaged(finalDamage, hitPoint, hitDirection);

            if (_currentHealth <= 0f)
            {
                _isDead = true;
                RpcOnDeath(hitDirection);

                if (destroyOnDeath)
                {
                    StartCoroutine(ServerDestroyDelayed());
                }
            }
        }

        [Server]
        private IEnumerator ServerDestroyDelayed()
        {
            yield return new WaitForSeconds(destroyDelay);
            NetworkServer.Destroy(gameObject);
        }

        #endregion

        #region Network Synchronization Hooks & Client RPCs

        private void OnHealthSyncChanged(float oldHealth, float newHealth)
        {
            OnHealthChanged?.Invoke(newHealth, maxHealth);
        }

        private void OnDeadSyncChanged(bool oldDead, bool newDead)
        {
            if (newDead && !oldDead)
            {
                HandleDeath(Vector3.zero);
                OnDeath?.Invoke();
            }
        }

        [ClientRpc]
        private void RpcOnDamaged(float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            PlayHitFeedback(damage, hitPoint, hitDirection);
            OnDamaged?.Invoke(damage, hitPoint, hitDirection);
        }

        [ClientRpc]
        private void RpcOnDeath(Vector3 deathDirection)
        {
            HandleDeath(deathDirection);
            OnDeath?.Invoke();
        }

        #endregion

        #region Feedback & Animation Dispatch

        private void PlayHitFeedback(float damage, Vector3 hitPoint, Vector3 hitDirection)
        {
            // 1. Animator Trigger
            if (_animator != null && _hasHitTrigger)
            {
                _animator.SetTrigger(_hitTriggerHash);
            }

            // 2. Procedural PrimeTween Wobble Punch
            if (useProceduralPunch)
            {
                if (_punchTween.isAlive)
                {
                    _punchTween.Stop();
                    transform.localScale = _originalScale;
                }
                _punchTween = Tween.PunchScale(transform, punchScale, duration: punchDuration, frequency: 12);
            }

            // 3. Material Damage Flash (Zero-GC MaterialPropertyBlock)
            if (useDamageFlash && flashRenderers != null && flashRenderers.Length > 0)
            {
                TriggerDamageFlash();
            }

            // 4. Hit VFX
            if (hitVFX != null)
            {
                if (hitPoint != default)
                {
                    hitVFX.transform.position = hitPoint;
                }
                hitVFX.Play(true);
            }

            // 5. Audio Feedback
            if (audioSource != null && hurtAudioClips != null && hurtAudioClips.Length > 0)
            {
                AudioClip clip = hurtAudioClips[Random.Range(0, hurtAudioClips.Length)];
                if (clip != null)
                {
                    audioSource.pitch = Random.Range(0.9f, 1.15f);
                    audioSource.PlayOneShot(clip);
                }
            }

            // 6. Camera Shake: isolated to the local player being damaged (Rule 3)
            if (isLocalPlayer || (!NetworkClient.active && !NetworkServer.active))
            {
                CameraManager.Instance?.ShakePlayerDamaged();
            }
        }

        private void TriggerDamageFlash()
        {
            if (_flashTween.isAlive) _flashTween.Stop();

            // Set flash color on all renderers
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                if (flashRenderers[i] == null) continue;
                flashRenderers[i].GetPropertyBlock(_propBlock);
                _propBlock.SetColor(BaseColorHash, flashColor);
                flashRenderers[i].SetPropertyBlock(_propBlock);
            }

            // Smoothly clear back to default
            _flashTween = Tween.Delay(flashDuration, () =>
            {
                for (int i = 0; i < flashRenderers.Length; i++)
                {
                    if (flashRenderers[i] == null) continue;
                    flashRenderers[i].SetPropertyBlock(null);
                }
            });
        }

        private void HandleDeath(Vector3 deathDirection)
        {
            // 1. Animator Death State
            if (_animator != null)
            {
                if (_hasIsDeadBool)
                {
                    _animator.SetBool(_isDeadBoolHash, true);
                }
                if (_hasDeathTrigger)
                {
                    _animator.SetTrigger(_deathTriggerHash);
                }
            }

            // 2. Disable Locomotion & Collision
            if (_locomotionController != null) _locomotionController.enabled = false;
            if (_choppingController != null) _choppingController.enabled = false;
            if (_characterController != null) _characterController.enabled = false;

            // 3. Audio Death Feedback
            if (audioSource != null && deathAudioClip != null)
            {
                audioSource.pitch = 1.0f;
                audioSource.PlayOneShot(deathAudioClip);
            }

            Debug.Log($"<color=#ff5555><b>[Woodsmen]</b> Character '{gameObject.name}' has died.</color>");
        }

        #endregion

        #region Healing & Revive API (Server Authoritative & Offline Safe)

        /// <summary>
        /// Restores health up to maxHealth.
        /// </summary>
        public void Heal(float amount)
        {
            if (_isDead) return;

            if (isServer)
            {
                _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
            }
            else if (!NetworkClient.active && !NetworkServer.active)
            {
                _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
                OnHealthChanged?.Invoke(_currentHealth, maxHealth);
            }
        }

        /// <summary>
        /// Revives the character from death with a percentage of max health.
        /// </summary>
        public void Revive(float healthPercent = 1.0f)
        {
            if (isServer)
            {
                _isDead = false;
                _currentHealth = Mathf.Clamp(maxHealth * healthPercent, 1f, maxHealth);
                RpcOnRevived();
            }
            else if (!NetworkClient.active && !NetworkServer.active)
            {
                _isDead = false;
                _currentHealth = Mathf.Clamp(maxHealth * healthPercent, 1f, maxHealth);
                HandleRevive();
                OnHealthChanged?.Invoke(_currentHealth, maxHealth);
                OnRevived?.Invoke();
            }
        }

        [ClientRpc]
        private void RpcOnRevived()
        {
            HandleRevive();
            OnRevived?.Invoke();
        }

        private void HandleRevive()
        {
            if (_animator != null && _hasIsDeadBool)
            {
                _animator.SetBool(_isDeadBoolHash, false);
            }

            if (_characterController != null) _characterController.enabled = true;
            if (_locomotionController != null) _locomotionController.enabled = true;
            if (_choppingController != null) _choppingController.enabled = true;

            Debug.Log($"<color=#50fa7b><b>[Woodsmen]</b> Character '{gameObject.name}' has been revived.</color>");
        }

        #endregion
    }
}
