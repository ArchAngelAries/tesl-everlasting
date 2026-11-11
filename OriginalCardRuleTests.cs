using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    public static class OriginalCardRuleTests
    {
        public static void AssertOriginalInventory(GameConfiguration config)
        {
            List<Card> cards = OriginalCardRules.Validate(config);
            Require(cards.Count == 220 && OriginalCardRules.Count == 220, "Exact installed no-effect inventory differs.");
            Card sentinel = cards[cards.Count - 1];
            Card first = cards[0];
            object firstTriggers = first.TriggeredEffects, sentinelTriggers = sentinel.TriggeredEffects;
            object attributes = sentinel.EntityAttributes, mechanics = sentinel.MechanicsEntries;
            string description = sentinel.Description;
            try
            {
                sentinel.Description = "Summon: additional authored behavior.";
                AssertRejected(config);
                Require(Object.ReferenceEquals(firstTriggers, first.TriggeredEffects)
                    && Object.ReferenceEquals(sentinelTriggers, sentinel.TriggeredEffects),
                    "Bulk original recovery mutated wrappers before validating the last card.");
            }
            finally { sentinel.Description = description; }
            IntAttribute attack = sentinel.EntityAttributes.Find(delegate(SPTESL.Attribute a) { return a.Name == AttributeType.Attack; }) as IntAttribute;
            int oldAttack = attack.Value;
            try { attack.Value++; AssertRejected(config); }
            finally { attack.Value = oldAttack; }
            Require(Object.ReferenceEquals(attributes, sentinel.EntityAttributes)
                && Object.ReferenceEquals(mechanics, sentinel.MechanicsEntries), "Original statistics/mechanic references were replaced.");
            Require(!OriginalCardRules.Contains(157797006) && !OriginalCardRules.Contains(855598976),
                "Authored Alit summon damage or Baliwog double summon was mistaken for an empty rule card.");
            foreach (Card card in OriginalCardRules.Validate(config))
            {
                Require(card.TriggeredEffects != null && card.Auras != null && card.ActionPlayInformation != null
                    && card.PlayRequirements != null && card.BetrayRequirements != null && card.ActivatedAbility != null,
                    "Original rule wrappers were not restored for " + card.Name + ".");
            }
        }

        private static void AssertRejected(GameConfiguration config)
        {
            bool rejected = false;
            try { OriginalCardRules.Restore(config); }
            catch (NotSupportedException) { rejected = true; }
            Require(rejected, "Changed original metadata was admitted without a rule audit.");
        }
        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
