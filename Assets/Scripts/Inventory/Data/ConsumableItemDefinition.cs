using UnityEngine;
using Woodsmen.Combat;

namespace Woodsmen.Inventory
{
    /// <summary>
    /// Specialized consumable item definition (potions, food, bandages).
    /// Polymorphically interacts with CharacterHealth when used.
    ///
    /// Kept in its own file so Unity can assign a stable, single GUID to this
    /// ScriptableObject type. Having multiple classes in one file causes the
    /// "Missing Script" problem in .asset files because Unity's fileID lookup
    /// only guarantees a clean reference for the PRIMARY class per file.
    /// </summary>
    [CreateAssetMenu(fileName = "NewConsumable", menuName = "Woodsmen/Inventory/Consumable Item", order = 11)]
    public class ConsumableItemDefinition : ItemDefinition
    {
        [Header("Consumable Effects")]
        [Tooltip("Amount of Health restored on use.")]
        [Min(1)]
        [SerializeField] private float healthRestore = 50f;

        public float HealthRestore => healthRestore;
        public override bool IsUsable => true;

        public override bool CanUse(GameObject user)
        {
            if (user == null) return false;
            if (!IsClassAllowed(user)) return false;

            // Only usable if player is injured
            if (user.TryGetComponent(out CharacterHealth health))
            {
                return !health.IsDead && health.CurrentHealth < health.MaxHealth;
            }

            return false;
        }

        public override bool Use(GameObject user)
        {
            if (!CanUse(user)) return false;

            if (user.TryGetComponent(out CharacterHealth health))
            {
                health.Heal(healthRestore);
                Debug.Log($"<color=#10b981><b>[Item]</b> Consumed {DisplayName}, restored {healthRestore} HP.</color>");
                return true;
            }

            return false;
        }

        public static ConsumableItemDefinition CreateRuntimeConsumable(
            string id,
            string displayName,
            float healthRestore,
            int maxStackSize = 20,
            Sprite icon = null,
            CharacterClass allowedClass = CharacterClass.Both)
        {
            var def = CreateInstance<ConsumableItemDefinition>();
            def.InitializeItem(id, displayName, ItemCategory.Consumable, maxStackSize, true, icon, allowedClass);
            def.healthRestore = healthRestore;
            return def;
        }
    }
}
