using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;
namespace OfflineEngine
{
    public static class OriginalDamageStrategyTests
    {
        public static void Run(GameConfiguration config, Action<string,Action> test)
        {
            test("strategic-original-lightning-lethal-enemy-avatar-over-creatures-and-own-avatar",delegate {
                using(Fixture f=new Fixture(config)) {
                    ServerCard spell=f.Create("Lightning Bolt",GameZoneName.Hand,0,f.One);
                    f.One.SetAttribute(AttributeType.Mana,4);
                    f.AvatarTwo.SetAvatarHealthPreGame(4); f.AvatarOne.SetAvatarHealthPreGame(4);
                    ServerCard ally=f.Create("Swamp Leviathan",GameZoneName.CreatureBoard,0,f.One);
                    ServerCard enemy=f.Create("Swamp Leviathan",GameZoneName.CreatureBoard,0,f.Two);
                    CardCommandList commands=f.Commands(); Decision choice=StrategicDecisionRestoration.ChooseDecision(f.Sim,commands);
                    Require(choice.Type==CcgRpc.DecisionType.Play && choice.ActorCardNetUid==spell.NetUid.Id
                        && choice.TargetCardNetUid==f.AvatarTwo.NetUid.Id && CommandRestoration.ValidateDecision(choice,commands,f.Sim.CardCatalog,f.One),
                        "Original Lightning strategy missed lethal or targeted own avatar/ally/creature instead.");
                    Require(f.AvatarOne.CurrentHealth==4 && f.AvatarTwo.CurrentHealth==4 && ally.CurrentHealth==8 && enemy.CurrentHealth==8
                        && spell.CurrentGameZoneName==GameZoneName.Hand && f.One.QueryAttribute(AttributeType.Mana).Value==4,
                        "Original damage ranking mutated native state.");
                    Require(choice.Equals(StrategicDecisionRestoration.ChooseDecision(f.Sim,commands)),"Original lethal spell ranking is nondeterministic.");
                }
            });
            test("strategic-original-firebolt-passes-with-only-allied-creature-targets",delegate {
                using(Fixture f=new Fixture(config)) {
                    ServerCard spell=f.Create("Firebolt",GameZoneName.Hand,0,f.One);
                    f.One.SetAttribute(AttributeType.Mana,2);
                    ServerCard ally=f.Create("Enraged Mudcrab",GameZoneName.CreatureBoard,0,f.One);
                    Decision choice=f.Choose();
                    Require(choice.Type==CcgRpc.DecisionType.NoCommand && ally.CurrentHealth==1 && spell.CurrentGameZoneName==GameZoneName.Hand,
                        "Original Firebolt strategy damaged its only allied target instead of passing.");
                }
            });
            test("strategic-original-ice-storm-plays-beneficial-enemy-only-board",delegate {
                using(Fixture f=new Fixture(config)) {
                    ServerCard spell=f.Create("Ice Storm",GameZoneName.Hand,0,f.One);
                    f.One.SetAttribute(AttributeType.Mana,12);
                    ServerCard a=f.Create("Enraged Mudcrab",GameZoneName.CreatureBoard,0,f.Two);
                    ServerCard b=f.Create("Enraged Mudcrab",GameZoneName.CreatureBoard,1,f.Two);
                    ServerCard c=f.Create("Deadly Draugr",GameZoneName.CreatureBoard,1,f.Two);
                    CardCommandList commands=f.Commands(); Decision choice=StrategicDecisionRestoration.ChooseDecision(f.Sim,commands);
                    Require(choice.Type==CcgRpc.DecisionType.Play && choice.ActorCardNetUid==spell.NetUid.Id
                        && CommandRestoration.ValidateDecision(choice,commands,f.Sim.CardCatalog,f.One)
                        && PlayCardHandler.PlayCardIsValid(f.Sim,f.One,spell,null,null,null,choice),
                        "Original Ice Storm strategy passed a favorable enemy-only clear; inspect native Subtype/Zone scoring eligibility.");
                    Require(a.CurrentHealth==1 && b.CurrentHealth==1 && c.CurrentHealth==1 && spell.CurrentGameZoneName==GameZoneName.Hand,
                        "Original board-clear ranking executed hypothetical damage.");
                }
            });
            test("strategic-original-ice-storm-avoids-losing-own-board-for-small-enemy-damage",delegate {
                using(Fixture f=new Fixture(config)) {
                    ServerCard spell=f.Create("Ice Storm",GameZoneName.Hand,0,f.One);
                    f.One.SetAttribute(AttributeType.Mana,12);
                    ServerCard a=f.Create("Enraged Mudcrab",GameZoneName.CreatureBoard,0,f.One);
                    ServerCard b=f.Create("Enraged Mudcrab",GameZoneName.CreatureBoard,1,f.One);
                    ServerCard c=f.Create("Deadly Draugr",GameZoneName.CreatureBoard,1,f.One);
                    ServerCard enemy=f.Create("Swamp Leviathan",GameZoneName.CreatureBoard,0,f.Two);
                    Decision choice=f.Choose();
                    Require(choice.Type==CcgRpc.DecisionType.NoCommand && a.CurrentHealth==1 && b.CurrentHealth==1 && c.CurrentHealth==1
                        && enemy.CurrentHealth==8 && spell.CurrentGameZoneName==GameZoneName.Hand,
                        "Original Ice Storm strategy sacrifices its own board for nonlethal enemy damage.");
                }
            });
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        internal sealed class Fixture : IDisposable
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


