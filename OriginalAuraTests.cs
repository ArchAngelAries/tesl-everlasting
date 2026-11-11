using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalAuraTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069, Fervor = -1653188426, Fervor2 = -1708131813, Captain = 899518525, Doomcrag = -1945422996, Conviction = -1264406243;

        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("original-aura-7-exact-atomic-metadata-owned-graph", delegate {
                List<Card> cards = OriginalAuraRules.Validate(config);
                Require(cards.Count == 9, "Original aura inventory differs.");
                object before = cards[0].Auras; string text = cards[8].Description;
                try {
                    cards[8].Description += " Extra effect."; bool rejected = false;
                    try { OriginalAuraRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected && Object.ReferenceEquals(before, cards[0].Auras), "Aura atomic preflight failed.");
                } finally { cards[8].Description = text; }
                OriginalAuraRules.Restore(config); Require(Object.ReferenceEquals(before, cards[0].Auras), "Aura graph identity changed.");
                EnchantmentAttributeModification m = config.FindCardById(Fervor).Auras[0].EnchantmentsToApply[0].Enchantment.AttributeModifications[0];
                try {
                    m.Value = 5; bool rejected = false;
                    try { OriginalAuraRules.Restore(config); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Changed aura value admitted.");
                } finally { m.Value = 1; }
                foreach (Card card in cards) Require(RuleImporter.IsSupported(card.ResourceId.Id), "Aura ID not supported: " + card.ResourceId.Id);
            });

            test("original-aura-native-apply-stack-remove-silence-lane", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard a = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.One), b = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, f.One);
                ServerCard enemy = f.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, f.Two);
                ServerCard fervor = f.CreateId(Fervor, GameZoneName.SupportBoard, 0, f.One);
                OfflineAuraRestoration.Apply(f.State);
                Require(Stats(a) == "9/9" && Stats(b) == "9/9" && Stats(enemy) == "8/8", "Divine Fervor aura differs: " + Stats(a) + " " + Stats(enemy));
                OfflineAuraRestoration.Apply(f.State);
                Require(Stats(a) == "9/9" && a.AuraEnchantments.Count == 1, "Aura recompute is not idempotent.");
                ServerCard fervor2 = f.CreateId(Fervor2, GameZoneName.SupportBoard, 0, f.One);
                ServerCard fervor3 = f.CreateId(Fervor, GameZoneName.SupportBoard, 0, f.One);
                OfflineAuraRestoration.Apply(f.State);
                Require(Stats(a) == "11/11" && a.AuraEnchantments.Count == 3, "Separate aura sources did not stack: " + Stats(a));
                ServerCardCatalog.MoveToCemetery(f.State, fervor3, ServerCardCatalog.MoveOperationType.Untyped);
                ServerCardCatalog.MoveToCemetery(f.State, fervor2, ServerCardCatalog.MoveOperationType.Untyped);
                ServerCardCatalog.MoveToCemetery(f.State, fervor, ServerCardCatalog.MoveOperationType.Untyped);
                OfflineAuraRestoration.Apply(f.State);
                Require(Stats(a) == "8/8" && Stats(b) == "8/8" && a.AuraEnchantments.Count == 0, "Aura not removed when its source left the board: " + Stats(a));

                EngineTests.Fixture g = new EngineTests.Fixture(config);
                ServerCard captain = g.CreateId(Captain, GameZoneName.CreatureBoard, 0, g.One);
                ServerCard near = g.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, g.One), far = g.CreateId(Leviathan, GameZoneName.CreatureBoard, 1, g.One);
                ServerCard foe = g.CreateId(Leviathan, GameZoneName.CreatureBoard, 0, g.Two);
                int captainAttack = captain.QueryAttribute(AttributeType.Attack).Value;
                ServerCard vampire = g.CreateId(Doomcrag, GameZoneName.CreatureBoard, 1, g.One);
                OfflineAuraRestoration.Apply(g.State);
                Require(Stats(near) == "9/8" && Stats(far) == "8/8" && Stats(foe) == "8/8" && captain.QueryAttribute(AttributeType.Attack).Value == captainAttack,
                    "Orc Clan Captain lane aura differs: near=" + Stats(near) + " far=" + Stats(far));
                Require(far.QueryAttribute(AttributeType.Deathtouch).Value > 0 && near.QueryAttribute(AttributeType.Deathtouch).Value == 0,
                    "Doomcrag Vampire Lethal aura differs.");
                CardActions.Silence.StaticExecuteSingleTarget(g.State, null, captain, g.One, -1);
                OfflineAuraRestoration.Apply(g.State);
                Require(Stats(near) == "8/8", "Silenced Orc Clan Captain kept its aura: " + Stats(near));
            });

            test("original-aura-real-paid-play-and-later-summon", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int player = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard conviction = f.Create(Conviction, player, GameZoneName.Hand), crab = f.Create(Mudcrab, player, GameZoneName.Hand);
                    ServerCard ally = f.Create(Leviathan, player, GameZoneName.CreatureBoard), enemy = f.Create(Leviathan, 1 - player, GameZoneName.CreatureBoard);
                    f.Event(delegate { PlayerActions.SetMana.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), s.Players[player], 12); });
                    s._turnStateStack.Peek().CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(s.Players[player], s.Players[1 - player], s.Players);
                    f.Play(conviction, null, player);
                    Require(conviction.CurrentGameZoneName == GameZoneName.SupportBoard && Stats(ally) == "8/9" && Stats(enemy) == "8/8", "Divine Conviction paid play aura differs: " + Stats(ally));
                    f.Play(crab, null, player);
                    Require(Stats(crab) == "2/2", "Later-summoned creature did not receive the aura: " + Stats(crab));
                }
            });
        }
        private static string Stats(ServerCard c) { return c.QueryAttribute(AttributeType.Attack).Value + "/" + c.CurrentHealth; }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
