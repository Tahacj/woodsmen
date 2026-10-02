using UnityEngine;

namespace Woodsmen.Combat.Weapons
{
    /// <summary>
    /// Common interface for all player-equippable weapons and tools.
    /// Provides modular hooks for melee combos, ranged projectile weapons, channelers, and tools.
    /// </summary>
    public interface IWeapon
    {
        /// <summary>
        /// Unique identifier for this weapon type (e.g. "crossbow", "sword", "axe").
        /// </summary>
        string WeaponId { get; }

        /// <summary>
        /// User-friendly display name.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Whether this weapon is currently executing an attack or animation.
        /// </summary>
        bool IsAttackActive { get; }

        /// <summary>
        /// Multiplier applied to locomotion speed while attacking (e.g. 0.5f = half speed).
        /// </summary>
        float AttackMovementMultiplier { get; }

        /// <summary>
        /// Called when this weapon is equipped onto a character.
        /// </summary>
        void OnEquip(GameObject wielder, Transform weaponSocket);

        /// <summary>
        /// Called when this weapon is unequipped.
        /// </summary>
        void OnUnequip();

        /// <summary>
        /// Called when the primary attack input is first pressed.
        /// </summary>
        void OnAttackStart();

        /// <summary>
        /// Called every frame while the primary attack input is held down.
        /// </summary>
        void OnAttackHold();

        /// <summary>
        /// Called when the primary attack input is released.
        /// </summary>
        void OnAttackRelease();

        /// <summary>
        /// Called every frame from the wielder's Update loop.
        /// </summary>
        void TickWeapon(float deltaTime);
    }
}
