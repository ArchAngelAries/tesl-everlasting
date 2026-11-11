using System;
using SPTESL;

namespace OfflineEngine
{
    public static class NativeKeywordRuleTests
    {
        public static void AssertMetadataGate(GameConfiguration config)
        {
            Card card = NativeKeywordRules.Validate(config);
            object attributes = card.EntityAttributes, mechanics = card.MechanicsEntries;
            string description = card.Description;
            object triggers = card.TriggeredEffects, auras = card.Auras, abilities = card.ActionPlayInformation;
            try
            {
                card.Description = "Summon: additional behavior requiring recovery.";
                bool rejected = false;
                try { NativeKeywordRules.Restore(config); }
                catch (NotSupportedException) { rejected = true; }
                Require(rejected && Object.ReferenceEquals(triggers, card.TriggeredEffects)
                    && Object.ReferenceEquals(auras, card.Auras) && Object.ReferenceEquals(abilities, card.ActionPlayInformation),
                    "Unexpected card behavior was accepted or mutated wrappers before metadata validation.");
            }
            finally { card.Description = description; }
            Require(Object.ReferenceEquals(attributes, card.EntityAttributes) && Object.ReferenceEquals(mechanics, card.MechanicsEntries)
                && RuleImporter.IsSupported(NativeKeywordRules.LurkingCrocodileId), "Original Crocodile native metadata/support registration was not retained.");
        }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
