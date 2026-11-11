using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Special lane rules for story/puzzle scenarios, as native LaneTriggers (the installed config
    // has none; LaneTriggers and their play restrictions were server data). Rule text: UESP
    // Legends:Lanes (research notes: story-lane-types.md). Every graph here is a
    // reconstruction: a native trigger (lane-conditional where the event card is in the lane)
    // whose single custom action runs native actions/static executes on the lane's cards.
    // Play restrictions (Jail, Mine Depths, Incinerator, Sewer) clear the lane bit of creature
    // play commands. Venom is a native lane aura; Library is a hand-cost aura applied by
    // OfflineAuraRestoration. Smelting follows UESP Legends:The_Caminus_Pit (CWC Kagouti Fabricant).
    // Interpretations of ambiguous text are noted per lane below.
    public static class StoryLaneRules
    {
        public static int WindyMoves, PlunderEquips, LaneEffects;
        public static readonly CardLaneType[] Supported = {
            CardLaneType.Plunder, CardLaneType.Windy, CardLaneType.Altar_Of_Silence, CardLaneType.Armor, CardLaneType.Armory,
            CardLaneType.Barracks, CardLaneType.Campfire, CardLaneType.Conveyor, CardLaneType.Docks, CardLaneType.Eclipse,
            CardLaneType.Flanking, CardLaneType.Fountain, CardLaneType.Graveyard, CardLaneType.Heist, CardLaneType.Incinerator,
            CardLaneType.Jail, CardLaneType.Library, CardLaneType.Liquid_Courage, CardLaneType.Madness, CardLaneType.Mage_Tower,
            CardLaneType.Masquerade_Ball, CardLaneType.Mine_Depths, CardLaneType.Monastery, CardLaneType.Order, CardLaneType.Reanimation,
            CardLaneType.Sewer, CardLaneType.Smelting, CardLaneType.Surplus, CardLaneType.Temple, CardLaneType.Torment, CardLaneType.Venom, CardLaneType.Water,
            // arena/draft lanes (rule text: localization scenariomodifiers.<Name>.text), added for mod game modes
            CardLaneType.Renewal, CardLaneType.Siege, CardLaneType.Killing_Field, CardLaneType.King_Of_The_Hill, CardLaneType.Blitz, CardLaneType.Ballista_Tower,
            CardLaneType.Hall_Of_Mirrors, CardLaneType.Champions_Arena, CardLaneType.Lucky, CardLaneType.Fortifications, CardLaneType.Warzone, CardLaneType.Inner_Fire,
            CardLaneType.Zoo, CardLaneType.Wispmothers_Blessing, CardLaneType.Mania, CardLaneType.Dementia };

        // Creature plays into these lanes are refused (abilities may still summon there).
        private static readonly CardLaneType[] NoPlay = { CardLaneType.Jail, CardLaneType.Mine_Depths, CardLaneType.Incinerator };
        private static readonly AttributeType[] Keywords = { AttributeType.Taunt, AttributeType.Deathtouch, AttributeType.Drain, AttributeType.Breakthrough,
            AttributeType.Charge, AttributeType.Regenerate };
        private static int[] items = new int[0], creaturesByCost = new int[0], animals = new int[0];
        private static Dictionary<int, List<int>> actionPool = new Dictionary<int, List<int>>();
        private static readonly Dictionary<int, int> championTurn = new Dictionary<int, int>();
        private static Dictionary<int, List<int>> creaturePool = new Dictionary<int, List<int>>();
        private static Card skeleton, draugr;
        private static readonly HashSet<CardInstanceId> reanimated = new HashSet<CardInstanceId>();
        public static readonly Enchantment LibraryDiscount = Mod(EnchantmentAttributeModificationType.Add, AttributeType.ManaCost, -1);

        public delegate void LaneEffect(Lane c);

        // Everything a lane effect needs: the event, the acting lane, and native helpers.
        public sealed class Lane
        {
            public CardActionArguments Args;
            public ServerEffectUtilities.TriggerGameState Gs { get { return Args.TriggerGameState; } }
            public int Index { get { return Args.TriggerArguments.ActingLaneIndex; } }
            public ServerCard Inciter { get { return Args.CardArguments.InciterCard; } }
            public ServerCard Target { get { return Args.CardArguments.EventTargetCard; } }
            public PlayerState InciterPlayer { get { return Args.PlayerArguments.InciterPlayerState; } }
            public PlayerState Owner(ServerCard card) { return card == null ? null : Gs.Players.Find(delegate(PlayerState p) { return p.NetId == card.OwnerNetId; }); }
            public bool Here(ServerCard card) { return card != null && card.CurrentGameZoneName == GameZoneName.CreatureBoard && card.CurrentLaneIndex == Index && !card.IsDying(); }
            public List<ServerCard> Creatures(PlayerState player, int lane)
            { return Gs.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return x.CurrentLaneIndex == lane && !x.IsDying(); }, player.NetId, GameZoneName.CreatureBoard); }
            public ServerCard Random(List<ServerCard> cards) { return cards.Count == 0 ? null : cards[Gs.RandomGenerator.GenerateInRange(0, cards.Count - 1)]; }
            public void Run(ICardAction action, params ServerCard[] targets)
            {
                List<ServerCard> list = new List<ServerCard>();
                foreach (ServerCard t in targets) if (t != null) list.Add(t);
                if (list.Count == 0) return;
                action.Execute(new CardActionArguments(Gs, list, Args.CardArguments, Args.PlayerArguments, Args.TriggerArguments, Args.AssetHash, Args.UsingDefaultFx));
                LaneEffects++;
            }
            public void Buff(ServerCard card, int attack, int health)
            { if (card != null && (attack != 0 || health != 0)) Run(new CardActions.Buff { Enchantment = Stats(attack, health) }, card); }
            public ServerCard Summon(Card card, PlayerState owner, int lane)
            {
                if (card == null || owner == null || Gs.CardCatalog.IsLaneFull(GameZoneName.CreatureBoard, lane, owner.NetId)) return null;
                GameZone.Position position = Gs.CardCatalog.FindInsertionPosition(owner.NetId, GameZoneName.CreatureBoard, lane, CardInsertionType.Bottom);
                List<ServerCard> made = PlayerActions.CreateToken.StaticExecute(Gs, null, card, false, GameZoneType.Board, position, owner.NetId, 1, -1, null);
                LaneEffects++;
                return made == null || made.Count == 0 ? null : made[0];
            }
            public ServerCard Avatar(PlayerState player)
            {
                List<ServerCard> avatars = Gs.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return x.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarBoard);
                return avatars.Count == 0 ? null : avatars[0];
            }
        }

        public sealed class LaneAction : ICardAction
        {
            public LaneEffect Effect; public CardFxAction Fx = CardFxAction.Buff;
            public CardFxAction GetFxActionType() { return Fx; }
            public void Execute(CardActionArguments arguments) { Effect(new Lane { Args = arguments }); }
        }

        public static void Install(LaneTrigger[] lanes, GameConfiguration config)
        {
            Prepare(config);
            Set(lanes, CardLaneType.Plunder, EnterHere(delegate(Lane c) {
                // "After a creature is summoned here, attach a random item to it."
                if (!c.Here(c.Inciter) || items.Length == 0) return;
                c.Run(new CardActions.EquipNewItem { ItemReference = new EditorCardReference(items[c.Gs.RandomGenerator.GenerateInRange(0, items.Length - 1)]) }, c.Inciter);
                PlunderEquips++; }));
            // "At the end of your opponent's turn, a random creature switches lanes": one random
            // creature of the player whose turn did not just end, in this lane, moves (if room).
            Set(lanes, CardLaneType.Windy, Item(EffectTriggerType.WhenPlayerTurnEnds, false, delegate(Lane c) {
                if (c.InciterPlayer == null) return;
                PlayerState other = c.Gs.Players.Find(delegate(PlayerState p) { return p.NetId != c.InciterPlayer.NetId; });
                if (other == null) return;
                ServerCard chosen = c.Random(c.Creatures(other, c.Index));
                if (chosen == null || c.Gs.CardCatalog.IsLaneFull(GameZoneName.CreatureBoard, 1 - c.Index, other.NetId)) return;
                c.Run(new CardActions.ChangeLane(), chosen); WindyMoves++; }));
            // "After a player Silences a creature, that player summons a 1/1 Skeleton here and gives it Guard."
            // The silencing card's owner is "that player"; "here" = the lane of the silenced creature.
            Set(lanes, CardLaneType.Altar_Of_Silence, Item(EffectTriggerType.WhenCardIsSilenced, false, delegate(Lane c) {
                if (!c.Here(c.Target)) return;
                PlayerState player = c.Owner(c.Inciter) ?? c.Owner(c.Target);
                ServerCard made = c.Summon(skeleton, player, c.Index);
                if (made != null) c.Run(new CardActions.GiveKeyword { KeywordAttrib = AttributeType.Taunt, RandomKeyword = false }, made); }));
            Set(lanes, CardLaneType.Armor, EnterHere(delegate(Lane c) {
                // "After a creature is summoned here, double its health."
                if (c.Here(c.Inciter)) c.Buff(c.Inciter, 0, c.Inciter.QueryAttribute(AttributeType.MaxHealth).Value); }));
            Set(lanes, CardLaneType.Armory, EnterHere(delegate(Lane c) {
                // "After you summon a creature here, a random friendly creature here gets +1/+1."
                if (c.Here(c.Inciter)) c.Buff(c.Random(c.Creatures(c.Owner(c.Inciter), c.Index)), 1, 1); }));
            Set(lanes, CardLaneType.Barracks, EnterHere(delegate(Lane c) {
                // "After you summon a creature here with 4 or more power, draw a card."
                if (c.Here(c.Inciter) && c.Inciter.QueryAttribute(AttributeType.Attack).Value >= 4)
                    PlayerActions.DrawCards.StaticExecute(c.Gs, c.Owner(c.Inciter), 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1); }));
            Set(lanes, CardLaneType.Campfire, EnterHere(delegate(Lane c) {
                // "After you summon a creature here, friendly creatures gain its keyword."
                if (!c.Here(c.Inciter)) return;
                PlayerState owner = c.Owner(c.Inciter);
                foreach (AttributeType keyword in Keywords)
                {
                    if (c.Inciter.QueryAttribute(keyword).Value <= 0) continue;
                    List<ServerCard> friends = c.Gs.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return x != c.Inciter && !x.IsDying(); }, owner.NetId, GameZoneName.CreatureBoard);
                    if (friends.Count > 0) c.Run(new CardActions.GiveKeyword { KeywordAttrib = keyword, RandomKeyword = false }, friends.ToArray());
                } }));
            // "At the end of your turn, creatures here move." (the ending player's creatures, if room)
            Set(lanes, CardLaneType.Conveyor, Item(EffectTriggerType.WhenPlayerTurnEnds, false, delegate(Lane c) {
                if (c.InciterPlayer == null) return;
                foreach (ServerCard card in c.Creatures(c.InciterPlayer, c.Index))
                    if (!c.Gs.CardCatalog.IsLaneFull(GameZoneName.CreatureBoard, 1 - c.Index, c.InciterPlayer.NetId)) c.Run(new CardActions.ChangeLane(), card); }));
            // "After a creature is attacked here, it falls into the water." (moves to the other lane, the Water lane)
            Set(lanes, CardLaneType.Docks, Item(EffectTriggerType.WhenCardDefends, false, delegate(Lane c) {
                if (c.Here(c.Inciter) && !c.Gs.CardCatalog.IsLaneFull(GameZoneName.CreatureBoard, 1 - c.Index, c.Inciter.OwnerNetId)) c.Run(new CardActions.ChangeLane(), c.Inciter); }));
            Set(lanes, CardLaneType.Eclipse, EnterHere(delegate(Lane c) {
                // "After a creature is summoned here, trigger its Last Gasp."
                if (c.Here(c.Inciter)) ServerEffectUtilities.TriggerDirectEffectEvent(c.Gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.LastGaspTrigger, c.Inciter, c.Gs.Players)); }));
            Set(lanes, CardLaneType.Flanking, EnterHere(delegate(Lane c) {
                // "After a creature is summoned here, give friendly creatures in the other lane +1/+0."
                if (!c.Here(c.Inciter)) return;
                foreach (ServerCard card in c.Creatures(c.Owner(c.Inciter), 1 - c.Index)) c.Buff(card, 1, 0); }));
            Set(lanes, CardLaneType.Fountain, EnterHere(delegate(Lane c) {
                // "Creatures with 2 power or less summoned here gain a Ward."
                if (c.Here(c.Inciter) && c.Inciter.QueryAttribute(AttributeType.Attack).Value <= 2) c.Run(new CardActions.Ward(), c.Inciter); }));
            // "After a creature other than Rotting Draugr is destroyed, summon a 1/1 Rotting Draugr."
            // Read as: a creature dying in this lane; the Draugr joins its owner's side of this lane.
            Set(lanes, CardLaneType.Graveyard, Item(EffectTriggerType.WhenCardDies, false, delegate(Lane c) {
                if (c.Inciter == null || draugr == null || c.Inciter.CardId == draugr.ResourceId.Id || c.Inciter.CurrentLaneIndex != c.Index || !c.Inciter.HasSubtype(CardSubtype.Creature)) return;
                c.Summon(draugr, c.Owner(c.Inciter), c.Index); }));
            // "Creatures in this lane have Pilfer: Gain 1 magicka." (Pilfer = damaging the opponent)
            Set(lanes, CardLaneType.Heist, Item(EffectTriggerType.WhenCardDealsDamage, false, delegate(Lane c) {
                if (Pilfer(c)) PlayerActions.ModifyMana.StaticExecute(c.Gs, c.Owner(c.Inciter), 1); }));
            // "At the end of your turn, creatures here are dealt 1 damage." (+ no creature plays)
            Set(lanes, CardLaneType.Incinerator, Item(EffectTriggerType.WhenPlayerTurnEnds, false, delegate(Lane c) {
                if (c.InciterPlayer == null) return;
                List<ServerCard> burning = c.Creatures(c.InciterPlayer, c.Index);
                if (burning.Count > 0) c.Run(new CardActions.Damage { HealthLoss = new ActionValueData(1), DamageSource = CardRole.Inciter }, burning.ToArray()); }));
            Set(lanes, CardLaneType.Jail, new LaneTrigger());
            Set(lanes, CardLaneType.Library, new LaneTrigger()); // hand-cost aura (OfflineAuraRestoration.ApplyLaneAuras)
            Set(lanes, CardLaneType.Liquid_Courage, Item(EffectTriggerType.WhenCardTakesDamage, false, delegate(Lane c) {
                // "After a creature here takes damage, it gains +2/+0."
                if (c.Here(c.Inciter) && c.Inciter.HasSubtype(CardSubtype.Creature)) c.Buff(c.Inciter, 2, 0); }));
            Set(lanes, CardLaneType.Madness, Item(EffectTriggerType.WhenCardDealsDamage, false, delegate(Lane c) {
                // "Creatures here have 'Pilfer: Transform into a random creature with cost 1 greater.'"
                if (!Pilfer(c)) return;
                int cost = c.Inciter.QueryAttribute(AttributeType.ManaCost).Value + 1;
                List<int> pool;
                if (!creaturePool.TryGetValue(Math.Min(cost, 12), out pool) || pool.Count == 0) return;
                c.Run(new CardActions.TransformCard { CardReference = new EditorCardReference(pool[c.Gs.RandomGenerator.GenerateInRange(0, pool.Count - 1)]) }, c.Inciter); }));
            // "After you play an action, summon a random creature in this lane with the same cost."
            Set(lanes, CardLaneType.Mage_Tower, Item(EffectTriggerType.WhenCardIsPlayed, false, delegate(Lane c) {
                if (c.Inciter == null || !c.Inciter.HasSubtype(CardSubtype.Spell)) return;
                List<int> pool;
                if (!creaturePool.TryGetValue(Math.Min(c.Inciter.QueryAttribute(AttributeType.ManaCost).Value, 12), out pool) || pool.Count == 0) return;
                c.Summon(GameManager.Instance.GetCard(pool[c.Gs.RandomGenerator.GenerateInRange(0, pool.Count - 1)]), c.Owner(c.Inciter), c.Index); }));
            Set(lanes, CardLaneType.Masquerade_Ball, Item(EffectTriggerType.WhenCardChangesLane, true, delegate(Lane c) {
                // "After a creature moves into this lane, give it +1/+1."
                if (c.Here(c.Inciter)) c.Buff(c.Inciter, 1, 1); }));
            // "Each turn, a player gains 1 magicka for each creature they have here." (+ no creature plays)
            Set(lanes, CardLaneType.Mine_Depths, Item(EffectTriggerType.WhenPlayerTurnStarts, false, delegate(Lane c) {
                if (c.InciterPlayer == null) return;
                int count = c.Creatures(c.InciterPlayer, c.Index).Count;
                if (count > 0) PlayerActions.ModifyMana.StaticExecute(c.Gs, c.InciterPlayer, count); }));
            // "At the start of each player's turn, destroy all their creatures here and they draw a card for each."
            Set(lanes, CardLaneType.Smelting, Item(EffectTriggerType.WhenPlayerTurnStarts, false, delegate(Lane c) {
                if (c.InciterPlayer == null) return;
                List<ServerCard> molten = c.Creatures(c.InciterPlayer, c.Index);
                if (molten.Count == 0) return;
                c.Run(new CardActions.Destroy { Sacrifice = false }, molten.ToArray());
                PlayerActions.DrawCards.StaticExecute(c.Gs, c.InciterPlayer, molten.Count, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1); }));
            // "Whenever a player gains health, give a random creature of theirs in this lane +1/+1."
            Set(lanes, CardLaneType.Monastery, Item(EffectTriggerType.WhenCardIsHealed, false, delegate(Lane c) {
                if (c.Target == null || !c.Target.HasSubtype(CardSubtype.Avatar)) return;
                c.Buff(c.Random(c.Creatures(c.Owner(c.Target), c.Index)), 1, 1); }));
            // "When a creature enters this lane, set its power and health equal to its cost."
            LaneEffect order = delegate(Lane c) {
                if (!c.Here(c.Inciter)) return;
                int cost = c.Inciter.QueryAttribute(AttributeType.ManaCost).Value;
                Enchantment set = Mod(EnchantmentAttributeModificationType.Set, AttributeType.Attack, cost);
                set.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Set, AttributeType.MaxHealth, Math.Max(1, cost)));
                c.Run(new CardActions.Buff { Enchantment = set }, c.Inciter); };
            Set(lanes, CardLaneType.Order, Both(order));
            // "When a non-Reanimated creature in this lane dies for the first time, return it to play as a 1/1."
            Set(lanes, CardLaneType.Reanimation, Item(EffectTriggerType.WhenCardDies, false, delegate(Lane c) {
                if (c.Inciter == null || c.Inciter.CurrentLaneIndex != c.Index || !c.Inciter.HasSubtype(CardSubtype.Creature) || reanimated.Contains(c.Inciter.InstanceId)) return;
                ServerCard made = c.Summon(GameManager.Instance.GetCard(c.Inciter.CardId), c.Owner(c.Inciter), c.Index);
                if (made == null) return;
                reanimated.Add(made.InstanceId);
                Enchantment one = Mod(EnchantmentAttributeModificationType.Set, AttributeType.Attack, 1);
                one.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Set, AttributeType.MaxHealth, 1));
                c.Run(new CardActions.Buff { Enchantment = one }, made); }));
            Set(lanes, CardLaneType.Sewer, new LaneTrigger()); // play restriction only
            Set(lanes, CardLaneType.Surplus, EnterHere(delegate(Lane c) {
                // "After you summon a creature here, reduce the cost of a random card in your hand by 1."
                if (!c.Here(c.Inciter)) return;
                ServerCard card = c.Random(c.Gs.CardCatalog.GatherCardsInZone(delegate(ServerCard x) { return x.QueryAttribute(AttributeType.ManaCost).Value > 0; }, c.Inciter.OwnerNetId, GameZoneName.Hand));
                if (card != null) c.Run(new CardActions.Debuff { Enchantment = Mod(EnchantmentAttributeModificationType.Add, AttributeType.ManaCost, -1) }, card); }));
            Set(lanes, CardLaneType.Temple, EnterHere(delegate(Lane c) {
                // "After you summon a creature here, gain 1 health."
                if (c.Here(c.Inciter)) c.Run(new CardActions.Heal { HealthGain = new ActionValueData(1), FullHeal = false, HealToAVDValue = false }, c.Avatar(c.Owner(c.Inciter))); }));
            Set(lanes, CardLaneType.Torment, EnterHere(delegate(Lane c) {
                // "After a creature is summoned here, deal 1 damage to it."
                if (c.Here(c.Inciter)) c.Run(new CardActions.Damage { HealthLoss = new ActionValueData(1), DamageSource = CardRole.Inciter }, c.Inciter); }));
            // "Creatures in this lane have Lethal." (native lane aura)
            LaneTrigger venom = new LaneTrigger();
            AuraEnchantment lethal = new AuraEnchantment { Name = "Venom", Enchantment = Mod(EnchantmentAttributeModificationType.Add, AttributeType.Deathtouch, 1) };
            lethal.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            venom.AuraEnchantments.Add(lethal);
            Set(lanes, CardLaneType.Venom, venom);
            // "When a creature enters this lane, Shackle it."
            Set(lanes, CardLaneType.Water, Both(delegate(Lane c) {
                if (c.Here(c.Inciter)) c.Run(new CardActions.Freeze { PermanentFreeze = false, Type = EnchantmentUtilities.FreezeType.Normal }, c.Inciter); }));
            InstallArenaLanes(lanes);
        }

        private static LaneTrigger Aura(string name, Enchantment e, ICardConditionalExpression extra)
        {
            LaneTrigger lane = new LaneTrigger();
            AuraEnchantment aura = new AuraEnchantment { Name = name, Enchantment = e };
            aura.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Creature });
            if (extra != null) aura.Selectors.Add(extra);
            lane.AuraEnchantments.Add(aura);
            return lane;
        }

        private static PlayerState Other(Lane c, PlayerState p) { return p == null ? null : c.Gs.Players.Find(delegate(PlayerState x) { return x.NetId != p.NetId; }); }

        // The player owning the single strongest creature (by attribute) in this lane, or null on a tie / empty lane.
        private static PlayerState Strongest(Lane c, AttributeType attribute)
        {
            int best = int.MinValue; PlayerState owner = null; bool tie = false;
            foreach (PlayerState p in c.Gs.Players)
                foreach (ServerCard card in c.Creatures(p, c.Index))
                {
                    int v = attribute == AttributeType.MaxHealth ? card.CurrentHealth : card.QueryAttribute(attribute).Value;
                    if (v > best) { best = v; owner = p; tie = false; } else if (v == best && owner != p) tie = true;
                }
            return tie ? null : owner;
        }

        private static void InstallArenaLanes(LaneTrigger[] lanes)
        {
            Set(lanes, CardLaneType.Renewal, Aura("Renewal", Mod(EnchantmentAttributeModificationType.Add, AttributeType.Regenerate, 1), null));
            Set(lanes, CardLaneType.Siege, Aura("Siege", Mod(EnchantmentAttributeModificationType.Add, AttributeType.Breakthrough, 1), null));
            Set(lanes, CardLaneType.Killing_Field, Aura("Killing Field", Stats(1, 0), null));
            Set(lanes, CardLaneType.Blitz, Aura("Blitz", Mod(EnchantmentAttributeModificationType.Set, AttributeType.Taunt, 0), null));
            CardConditionals.AttributeValue guard = new CardConditionals.AttributeValue { AttributeName = AttributeType.Taunt, Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo };
            guard.Value.ConstantValue = 1; guard.Value.ConstantMultiplier = 1;
            Set(lanes, CardLaneType.Fortifications, Aura("Fortifications", Stats(1, 1), guard));
            Set(lanes, CardLaneType.King_Of_The_Hill, EnterHere(delegate(Lane c) {
                if (c.Here(c.Inciter) && c.Inciter.QueryAttribute(AttributeType.ManaCost).Value >= 5) c.Run(new CardActions.GiveKeyword { KeywordAttrib = AttributeType.Taunt, RandomKeyword = false }, c.Inciter); }));
            Set(lanes, CardLaneType.Lucky, EnterHere(delegate(Lane c) {
                if (c.Here(c.Inciter)) c.Run(new CardActions.GiveKeyword { KeywordAttrib = AttributeType.None, RandomKeyword = true }, c.Inciter); }));
            Set(lanes, CardLaneType.Warzone, EnterHere(delegate(Lane c) {
                if (!c.Here(c.Inciter)) return;
                PlayerState foe = Other(c, c.Owner(c.Inciter));
                if (foe != null) { PlayerActions.DamageAvatarOfPlayer.StaticExecute(c.Gs, c.Inciter, foe, 1, -1); LaneEffects++; } }));
            Set(lanes, CardLaneType.Hall_Of_Mirrors, EnterHere(delegate(Lane c) {
                if (!c.Here(c.Inciter)) return;
                PlayerState owner = c.Owner(c.Inciter);
                if (c.Creatures(owner, c.Index).Count == 1) c.Summon(GameManager.Instance.GetCard(c.Inciter.CardId), owner, c.Index); }));
            Set(lanes, CardLaneType.Wispmothers_Blessing, EnterHere(delegate(Lane c) {
                if (!c.Here(c.Inciter) || c.Inciter.QueryAttribute(AttributeType.ManaCost).Value > 2) return;
                c.Summon(GameManager.Instance.GetCard(c.Inciter.CardId), c.Owner(c.Inciter), 1 - c.Index); }));
            Set(lanes, CardLaneType.Zoo, EnterHere(delegate(Lane c) {
                if (!c.Here(c.Inciter) || animals.Length == 0 || c.Inciter.HasSubtype(CardSubtype.Animal)) return;
                c.Run(new CardActions.TransformCard { CardReference = new EditorCardReference(animals[c.Gs.RandomGenerator.GenerateInRange(0, animals.Length - 1)]) }, c.Inciter); }));
            Set(lanes, CardLaneType.Inner_Fire, Item(EffectTriggerType.WhenCardDies, false, delegate(Lane c) {
                if (c.Inciter == null || c.Inciter.CurrentLaneIndex != c.Index || !c.Inciter.HasSubtype(CardSubtype.Creature)) return;
                List<int> pool;
                if (!actionPool.TryGetValue(Math.Min(12, c.Inciter.QueryAttribute(AttributeType.ManaCost).Value), out pool) || pool.Count == 0) return;
                PlayerState owner = c.Owner(c.Inciter);
                GameZone.Position pos = c.Gs.CardCatalog.FindInsertionPosition(owner.NetId, GameZoneName.Hand, 0, CardInsertionType.Bottom);
                PlayerActions.CreateToken.StaticExecute(c.Gs, null, GameManager.Instance.GetCard(pool[c.Gs.RandomGenerator.GenerateInRange(0, pool.Count - 1)]), false, GameZoneType.Hand, pos, owner.NetId, 1, -1, null);
                LaneEffects++; }));
            Set(lanes, CardLaneType.Ballista_Tower, Item(EffectTriggerType.WhenPlayerTurnStarts, false, delegate(Lane c) {
                PlayerState me = c.InciterPlayer, foe = Other(c, me);
                if (me == null || foe == null || c.Creatures(me, c.Index).Count <= c.Creatures(foe, c.Index).Count) return;
                PlayerActions.DamageAvatarOfPlayer.StaticExecute(c.Gs, null, foe, 2, -1); LaneEffects++; }));
            Set(lanes, CardLaneType.Mania, Item(EffectTriggerType.WhenPlayerTurnStarts, false, delegate(Lane c) {
                if (c.InciterPlayer != null && Strongest(c, AttributeType.MaxHealth) == c.InciterPlayer)
                    PlayerActions.DrawCards.StaticExecute(c.Gs, c.InciterPlayer, 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, false, -1); }));
            Set(lanes, CardLaneType.Dementia, Item(EffectTriggerType.WhenPlayerTurnStarts, false, delegate(Lane c) {
                PlayerState foe = Other(c, c.InciterPlayer);
                if (c.InciterPlayer != null && foe != null && Strongest(c, AttributeType.Attack) == c.InciterPlayer) { PlayerActions.DamageAvatarOfPlayer.StaticExecute(c.Gs, null, foe, 3, -1); LaneEffects++; } }));
            Set(lanes, CardLaneType.Champions_Arena, Item(EffectTriggerType.WhenCardAttacks, false, delegate(Lane c) {
                if (!c.Here(c.Inciter) || !c.Inciter.HasSubtype(CardSubtype.Creature)) return;
                PlayerState owner = c.Owner(c.Inciter);
                int turn = owner.QueryAttribute(AttributeType.TurnCount).Value, last;
                if (championTurn.TryGetValue(owner.NetId.Id, out last) && last == turn) return;
                championTurn[owner.NetId.Id] = turn;
                Enchantment extra = Mod(EnchantmentAttributeModificationType.Add, AttributeType.NumAttacksPerTurn, 1);
                extra.ExpiresAtEndOfTurn = true;
                c.Run(new CardActions.Buff { Enchantment = extra }, c.Inciter); }));
        }

        public static void ResetMatch() { reanimated.Clear(); championTurn.Clear(); }

        // A creature in this lane damaging the opposing avatar (the Pilfer event).
        private static bool Pilfer(Lane c)
        {
            return c.Here(c.Inciter) && c.Inciter.HasSubtype(CardSubtype.Creature) && c.Target != null && c.Target.HasSubtype(CardSubtype.Avatar)
                && c.Target.OwnerNetId != c.Inciter.OwnerNetId;
        }

        private static void Prepare(GameConfiguration config)
        {
            List<int> itemList = new List<int>(), animalList = new List<int>();
            creaturePool = new Dictionary<int, List<int>>();
            actionPool = new Dictionary<int, List<int>>();
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card == null || !card.Production) continue;
                    if (card.Name == "Skeleton" && card.Definition == "Creature" && skeleton == null) skeleton = card;
                    if (card.Name == "Rotting Draugr" && card.Definition == "Creature" && draugr == null) draugr = card;
                    if (card.Source != CardSource.Normal || string.IsNullOrEmpty(card.ExportCode)) continue;
                    if (card.Definition == "Item") itemList.Add(card.ResourceId.Id);
                    if (card.Definition == "Creature" && card.HasSubtype(CardSubtype.Animal)) animalList.Add(card.ResourceId.Id);
                    if (card.Definition == "Action")
                    {
                        int acost = Math.Min(12, Math.Max(0, card.GetIntegerAttribute(AttributeType.ManaCost)));
                        List<int> apool;
                        if (!actionPool.TryGetValue(acost, out apool)) actionPool[acost] = apool = new List<int>();
                        apool.Add(card.ResourceId.Id);
                    }
                    if (card.Definition == "Creature")
                    {
                        int cost = Math.Min(12, Math.Max(0, card.GetIntegerAttribute(AttributeType.ManaCost)));
                        List<int> pool;
                        if (!creaturePool.TryGetValue(cost, out pool)) creaturePool[cost] = pool = new List<int>();
                        pool.Add(card.ResourceId.Id);
                    }
                }
            items = itemList.ToArray(); animals = animalList.ToArray();
            reanimated.Clear();
        }

        private static void Set(LaneTrigger[] lanes, CardLaneType type, LaneTrigger trigger) { lanes[(int)type] = trigger; }

        private static LaneTrigger EnterHere(LaneEffect effect) { return Item(EffectTriggerType.WhenCardEntersBoard, true, effect); }

        private static LaneTrigger Both(LaneEffect effect)
        {
            LaneTrigger lane = Item(EffectTriggerType.WhenCardEntersBoard, true, effect);
            lane.Triggers.Add(Item(EffectTriggerType.WhenCardChangesLane, true, effect).Triggers[0]);
            return lane;
        }

        // One lane trigger whose custom action always runs once (the selector matches the two
        // avatars) and does its own lane/event filtering.
        private static LaneTrigger Item(EffectTriggerType type, bool inciterInLane, LaneEffect effect)
        {
            LaneTrigger lane = new LaneTrigger();
            LaneTriggerItem item = new LaneTriggerItem();
            if (inciterInLane) item.Conditionals.Add(new LaneCardConditionals.CardInLane { Condition = LaneCardConditionals.CardInLane.Requirement.Is, CardRole = CardRole.Inciter });
            item.Trigger.TriggerType = type;
            item.Trigger.SortOrder = TriggerSortOrder.EndOfActiveQueue;
            item.Trigger.ClientTargetingType = PlayTargetingType.NoTarget;
            Effect e = new Effect();
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = new LaneAction { Effect = effect } };
            action.Selectors.Add(new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = CardSubtype.Avatar });
            action.SelectorSort = new CardListSort { Method = CardListSort.SortMethod.Random, NumToReturn = new ActionValueData(1) };
            e.CardActions.Add(action);
            item.Trigger.EffectGroup.Effects.Add(e);
            lane.Triggers.Add(item);
            return lane;
        }

        private static Enchantment Mod(EnchantmentAttributeModificationType type, AttributeType attribute, int value)
        {
            Enchantment e = new Enchantment();
            e.AttributeModifications.Add(new EnchantmentAttributeModification(type, attribute, value));
            return e;
        }

        private static Enchantment Stats(int attack, int health)
        {
            Enchantment e = new Enchantment();
            if (attack != 0) e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.Attack, attack));
            if (health != 0) e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxHealth, health));
            return e;
        }

        // Lane play restrictions on creature play commands (native FillPlayCardCommandStateNoPrerequisites postfix).
        public static void RestrictPlayLanes(CcgRpc.CardCommandState state, ServerCard card, PlayerState player, ServerCardCatalog catalog)
        {
            if (!StoryScenarioRules.Active || state == null || card == null || player == null || !card.HasSubtype(CardSubtype.Creature) || state.ValidBoardLanes == 0) return;
            int lanes = state.ValidBoardLanes;
            for (int lane = 0; lane < 2; lane++)
            {
                if ((lanes & (1 << lane)) == 0) continue;
                CardLaneType type = catalog.QueryCreatureBoardLaneType(player.NetId, lane);
                bool refused = Array.IndexOf(NoPlay, type) >= 0 || (type == CardLaneType.Sewer && card.QueryAttribute(AttributeType.MaxHealth).Value > 2);
                if (refused) lanes &= ~(1 << lane);
            }
            state.ValidBoardLanes = lanes;
            state.FullBoardLanes &= lanes;
            if (lanes == 0) state.ValidCardCommandMask &= ~(int)CardCommand.PlayToPosition;
        }

        // Library: "While you have a creature here, actions cost 1 less." Applied as a hand aura.
        public static bool LibraryActive(ServerCardCatalog catalog, PlayerState player)
        {
            for (int lane = 0; lane < catalog.FindLaneCount(GameZoneName.CreatureBoard, player.NetId); lane++)
                if (catalog.QueryCreatureBoardLaneType(player.NetId, lane) == CardLaneType.Library
                    && catalog.GatherCardsInZone(delegate(ServerCard x) { return x.CurrentLaneIndex == lane && !x.IsDying(); }, player.NetId, GameZoneName.CreatureBoard).Count > 0)
                    return true;
            return false;
        }
    }
}
