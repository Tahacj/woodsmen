using System;
using UnityEngine;

namespace Woodsmen
{
    /// <summary>
    /// Playable character classes in Woodsmen.
    /// Used for role specialization, equipment compatibility, and item usability.
    /// </summary>
    public enum CharacterClass
    {
        /// <summary>Universal items usable by all classes (Lumberjack and Warrior).</summary>
        Both = 0,

        /// <summary>Items exclusive to the Lumberjack.</summary>
        Lumberjack = 1,

        /// <summary>Items exclusive to the Warrior.</summary>
        Warrior = 2
    }

    /// <summary>
    /// Extension methods for character class evaluation, compatibility checks, and UI formatting.
    /// </summary>
    public static class CharacterClassExtensions
    {
        /// <summary>
        /// Resolves the character class of a GameObject using direct providers, component checks, or name matching.
        /// </summary>
        public static CharacterClass GetCharacterClass(this GameObject gameObject)
        {
            if (gameObject == null) return CharacterClass.Both;

            // 1. Component check (LumberjackChopping)
            if (gameObject.TryGetComponent(out Players.LumberjackChopping _))
            {
                return CharacterClass.Lumberjack;
            }

            // 3. Name-based matching for prefabs and spawned instances
            string name = gameObject.name;
            if (name.IndexOf("Warrior", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CharacterClass.Warrior;
            }
            if (name.IndexOf("Lumberjack", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return CharacterClass.Lumberjack;
            }

            return CharacterClass.Both;
        }

        /// <summary>
        /// Returns true if the allowed class permits the specified character class to use it.
        /// Both allows everyone; specific classes allow only that class.
        /// </summary>
        public static bool IsClassCompatible(this CharacterClass allowed, CharacterClass characterClass)
        {
            if (allowed == CharacterClass.Both || characterClass == CharacterClass.Both) return true;
            return allowed == characterClass;
        }

        /// <summary>
        /// Human-friendly display label for UI badges.
        /// </summary>
        public static string GetClassDisplayName(this CharacterClass characterClass)
        {
            return characterClass switch
            {
                CharacterClass.Warrior => "Warrior Only",
                CharacterClass.Lumberjack => "Lumberjack Only",
                _ => "All Classes"
            };
        }
    }

    /// <summary>
    /// Interface for components that explicitly declare a character class.
    /// </summary>
    public interface ICharacterClassProvider
    {
        CharacterClass CharacterClass { get; }
    }
}
