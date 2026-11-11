using System;
using System.Text;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Owner report 2026-11-10: a Charge creature sometimes plays its attack on the opponent twice; the second attack does
    // no damage. The client lets a creature attack while the server's command list offers it an Attack (and floats it), so
    // after one attack a Charge creature must have no Attack command and must not float.
    public static class ChargeAttackTests
    {
        private const int Crab = 620729069;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("charge-creature-attacks-once-then-no-attack-offered", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Crab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex, enemy = 1 - me;
                    ServerCard card = null;
                    foreach (string name in new[] { "Fearless Northlander", "Torval Crook", "Dunmer Tyro", "Rift Thane", "Charging Bull", "Nord Firebrand" })
                    {
                        if (card != null) break;
                        foreach (CardSet set in config.CardCollection)
                            foreach (Card c in set.Cards)
                                if (card == null && c != null && c.Name == name && c.Definition == "Creature")
                                {
                                    ServerCard made = f.Create(c.ResourceId.Id, me, GameZoneName.Hand);
                                    if (made.QueryAttribute(AttributeType.Charge).Value > 0) card = made;
                                }
                    }
                    if (card == null) throw new InvalidOperationException("no Charge creature found among the candidates");
                    f.Mana(me, 30); f.Refresh(me);
                    f.PlayCreatureLane(card, 0, me);
                    f.Refresh(me);
                    if ((Mask(f, card) & (int)CardCommand.Attack) == 0) throw new InvalidOperationException("Charge creature cannot attack the turn it is played: " + Describe(f, card));
                    ServerCard avatar = f.Avatar(enemy);
                    int health = avatar.CurrentHealth;
                    f.Attack(card, avatar, me);
                    if (avatar.CurrentHealth >= health) throw new InvalidOperationException("The attack did no damage.");
                    f.Refresh(me);
                    if ((Mask(f, card) & (int)CardCommand.Attack) != 0) throw new InvalidOperationException("A second attack is offered after the Charge attack: " + Describe(f, card));
                    // what the client is sent (LifecycleRestoration.Send): the native list, kept to creatures that can attack now
                    FloatingCards floating = LifecycleRestoration.ReadyToAttack(f.Sim, f.Sim.CardCatalog.GenerateFloatingCardList(f.Sim.Players[me], f.Sim.Players[enemy]));
                    if (floating.CardNetUids.Contains(card.NetUid.Id)) throw new InvalidOperationException("The Charge creature still floats (ready to attack) after attacking: " + Describe(f, card));
                    FloatingCards fromCommands = f.Sim.CardCatalog.GenerateFloatingCardList(f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList);
                    if (fromCommands.CardNetUids.Contains(card.NetUid.Id)) throw new InvalidOperationException("The command list still floats the Charge creature: " + Describe(f, card));
                }
            });
        }

        private static int Mask(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard card)
        {
            foreach (CardCommandState s in f.Sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ValidCommands)
                if (s.CardNetUid == card.NetUid.Id) return s.ValidCardCommandMask;
            return 0;
        }

        private static string Describe(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard card)
        {
            StringBuilder b = new StringBuilder();
            b.Append(card.CardId).Append(" zone ").Append(card.CurrentGameZoneName).Append(" mask ").Append(Mask(f, card)).Append(" actions ").Append(card.HasConsumedAllActionsForThisTurn())
                .Append(" charge ").Append(card.QueryAttribute(AttributeType.Charge).Value);
            return b.ToString();
        }
    }
}
