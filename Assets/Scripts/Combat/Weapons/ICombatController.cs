namespace Woodsmen.Combat.Weapons
{
    /// <summary>
    /// Decoupled interface implemented by player combat controllers (e.g. PlayerCombatController).
    /// Allows LocomotionController and other systems to query attack status without hardcoding specific class names.
    /// </summary>
    public interface ICombatController
    {
        /// <summary>
        /// True if an attack or combat action is currently executing.
        /// </summary>
        bool IsAttackActive { get; }

        /// <summary>
        /// Speed penalty multiplier during attack (e.g. 0.5f = 50% movement speed).
        /// </summary>
        float AttackMovementMultiplier { get; }

        /// <summary>
        /// The currently equipped weapon or tool.
        /// </summary>
        IWeapon CurrentWeapon { get; }
    }
}
