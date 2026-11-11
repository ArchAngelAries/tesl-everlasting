using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Behaviour tests for cards restored on 2026-11-05: lane choice (Trial of Flame), equip-or-throw (Spear of Embers), activated
    // choice (College of Winterhold), and Prophecy plays outside rune breaks (Fate Weaver, Assassin Beetle).
    internal static class OfflineCardBehaviourTests
    {
        private const int Leviathan = 1427457192, Mudcrab = 620729069; // Swamp Leviathan 8/8, Enraged Mudcrab 2/1
        private const int TrialOfFlame = -552512167, ChildOfHircine = -873497107;   // Child of Hircine 6/7, "Slay: May attack again this turn"

        private const int SpearOfEmbers = -434815795, CollegeOfWinterhold = 1891908716;

        private const int FateWeaver = 378495215, AssassinBeetle = -1125414247;

        // Lurking Crocodile: a plain Prophecy creature (also used by the native prophecy tests).
        private static int ProphecyCreature(GameConfiguration config) { return NativeKeywordRules.LurkingCrocodileId; }

        // Play a card, then accept the Prophecy window it opens (the engine's own chooser plays the offered card).
        private static void PlayThenAcceptProphecy(OriginalTargetedSummonTests.LifecycleFixture f, ServerCard card, ServerCard target, int me)
        {
            Simulation s = f.Sim;
            CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
            Decision play = new Decision { Type = CcgRpc.DecisionType.Play, CommandUid = list.ListUid, ActorCardNetUid = card.NetUid.Id,
                TargetCardNetUid = target == null ? CardNetUid.GetInvalid().Id : target.NetUid.Id };
            s.PlayerServiceClients[me]._decisionMessages.Enqueue(play);
            bool offered = false;
            System.Text.StringBuilder seen = new System.Text.StringBuilder();
            for (int tick = 0; tick < 400 && !offered; tick++)
            {
                f.AnswerTargetPrompt();
                TurnState top = s._turnStateStack.Peek();
                if (seen.Length < 600 && top.CurrentCommandItem != null && top.CurrentSequence == null)
                {
                    string shape = "depth " + s._turnStateStack.Count + " cmds " + top.CurrentCommandItem.CommandList.ValidCommands.Count + " masks "
                        + string.Join("/", new List<CardCommandState>(top.CurrentCommandItem.CommandList.ValidCommands).ConvertAll(delegate(CardCommandState c) { return c.ValidCardCommandMask + ":" + c.PlayTargets.Count; }).ToArray())
                        + " prophecy " + s.CardCatalog.FindCardCount(GameZoneName.Prophecy, s.Players[me].NetId) + "; ";
                    if (seen.ToString().IndexOf(shape) < 0) seen.Append(shape);
                }
                if (s._turnStateStack.Count >= 2 && top.CurrentSequence == null && top.CurrentCommandItem != null
                    && s.CardCatalog.FindCardCount(GameZoneName.Prophecy, s.Players[me].NetId) == 1)
                {
                    Decision accept = DecisionRestoration.ChooseDecision(s, top.CurrentCommandItem.CommandList);
                    if (accept.Type != CcgRpc.DecisionType.Play) throw new InvalidOperationException("the chooser declined the Prophecy");
                    s.PlayerServiceClients[top.ActivePlayerIndex]._decisionMessages.Enqueue(accept);
                    offered = true;
                }
                LifecycleRestoration.FixedUpdate(s);
            }
            if (!offered) throw new InvalidOperationException("no Prophecy window opened; saw " + seen + " card at " + card.CurrentGameZoneName);
            f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
        }

        internal static void Run(GameConfiguration config, Action<string, Action> test)
        {
            // Fate Weaver: draw a card; a Prophecy may be played for free.
            test("prophecy-fate-weaver-plays-a-drawn-prophecy-free", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    foreach (ServerCard c in s.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return true; }, s.Players[me].NetId, GameZoneName.Deck))
                        f.Event(delegate { ServerCardCatalog.MoveToCemetery(DecisionRestoration.State(s, s._turnStateStack.Peek()), c, ServerCardCatalog.MoveOperationType.Untyped); });
                    ServerCard prophecy = f.Create(ProphecyCreature(config), me, GameZoneName.Deck);
                    ServerCard weaver = f.Create(FateWeaver, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    int cost = weaver.QueryAttribute(AttributeType.ManaCost).Value;
                    PlayThenAcceptProphecy(f, weaver, null, me);
                    if (prophecy.CurrentGameZoneName != GameZoneName.CreatureBoard) throw new InvalidOperationException("the drawn Prophecy is in " + prophecy.CurrentGameZoneName);
                    int mana = s.Players[me].QueryAttribute(AttributeType.Mana).Value;
                    if (mana != 30 - cost) throw new InvalidOperationException("the Prophecy was not free: mana " + mana + ", expected " + (30 - cost));
                }
            });
            // Assassin Beetle: Summon: play a Prophecy from your hand (the one chosen as its target).
            test("prophecy-assassin-beetle-plays-a-prophecy-from-hand", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard prophecy = f.Create(ProphecyCreature(config), me, GameZoneName.Hand);
                    ServerCard beetle = f.Create(AssassinBeetle, me, GameZoneName.Hand);
                    f.Mana(me, 30); f.Refresh(me);
                    int cost = beetle.QueryAttribute(AttributeType.ManaCost).Value;
                    PlayThenAcceptProphecy(f, beetle, null, me);
                    if (prophecy.CurrentGameZoneName != GameZoneName.CreatureBoard) throw new InvalidOperationException("the hand Prophecy is in " + prophecy.CurrentGameZoneName);
                    int mana = s.Players[me].QueryAttribute(AttributeType.Mana).Value;
                    if (mana != 30 - cost) throw new InvalidOperationException("the Prophecy was not free: mana " + mana + ", expected " + (30 - cost));
                }
            });
            // College of Winterhold: activate, pick one of three random actions, and it is drawn.
            test("activated-choice-college-of-winterhold-draws-the-pick", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                    ServerCard college = f.Create(CollegeOfWinterhold, me, GameZoneName.SupportBoard);
                    f.Mana(me, 30); f.Refresh(me);
                    List<ServerCard> before = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[me].NetId, GameZoneName.Hand);
                    f.Activate(college, null, me);
                    List<ServerCard> after = s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, s.Players[me].NetId, GameZoneName.Hand);
                    List<ServerCard> added = after.FindAll(delegate(ServerCard c) { return !before.Contains(c); });
                    if (added.Count != 1) throw new InvalidOperationException("expected one new card in hand, got " + added.Count);
                    if (!added[0].HasSubtype(CardSubtype.Spell)) throw new InvalidOperationException("the drawn card is not an action: " + added[0].CardId);
                    if (s._turnStateStack.Count != 1) throw new InvalidOperationException("the choice did not finish");
                }
            });
            // Spear of Embers: equip it (+3/+3) or throw it (3 damage). Equipping must not also throw it.
            test("item-choice-spear-of-embers-equip-does-not-throw", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, foe = 1 - me;
                    ServerCard spear = f.Create(SpearOfEmbers, me, GameZoneName.Hand);
                    ServerCard wielder = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0);
                    ServerCard enemy = f.CreateIn(Leviathan, foe, GameZoneName.CreatureBoard, 0);
                    int attack = wielder.QueryAttribute(AttributeType.Attack).Value, enemyHealth = enemy.CurrentHealth;
                    f.Mana(me, 30); f.Refresh(me);
                    f.PlayEquip(spear, wielder, me);
                    if (wielder.QueryAttribute(AttributeType.Attack).Value != attack + 3) throw new InvalidOperationException("wielder power " + wielder.QueryAttribute(AttributeType.Attack).Value + ", expected " + (attack + 3));
                    if (enemy.CurrentHealth != enemyHealth) throw new InvalidOperationException("equipping also damaged an enemy creature");
                }
            });
            test("lane-choice-trial-of-flame-keeps-strongest-each-side", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, foe = 1 - me;
                    ServerCard trial = f.Create(TrialOfFlame, me, GameZoneName.Hand);
                    ServerCard myBig = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 0), mySmall = f.CreateIn(Mudcrab, me, GameZoneName.CreatureBoard, 0);
                    ServerCard foeBig = f.CreateIn(Leviathan, foe, GameZoneName.CreatureBoard, 0), foeSmall = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 0);
                    ServerCard other = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 1);   // the other lane is untouched
                    f.Mana(me, 30);
                    f.Refresh(me);
                    CardCommandState command = OriginalTargetedSummonTests.Command(s._turnStateStack.Peek().CurrentCommandItem.CommandList, trial);
                    if (command == null || command.LaneTargets.Count == 0) throw new InvalidOperationException("Trial of Flame offers no lane");
                    BoardLaneTarget left = command.LaneTargets.Contains(BoardLaneTarget.MyLeftLane) ? BoardLaneTarget.MyLeftLane : command.LaneTargets[0];
                    f.PlayLane(trial, left, me);
                    Func<ServerCard, bool> alive = delegate(ServerCard c) { return c.CurrentGameZoneName == GameZoneName.CreatureBoard; };
                    if (!alive(myBig) || !alive(foeBig)) throw new InvalidOperationException("the strongest creature on a side was destroyed");
                    if (alive(mySmall) || alive(foeSmall)) throw new InvalidOperationException("a weaker creature in the chosen lane survived (" + mySmall.CurrentGameZoneName + ", " + foeSmall.CurrentGameZoneName + ")");
                    if (!alive(other)) throw new InvalidOperationException("a creature in the other lane was destroyed");
                }
            });
            // Slay (owner report 2026-11-05, seen online): Child of Hircine "Slay: May attack again this turn". After killing a
            // creature it attacks again; a creature without it (Swamp Leviathan) does not.
            // Beast Form (owner report 2026-11-06): a broken rune turns werewolves into their beast form, as Reborn does
            // (capture 2026-11-05: Companion Harbinger -1497585773 -> 1629862753, Whiterun Protector -1216391466 -> 347107517).
            foreach (bool enemyRune in new bool[] { true, false })
            {
                bool theirs = enemyRune;
                test("beast-form-werewolves-" + (theirs ? "transform-on-enemy" : "stay-on-own") + "-rune-break", delegate
                {
                    using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                    {
                        Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex;
                        ServerCard harbinger = f.CreateIn(-1497585773, me, GameZoneName.CreatureBoard, 0);
                        ServerCard protector = f.CreateIn(-1216391466, me, GameZoneName.CreatureBoard, 1);
                        int protectorAttack = protector.QueryAttribute(AttributeType.Attack).Value;
                        ServerCard avatar = f.Avatar(theirs ? 1 - me : me);
                        PlayerState owner = s.Players[theirs ? 1 - me : me];
                        f.Event(delegate { PlayerActions.DamageAvatarOfPlayer.StaticExecute(DecisionRestoration.State(s, s._turnStateStack.Peek()), null, owner, avatar.CurrentHealth - 25, -1); });
                        s.CheckForRuneBreaks();   // as after any damage in a turn (the test injects the damage outside a decision)
                        f.Until(delegate { return OriginalTargetedSummonTests.Ready(s); });
                        // Beast Form's trigger requires the broken rune to be an enemy's (TriggerCardRequirements: EventTarget and
                        // Self are Enemies): the owner's own rune leaves them as they are.
                        if (!theirs)
                        {
                            if (harbinger.CardId != -1497585773 || protector.CardId != -1216391466) throw new InvalidOperationException("werewolves transformed on their own rune break");
                            return;
                        }
                        if (harbinger.CardId != 1629862753 || protector.CardId != 347107517)
                            throw new InvalidOperationException("werewolves did not transform: Harbinger " + harbinger.CardId + ", Protector " + protector.CardId
                                + " (attack " + protectorAttack + "->" + protector.QueryAttribute(AttributeType.Attack).Value + "), runes lost "
                                + owner.QueryAttribute(AttributeType.NumRunesLost).Value + ", runes " + owner.QueryAttribute(AttributeType.NumRunes).Value
                                + ", rune health " + owner.QueryAttribute(AttributeType.RuneHealth).Value + ", avatar " + avatar.CurrentHealth);
                    }
                });
            }
            test("slay-child-of-hircine-attacks-again-after-killing", delegate
            {
                using (OriginalTargetedSummonTests.LifecycleFixture f = new OriginalTargetedSummonTests.LifecycleFixture(config, Mudcrab))
                {
                    Simulation s = f.Sim; int me = s._turnStateStack.Peek().ActivePlayerIndex, foe = 1 - me;
                    ServerCard child = f.CreateIn(ChildOfHircine, me, GameZoneName.CreatureBoard, 0);
                    ServerCard big = f.CreateIn(Leviathan, me, GameZoneName.CreatureBoard, 1);
                    f.EndTurn(); f.EndTurn();   // both ready to attack
                    ServerCard first = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 0), second = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 0);
                    ServerCard third = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 1), fourth = f.CreateIn(Mudcrab, foe, GameZoneName.CreatureBoard, 1);
                    f.Refresh(me);
                    f.Attack(child, first, me);
                    if (first.CurrentGameZoneName == GameZoneName.CreatureBoard) throw new InvalidOperationException("Child of Hircine did not kill the first Mudcrab");
                    f.Attack(child, second, me);   // throws "Native attack command is invalid" if Slay gave no second attack
                    if (second.CurrentGameZoneName == GameZoneName.CreatureBoard) throw new InvalidOperationException("the second attack did not resolve");
                    f.Attack(big, third, me);
                    CardCommandList list = s._turnStateStack.Peek().CurrentCommandItem.CommandList;
                    Decision again = new Decision { Type = CcgRpc.DecisionType.Attack, CommandUid = list.ListUid, ActorCardNetUid = big.NetUid.Id, TargetCardNetUid = fourth.NetUid.Id };
                    if (CommandRestoration.ValidateDecision(again, list, s.CardCatalog, s.Players[me]))
                        throw new InvalidOperationException("a creature without Slay was allowed a second attack");
                }
            });
        }
    }
}
