using UnityEngine;

namespace Woodsmen.Combat
{
    /// <summary>
    /// Universal contract for all entities that can receive damage (Characters, Trees, Monsters, Destructibles).
    /// Standardizes combat interactions, hit detection, and damage routing across the project.
    /// </summary>
    public interface IDamageable
    {
        /// <summary>
        /// Applies damage to this entity.
        /// </summary>
        /// <param name="damage">Amount of damage to inflict.</param>
        /// <param name="hitPoint">World position where impact occurred.</param>
        /// <param name="hitDirection">Direction from which the attack came.</param>
        /// <param name="attacker">GameObject that initiated the attack (can be null).</param>
        void TakeDamage(float damage, Vector3 hitPoint = default, Vector3 hitDirection = default, GameObject attacker = null);

        /// <summary>
        /// Current remaining health points.
        /// </summary>
        float CurrentHealth { get; }

        /// <summary>
        /// Maximum health capacity.
        /// </summary>
        float MaxHealth { get; }

        /// <summary>
        /// Normalized health from 0.0 (dead) to 1.0 (full health).
        /// </summary>
        float HealthNormalized { get; }

        /// <summary>
        /// True if the entity is dead/destroyed.
        /// </summary>
        bool IsDead { get; }
    }
}
