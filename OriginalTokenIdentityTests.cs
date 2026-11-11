using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Exact installed identities through paid decisions, not direct effect execution.
    public static class OriginalTokenIdentityTests
    {
        private const int Mudcrab = 620729069, BanditAmbush = 195135320, CruelBandit = 906449635,
            Scamp = 175622366, NordFirebrand = 195169233, StrategicDeployment = -374586671, ImperialRecruit = -2084284159,
            CastOut = 780292596;

        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            HandCase(config, test, "bedlam", -1038531871, Scamp, 2, 1, 1, AttributeType.None);
            HandCase(config, test, "knight-of-gnisis", 1345209521, 1403480832, 1, 2, 2, AttributeType.Taunt);
            HandCase(config, test, "marked-man", 1439548280, 741809568, 1, 0, 2, AttributeType.Taunt);
            HandCase(config, test, "raiding-party-exclusive", -2144786523, NordFirebrand, 2, 1, 1, AttributeType.Charge);
            HandCase(config, test, "raiding-party-normal", -140181666, NordFirebrand, 2, 1, 1, AttributeType.Charge);
            GoldenCases(config, test, "normal", 1480705345);
            GoldenCases(config, test, "exclusive", -1596715503);
            DeploymentCase(config, test, "empty", 0, 0);
            DeploymentCase(config, test, "near-full-both", 3, 3);
            DeploymentCase(config, test, "full-left-no-spill", 4, 2);

            test("token-bandit-ambush-two-per-lane-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    PlayPaid(f, BanditAmbush, me);
                    List<ServerCard> tokens = Cards(f, me, GameZoneName.CreatureBoard, CruelBandit);
                    Require(tokens.Count == 4 && f.LaneCount(me, 0) == 2 && f.LaneCount(me, 1) == 2,
                        "Bandit Ambush must create four Cruel Bandits, exactly two in each lane.");
                    foreach (ServerCard token in tokens) CheckToken(f, token, me, CruelBandit, 1, 1, AttributeType.None);
                    Require(f.BoardCount(1 - me) == 0, "Bandit Ambush created an enemy creature.");
                }
            });

            test("token-bandit-ambush-near-full-lane-no-spill-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    for (int n = 0; n < 3; n++) f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    PlayPaid(f, BanditAmbush, me);
                    List<ServerCard> tokens = Cards(f, me, GameZoneName.CreatureBoard, CruelBandit);
                    int left = 0, right = 0;
                    foreach (ServerCard token in tokens)
                    {
                        CheckToken(f, token, me, CruelBandit, 1, 1, AttributeType.None);
                        if (token.CurrentLaneIndex == 0) left++; else if (token.CurrentLaneIndex == 1) right++;
                    }
                    Require(tokens.Count == 3 && left == 1 && right == 2 && f.LaneCount(me, 0) == 4 && f.LaneCount(me, 1) == 2,
                        "A nearly full left lane must accept one Bandit; its missing second Bandit must not spill into the right lane.");
                    Require(Cards(f, me, GameZoneName.CreatureBoard, Mudcrab).Count == 3 && f.BoardCount(1 - me) == 0,
                        "Bandit Ambush disturbed existing creatures or created an enemy creature.");
                }
            });

            test("token-invasion-party-created-branch-invades-before-scamps-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    PlayPaid(f, -705626882, me);
                    List<ServerCard> gates = Cards(f, me, GameZoneName.CreatureBoard, OriginalOblivionGateRules.Gate);
                    Require(gates.Count == 1, "Invasion Party must create exactly one friendly Oblivion Gate.");
                    ServerCard gate = gates[0];
                    Require(gate.OwnerNetId == f.Sim.Players[me].NetId && gate.CurrentLaneIndex == 1
                        && gate.QueryAttribute(AttributeType.OblivionGateLevel).Value == 1
                        && gate.QueryAttribute(AttributeType.Attack).Value == 0 && gate.CurrentHealth == 4,
                        "Invasion Party did not open a level 1 0/4 Gate in the right lane.");
                    List<ServerCard> tokens = Cards(f, me, GameZoneName.CreatureBoard, Scamp);
                    Require(tokens.Count == 2 && f.BoardCount(me) == 3 && f.BoardCount(1 - me) == 0,
                        "Invasion Party must summon exactly two friendly Scamps alongside its Gate.");
                    // The Gate's DaedraTrigger grants +0/+1 on entry (not a continuous stats aura).
                    // Both exact Scamps must receive it, exercising Invade-before-summon ordering.
                    foreach (ServerCard token in tokens)
                    {
                        CheckToken(f, token, me, Scamp, 1, 2, AttributeType.None);
                        Require(token.CurrentLaneIndex == 0 || token.CurrentLaneIndex == 1, "Scamp is not in a creature lane.");
                    }
                }
            });
        }

        private static void HandCase(GameConfiguration config, Action<string, Action> test, string label,
            int sourceId, int tokenId, int count, int attack, int health, AttributeType keyword)
        {
            test("token-" + label + "-exact-hand-identities-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    int handBefore = f.HandCount(me), enemyHandBefore = f.HandCount(1 - me);
                    PlayPaid(f, sourceId, me);
                    List<ServerCard> tokens = Cards(f, me, GameZoneName.Hand, tokenId);
                    Require(tokens.Count == count && f.HandCount(me) == handBefore + count,
                        label + " produced the wrong hand count or token identity.");
                    foreach (ServerCard token in tokens) CheckToken(f, token, me, tokenId, attack, health, keyword);
                    Require(f.HandCount(1 - me) == enemyHandBefore && Cards(f, 1 - me, GameZoneName.Hand, tokenId).Count == 0,
                        label + " created cards for the opponent.");
                    Require(Cards(f, me, GameZoneName.CreatureBoard, tokenId).Count == 0,
                        label + " summoned its hand tokens onto the board.");
                }
            });
        }

        private static void GoldenCases(GameConfiguration config, Action<string, Action> test, string printing, int id)
        {
            for (int lane = 0; lane < 2; lane++)
            {
                int chosenLane = lane;
                test("token-golden-saint-" + printing + "-lane-" + lane + "-health-and-nonrecursion-paid-lifecycle", delegate {
                    // Below, equal and above: the comparison must be strict, and the successful Summon creates one card.
                    for (int healthDifference = -1; healthDifference <= 1; healthDifference++)
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                        int damaged = healthDifference < 0 ? me : 1 - me;
                        if (healthDifference != 0)
                            f.Event(delegate { f.Avatar(damaged).ModifyCurrentHealth(-1, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                        ServerCard source = PlayPaid(f, id, me, chosenLane == 0 ? BoardLaneTarget.MyLeftLane : BoardLaneTarget.MyRightLane);
                        List<ServerCard> saints = Cards(f, me, GameZoneName.CreatureBoard, id);
                        int expected = healthDifference > 0 ? 2 : 1;
                        Require(saints.Count == expected && f.BoardCount(me) == expected && f.BoardCount(1 - me) == 0,
                            "Golden Saint health comparison or native non-reentrant Summon failed: " + healthDifference + " produced " + saints.Count);
                        Require(source.CurrentLaneIndex == chosenLane && f.LaneCount(me, chosenLane) == 1
                            && f.LaneCount(me, 1 - chosenLane) == expected - 1, "Golden Saint did not use exactly the other lane.");
                        foreach (ServerCard saint in saints)
                        {
                            CheckToken(f, saint, me, id, 4, 4, AttributeType.Taunt);
                            Require(saint.TriggeredEffects.Exists(delegate(TriggeredEffect t) { return t.TriggerType == EffectTriggerType.Summon && !t.AllowReentrancy; }),
                                "Generated Golden Saint lost its native non-reentrant Summon ability.");
                        }
                    }
                });
                test("token-golden-saint-" + printing + "-lane-" + lane + "-full-other-lane-paid-lifecycle", delegate {
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                        f.Event(delegate { f.Avatar(1 - me).ModifyCurrentHealth(-1, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                        for (int n = 0; n < 4; n++) f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 1 - chosenLane);
                        PlayPaid(f, id, me, chosenLane == 0 ? BoardLaneTarget.MyLeftLane : BoardLaneTarget.MyRightLane);
                        Require(Cards(f, me, GameZoneName.CreatureBoard, id).Count == 1 && f.BoardCount(me) == 5
                            && f.LaneCount(me, chosenLane) == 1 && f.LaneCount(me, 1 - chosenLane) == 4,
                            "Golden Saint must not replace a creature or redirect its summon when the other lane is full.");
                        Require(f.BoardCount(1 - me) == 0, "Golden Saint created an enemy creature.");
                    }
                });
            }
            test("token-golden-saint-" + printing + "-generated-card-unsummon-replay-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    f.Event(delegate { f.Avatar(1 - me).ModifyCurrentHealth(-1, null, DecisionRestoration.State(f.Sim, f.Sim._turnStateStack.Peek()), true); });
                    ServerCard first = PlayPaid(f, id, me, BoardLaneTarget.MyLeftLane);
                    List<ServerCard> saints = Cards(f, me, GameZoneName.CreatureBoard, id);
                    Require(saints.Count == 2, "Initial Golden Saint recursed before replay test.");
                    ServerCard generated = saints.Find(delegate(ServerCard c) { return c != first; });
                    PlayExistingPaid(f, f.Create(CastOut, me, GameZoneName.Hand), me, BoardLaneTarget.None, generated);
                    Require(generated.CurrentGameZoneName == GameZoneName.Hand && Cards(f, me, GameZoneName.CreatureBoard, id).Count == 1,
                        "Cast Out did not return the generated Golden Saint to hand.");
                    PlayExistingPaid(f, generated, me, BoardLaneTarget.MyRightLane, null);
                    Require(Cards(f, me, GameZoneName.CreatureBoard, id).Count == 3 && f.LaneCount(me, 0) == 2 && f.LaneCount(me, 1) == 1,
                        "Replaying the generated Golden Saint must summon exactly one more Saint; its ability must remain usable.");
                }
            });
        }

        private static void DeploymentCase(GameConfiguration config, Action<string, Action> test, string label, int leftBefore, int rightBefore)
        {
            test("token-strategic-deployment-" + label + "-paid-lifecycle", delegate {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    int me = f.Sim._turnStateStack.Peek().ActivePlayerIndex;
                    for (int n = 0; n < leftBefore; n++) f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    for (int n = 0; n < rightBefore; n++) f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 1);
                    int handBefore = f.HandCount(me), enemyHandBefore = f.HandCount(1 - me);
                    PlayPaid(f, StrategicDeployment, me);
                    int left = Math.Min(2, 4 - leftBefore), right = Math.Min(2, 4 - rightBefore);
                    List<ServerCard> recruits = Cards(f, me, GameZoneName.CreatureBoard, ImperialRecruit);
                    Require(recruits.Count == left + right && f.LaneCount(me, 0) == leftBefore + left && f.LaneCount(me, 1) == rightBefore + right
                        && f.BoardCount(me) == leftBefore + rightBefore + left + right, "Strategic Deployment counts, lane capacity or exact Recruit identity differ.");
                    foreach (ServerCard recruit in recruits)
                    {
                        CheckToken(f, recruit, me, ImperialRecruit, 1, 1, AttributeType.None);
                        Require(recruit.HasSubtype(CardSubtype.Imperial) && recruit.HasSubtype(CardSubtype.Recruit), "Strategic Deployment must create Imperial Recruits.");
                    }
                    Require(Cards(f, me, GameZoneName.CreatureBoard, Mudcrab).Count == leftBefore + rightBefore
                        && f.BoardCount(1 - me) == 0 && f.HandCount(me) == handBefore && f.HandCount(1 - me) == enemyHandBefore,
                        "Strategic Deployment disturbed existing creatures, hand counts or opponent ownership.");
                }
            });
        }

        private static ServerCard PlayPaid(OriginalTargetedSummonTests.LifecycleFixture f, int id, int me)
        { return PlayPaid(f, id, me, BoardLaneTarget.None); }

        private static ServerCard PlayPaid(OriginalTargetedSummonTests.LifecycleFixture f, int id, int me, BoardLaneTarget lane)
        {
            Require(OriginalSpecRules.HasRestoredRules(id), "Token source was not emitted: " + id + " " + OriginalSpecRules.SkipReason(id));
            ServerCard source = f.Create(id, me, GameZoneName.Hand);
            PlayExistingPaid(f, source, me, lane, null);
            return source;
        }

        private static void PlayExistingPaid(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard source, int me, BoardLaneTarget lane, ServerCard target)
        {
            f.Mana(me, 30); f.Refresh(me);
            int cost = source.QueryAttribute(AttributeType.ManaCost).Value;
            int playedBefore = source.QueryGameJournalCount(CardJournal.Played);
            if (lane == BoardLaneTarget.None) f.Play(source, target, me); else f.PlayCreatureLane(source, lane == BoardLaneTarget.MyLeftLane ? 0 : 1, me);
            Require(f.Sim.Players[me].QueryAttribute(AttributeType.Mana).Value == 30 - cost
                && source.QueryGameJournalCount(CardJournal.Played) == playedBefore + 1 && source.CurrentGameZoneName != GameZoneName.Hand,
                "Token source did not resolve through one paid play: " + source.CardId + " cost " + cost);
        }

        private static List<ServerCard> Cards(OriginalTargetedSummonTests.LifecycleFixture f, int owner, GameZoneName zone, int id)
        { return f.Sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return c.CardId == id; }, f.Sim.Players[owner].NetId, zone); }

        private static void CheckToken(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard card, int owner,
            int id, int attack, int health, AttributeType keyword)
        {
            Require(card.CardId == id && card.OwnerNetId == f.Sim.Players[owner].NetId
                && card.QueryAttribute(AttributeType.Attack).Value == attack && card.QueryAttribute(AttributeType.MaxHealth).Value == health,
                "Wrong token identity, owner or stats for " + id + ": " + card.CardId + " "
                + card.QueryAttribute(AttributeType.Attack).Value + "/" + card.QueryAttribute(AttributeType.MaxHealth).Value);
            if (keyword != AttributeType.None) Require(card.QueryAttribute(keyword).Value > 0, "Token " + id + " lost " + keyword);
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
