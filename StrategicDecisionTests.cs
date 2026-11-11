using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Real native catalog/commands/legality/Fight, with explicitly synthetic stats.
    // Caller installs the ordinary engine patches and reviewed RuleImporter first.
    public static class StrategicDecisionTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("strategic-native-lethal-before-trade", delegate {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard attacker = f.Creature(f.One, 0, 5, 5);
                    f.Creature(f.Two, 0, 0, 3);
                    f.AvatarTwo.SetAvatarHealthPreGame(3);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Attack && choice.ActorCardNetUid == attacker.NetUid.Id
                        && choice.TargetCardNetUid == f.AvatarTwo.NetUid.Id, "Immediate lethal was passed for a trade.");
                    CardActions.Fight.StaticExecute(f.State, attacker, new List<ServerCard> { f.AvatarTwo }, -1);
                    Require(f.AvatarTwo.CurrentHealth <= 0, "Selected lethal did not kill avatar with native Fight.");
                }
            });
            test("strategic-avoid-losing-exchange-and-take-favorable-trade", delegate {
                using (Fixture f = new Fixture(config))
                {
                    f.Creature(f.One, 0, 3, 2);
                    ServerCard strong = f.Creature(f.Two, 0, 6, 8);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Attack && choice.TargetCardNetUid == f.AvatarTwo.NetUid.Id,
                        "AI threw a creature into a losing exchange while face was legal.");
                    strong.SetAttribute(AttributeType.Taunt, 1);
                    choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.NoCommand, "AI took a losing forced exchange without benefit.");
                    strong.SetAttribute(AttributeType.Taunt, 0);
                    ServerCard weak = f.Creature(f.Two, 0, 1, 1);
                    choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Attack && choice.TargetCardNetUid == weak.NetUid.Id,
                        "AI did not prefer a profitable kill that preserves its attacker.");
                }
            });
            test("strategic-native-guard-cover-and-exhaustion-legality", delegate {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard attacker = f.Creature(f.One, 0, 3, 3);
                    ServerCard guard = f.Creature(f.Two, 0, 1, 2); guard.SetAttribute(AttributeType.Taunt, 1);
                    ServerCard covered = f.Creature(f.Two, 0, 6, 1); covered.SetAttribute(AttributeType.Cover, 1);
                    Decision choice = f.Choose();
                    Require(choice.Type == CcgRpc.DecisionType.Attack && choice.TargetCardNetUid == guard.NetUid.Id,
                        "Strategic choice bypassed native Guard/Cover targeting.");
                    for (int n = 0; n < attacker.QueryAttribute(AttributeType.NumAttacksPerTurn).Value; n++) attacker.IncrementActionCount();
                    Require(f.Choose().Type == CcgRpc.DecisionType.NoCommand, "AI attacked with an exhausted creature.");
                }
            });
            test("strategic-mana-useful-ring-and-determinism-without-mutation", delegate {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard card = f.Create("Afflicted Alit", GameZoneName.Hand, 0, f.One);
                    f.One.SetAttribute(AttributeType.CanUseRing, 1); f.One.SetAttribute(AttributeType.RingUsesLeft, 3);
                    f.One.SetAttribute(AttributeType.Mana, 0);
                    Require(f.Choose().Type == CcgRpc.DecisionType.NoCommand, "AI spent ring when one mana could not unlock a play.");
                    f.One.SetAttribute(AttributeType.Mana, 1);
                    Require(f.Choose().Type == CcgRpc.DecisionType.UseRing, "AI did not use ring to unlock an available two-cost creature.");
                    f.One.SetAttribute(AttributeType.Mana, 2);
                    CardCommandList list = f.Commands();
                    Decision first = StrategicDecisionRestoration.ChooseDecision(f.Sim, list);
                    for (int n = 0; n < 5; n++)
                    {
                        Decision again = StrategicDecisionRestoration.ChooseDecision(f.Sim, list);
                        Require(first.Equals(again), "Identical native state produced different strategic choices.");
                    }
                    Require(first.Type == CcgRpc.DecisionType.Play && first.ActorCardNetUid == card.NetUid.Id
                        && f.One.QueryAttribute(AttributeType.Mana).Value == 2 && card.CurrentGameZoneName == GameZoneName.Hand,
                        "Chooser played unaffordable card, wasted ring, or mutated native state.");
                    f.One.SetAttribute(AttributeType.Mana, 0);
                    f.One.SetAttribute(AttributeType.CanUseRing, 0);
                    Require(f.Choose().Type == CcgRpc.DecisionType.NoCommand, "AI ignored native available mana.");
                }
            });
            test("strategic-lane-tempo-and-hidden-information-invariance", delegate {
                using (Fixture f = new Fixture(config))
                {
                    f.Create("Afflicted Alit", GameZoneName.Hand, 0, f.One); f.One.SetAttribute(AttributeType.Mana, 2);
                    f.Creature(f.Two, 0, 9, 9);
                    Decision first = f.Choose();
                    Require(first.Type == CcgRpc.DecisionType.Play && first.LaneIndex == 0, "AI failed to contest visible lane pressure.");
                    f.Create("Swamp Leviathan", GameZoneName.Hand, 0, f.Two);
                    f.Create("Ice Spike", GameZoneName.Deck, 0, f.Two);
                    f.Create("Swamp Leviathan", GameZoneName.Deck, 0, f.One);
                    Decision again = f.Choose(); again.CommandUid = first.CommandUid;
                    Require(first.Equals(again), "Hidden hand/deck identities affected strategic choice.");
                }
            });
            test("strategic-self-lethal-summon-and-custom-choice-gates", delegate {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard card = f.Create("Afflicted Alit", GameZoneName.Hand, 0, f.One); f.One.SetAttribute(AttributeType.Mana, 2);
                    f.AvatarOne.SetAvatarHealthPreGame(2);
                    Require(f.Choose().Type == CcgRpc.DecisionType.NoCommand, "AI chose a summon that kills its own avatar.");
                    CardCommandList forced = new CardCommandList { ListUid = 91, MustSelectCommand = true };
                    forced.ValidCommands.Add(new CardCommandState { CardNetUid = card.NetUid.Id, NumActionChoices = 2 });
                    bool rejected = false;
                    try { StrategicDecisionRestoration.ChooseDecision(f.Sim, forced); } catch (NotSupportedException) { rejected = true; }
                    Require(rejected, "Unknown required custom choice was silently approximated.");
                }
            });
            test("strategic-native-ward-firststrike-combat-preview", delegate {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard attacker = f.Creature(f.One, 0, 3, 2);
                    ServerCard defender = f.Creature(f.Two, 0, 3, 2);
                    defender.SetAttribute(AttributeType.Ward, 1);
                    Require(StrategicDecisionRestoration.ScoreAttack(attacker, defender, f.AvatarTwo) < 0,
                        "Preview counted lethal through intact native Ward.");
                    defender.SetAttribute(AttributeType.Ward, 0); attacker.SetAttribute(AttributeType.FirstStrike, 1);
                    Require(StrategicDecisionRestoration.ScoreAttack(attacker, defender, f.AvatarTwo) > 0,
                        "Preview missed native first-strike survival.");
                    CardActions.Fight.StaticExecute(f.State, attacker, new List<ServerCard> { defender }, -1);
                    Require(defender.IsDying() && !attacker.IsDying() && attacker.CurrentHealth == 2,
                        "Native Fight disagreed with first-strike preview.");
                }
            });
            test("strategic-original-javelin-killable-enemy-over-ally-and-native-kill-immunity", delegate {
                using (Fixture f = new Fixture(config))
                {
                    ServerCard javelin = f.Create("Piercing Javelin", GameZoneName.Hand, 0, f.One);
                    f.One.SetAttribute(AttributeType.Mana, 5);
                    ServerCard ally = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.One);
                    ServerCard enemy = f.Create("Young Mammoth", GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard immortal = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    ServerCard indestructible = f.Create("Swamp Leviathan", GameZoneName.CreatureBoard, 0, f.Two);
                    // Original creatures/statistics; the two immunity flags are
                    // explicit isolated fixtures for the surviving native Kill guards.
                    immortal.SetAttribute(AttributeType.Immortal, 1);
                    indestructible.SetAttribute(AttributeType.Indestructible, 1);
                    CardCommandList commands = f.Commands();
                    CardCommandState offered = null;
                    foreach (CardCommandState command in commands.ValidCommands)
                        if (command.CardNetUid == javelin.NetUid.Id) { offered = command; break; }
                    Require(offered != null && offered.PlayTargets.Contains(ally.NetUid.Id) && offered.PlayTargets.Contains(enemy.NetUid.Id)
                        && offered.PlayTargets.Contains(immortal.NetUid.Id) && offered.PlayTargets.Contains(indestructible.NetUid.Id),
                        "Original Javelin native command did not offer the visible ally/enemy/immunity comparison.");
                    int mana = f.One.QueryAttribute(AttributeType.Mana).Value;
                    int allyHealth = ally.CurrentHealth, enemyHealth = enemy.CurrentHealth;
                    Decision choice = StrategicDecisionRestoration.ChooseDecision(f.Sim, commands);
                    Require(choice.Type == CcgRpc.DecisionType.Play && choice.ActorCardNetUid == javelin.NetUid.Id
                        && choice.TargetCardNetUid == enemy.NetUid.Id
                        && CommandRestoration.ValidateDecision(choice, commands, f.Sim.CardCatalog, f.One)
                        && PlayCardHandler.PlayCardIsValid(f.Sim, f.One, javelin, enemy, null, null, choice),
                        "Original Javelin strategy passed a killable enemy, destroyed an ally, or selected a stronger Kill-immune creature.");
                    Require(f.One.QueryAttribute(AttributeType.Mana).Value == mana && ally.CurrentHealth == allyHealth && enemy.CurrentHealth == enemyHealth
                        && !ally.IsDying() && !enemy.IsDying() && !immortal.IsDying() && !indestructible.IsDying()
                        && javelin.CurrentGameZoneName == GameZoneName.Hand,
                        "Original Javelin ranking executed a hypothetical effect or mutated native state.");
                    Require(choice.Equals(StrategicDecisionRestoration.ChooseDecision(f.Sim, commands)),
                        "Original Javelin destroy ranking was nondeterministic on unchanged native commands.");
                }
            });
        }

        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        private sealed class Fixture : IDisposable
        {
            public readonly Simulation Sim = new Simulation();
            public readonly PlayerState One, Two;
            public readonly ServerEffectUtilities.TriggerGameState State;
            public readonly ServerCard AvatarOne, AvatarTwo;
            private readonly Card avatar;
            private readonly List<TriggeredEffect> oldEffects;
            private readonly List<Aura> oldAuras;
            private readonly ActivatedAbility oldAbility;
            private readonly GameConfiguration config;
            public Fixture(GameConfiguration value)
            {
                config = value;
                Sim.RandomGenerator = new RandomIntegerGenerator(48191);
                Sim.CardCatalog = new ServerCardCatalog(Sim.RandomGenerator);
                for (int slot = 0; slot < 2; slot++)
                {
                    PlayerState player = new PlayerState { NetId = new PlayerUniqueId(slot + 1), Name = "Strategic fixture " + slot };
                    foreach (SPTESL.Attribute attribute in config.PlayerDefinitions[0].EntityAttributes)
                    {
                        IntAttribute integer = attribute as IntAttribute; BoolAttribute boolean = attribute as BoolAttribute;
                        if (integer != null) player.SetAttribute(attribute.Name, integer.Value);
                        if (boolean != null) player.SetAttribute(attribute.Name, boolean.Value ? 1 : 0);
                    }
                    player.SetAttribute(AttributeType.Mana, 0); player.SetAttribute(AttributeType.HandSize, 10);
                    player.SetAttribute(AttributeType.CanUseRing, 0); player.SetAttribute(AttributeType.RingUsesLeft, 0);
                    for (int n = 0; n < 28; n++) player.MatchStatValues.Add(0);
                    Sim.Players.Add(player); Sim.CardCatalog.CreatePlayerGameZones(player.NetId, config.Zones, new GameZoneOverrideList());
                }
                One = Sim.Players[0]; Two = Sim.Players[1]; One.IsMyTurn = true;
                Sim.CurrentPlayer = One; Sim.CurrentOpponents.Add(Two);
                TurnState turn = new TurnState { ActivePlayerIndex = 0, TurnPlayerIndex = 0, Phase = TurnPhase.PlayCards,
                    CustomUICards = new List<CustomDisplayCard>() };
                Sim._turnStateStack.Push(turn);
                Sim.HistoryLog.StartEvent(HistoryType.EventGameStart, One.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
                State = new ServerEffectUtilities.TriggerGameState(Sim.HistoryLog, Sim.RandomGenerator, Sim.CardCatalog, Sim.Players,
                    turn.EffectTriggerQueues, new List<ActiveQuest>(), turn.CustomUICards, UniqueResourceId.GetInvalid().Id);
                foreach (CardSet set in config.CardCollection) foreach (Card card in set.Cards)
                    if (avatar == null && Simulation.IsValidAvatarCard(card.ResourceId.Id)) avatar = card;
                Require(avatar != null, "No original avatar available for strategic fixture.");
                oldEffects = avatar.TriggeredEffects; oldAuras = avatar.Auras; oldAbility = avatar.ActivatedAbility;
                // Empty wrappers are isolated fixtures for missing original avatar fields.
                if (avatar.TriggeredEffects == null) avatar.TriggeredEffects = new List<TriggeredEffect>();
                if (avatar.Auras == null) avatar.Auras = new List<Aura>();
                if (avatar.ActivatedAbility == null) avatar.ActivatedAbility = new ActivatedAbility();
                AvatarOne = ServerCardCatalog.CreateCard(State, avatar.ResourceId.Id, false, GameZoneName.AvatarBoard, 0,
                    CardInsertionType.Bottom, One.NetId, null, null);
                AvatarTwo = ServerCardCatalog.CreateCard(State, avatar.ResourceId.Id, false, GameZoneName.AvatarBoard, 0,
                    CardInsertionType.Bottom, Two.NetId, null, null);
                Require(AvatarOne != null && AvatarTwo != null, "Native avatar creation failed.");
                AvatarOne.SetAvatarHealthPreGame(30); AvatarTwo.SetAvatarHealthPreGame(30);
            }
            public ServerCard Create(string name, GameZoneName zone, int lane, PlayerState owner)
            {
                foreach (CardSet set in config.CardCollection) foreach (Card card in set.Cards)
                    if (card.Name == name && RuleImporter.IsSupported(card.ResourceId.Id))
                    {
                        ServerCard created = ServerCardCatalog.CreateCard(State, card.ResourceId.Id, false, zone, lane,
                            CardInsertionType.Bottom, owner.NetId, null, null);
                        Require(created != null, "Strategic native fixture card creation failed: " + name); return created;
                    }
                throw new InvalidOperationException("Required reviewed strategic fixture card missing: " + name);
            }
            public ServerCard Creature(PlayerState owner, int lane, int attack, int health)
            {
                ServerCard card = Create("Enraged Mudcrab", GameZoneName.CreatureBoard, lane, owner);
                card.SetAttribute(AttributeType.Attack, attack); card.SetAttribute(AttributeType.MaxHealth, health);
                card.ModifyCurrentHealth(health - card.CurrentHealth, null, State, true);
                card.OnStartTurn(owner.NetId, State);
                Require(card.CurrentHealth == health && card.QueryAttribute(AttributeType.Attack).Value == attack,
                    "Synthetic combat fixture stats did not apply.");
                return card;
            }
            public CardCommandList Commands()
            {
                CardCommandItem item = Sim.CardCatalog.GenerateCardCommandItem(One, Two, Sim.Players);
                Sim._turnStateStack.Peek().CurrentCommandItem = item; return item.CommandList;
            }
            public Decision Choose()
            {
                CardCommandList commands = Commands();
                Decision decision = StrategicDecisionRestoration.ChooseDecision(Sim, commands);
                Require(CommandRestoration.ValidateDecision(decision, commands, Sim.CardCatalog, One), "AI returned an invalid native decision.");
                return decision;
            }
            public void Dispose()
            {
                avatar.TriggeredEffects = oldEffects; avatar.Auras = oldAuras; avatar.ActivatedAbility = oldAbility;
                Sim.HistoryLog.EndEvent();
            }
        }
    }
}
