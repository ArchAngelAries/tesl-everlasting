using System;
using SPTESL;

namespace OfflineEngine
{
    public static class IceSpikeTests
    {
        private const int IceSpike = 1690856288, Mudcrab = 620729069;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("ice-spike-spec-paid-damage-and-draw", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Require(OriginalSpecRules.HasRestoredRules(IceSpike), "Ice Spike spec was not emitted: " + OriginalSpecRules.SkipReason(IceSpike));
                    Card card = config.FindCardById(IceSpike);
                    Require(card.ActionPlayInformation.Count == 1 && card.ActionPlayInformation[0].EffectGroup.Effects.Count == 2
                        && card.ActionPlayInformation[0].EffectGroup.Effects[0].PlayerActions[0].Action is PlayerActions.DamageAvatarOfPlayer
                        && card.ActionPlayInformation[0].EffectGroup.Effects[1].PlayerActions[0].Action is PlayerActions.DrawCards,
                        "Ice Spike must apply damage before drawing.");
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard source = f.Create(IceSpike, me, GameZoneName.Hand);
                    int hand = f.HandCount(me), deck = f.DeckCount(me), enemyDeck = f.DeckCount(enemy);
                    f.Mana(me, 12); f.Refresh(me);
                    f.Play(source, null, me);
                    Require(f.Avatar(enemy).CurrentHealth == 28 && f.Avatar(me).CurrentHealth == 30
                        && f.DeckCount(me) == deck - 1 && f.DeckCount(enemy) == enemyDeck
                        && f.HandCount(me) == hand && f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 10
                        && source.CurrentGameZoneName == GameZoneName.Cemetery && source.QueryGameJournalCount(CardJournal.Played) == 1,
                        "Ice Spike did not resolve exactly one paid play, two opponent damage and one friendly draw.");
                }
            });
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
