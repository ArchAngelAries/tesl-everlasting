using System;
using System.Collections.Generic;
using SPTESL;

namespace OfflineEngine
{
    // Reconstructed absent per-card Last Gasp / Slay / Pilfer wrappers from exact installed text, not recovered server graphs.
    // The triggers themselves are fired by intact native code: DecisionRestoration.DiscardCards raises LastGaspTrigger after the
    // native Deathrattle move; native CardActions.Damage.StaticExecuteSingleTarget raises Slay (dying creature on the acting
    // card owner's turn, or SlayOnBothTurns) and Pilfer (damage to an opposing avatar). Effects use native Damage, Heal, Buff,
    // DrawCards, ModifyMana and DamageAvatarOfPlayer. Veteran is NOT included: no surviving client code raises it.
    public static class OriginalKeywordTriggerRules
    {
        public enum Kind { OpponentDamageHeal, Heal, OpponentDamage, GainMagicka, LaneEnemyDamage, LaneAllDamage, MaxMagickaDraw, Draw, SelfBuff, MaxMagicka }
        private sealed class Entry
        {
            public int Id, A, B; public EffectTriggerType Trigger; public Kind Kind; public string Hash;
            public Entry(int id, EffectTriggerType trigger, Kind kind, int a, int b, string hash) { Id = id; Trigger = trigger; Kind = kind; A = a; B = b; Hash = hash; }
        }
        private const EffectTriggerType LastGasp = EffectTriggerType.LastGaspTrigger, Slay = EffectTriggerType.Slay, Pilfer = EffectTriggerType.Pilfer;
        private static readonly Entry[] entries = new Entry[] {
            new Entry(-1206609355, LastGasp, Kind.OpponentDamageHeal, 3, 3, "0B14FCF06627FC83156A63D306679D047816A5F66870D5541857509E892F7B4B"), // House Kinsman
            new Entry(1931741259, LastGasp, Kind.Heal, 5, 0, "1FD898C993F7C2E08ECA2FD4C6A2F7C055E927C4BF5B10F842B25272F7E20E06"), // Restless Templar
            new Entry(964579944, LastGasp, Kind.OpponentDamage, 5, 0, "83CC8868BC00B3B86066A9D71EE9E528478D6360D810EF96E1610F2265E0467D"), // Sower of Revenge (Guard)
            new Entry(1323193054, LastGasp, Kind.GainMagicka, 2, 0, "45C3850D5FA9B24EBA4F569CE86DEC1F7BC8B7A93AF73AA182497B3EC8A7F40D"), // Moon Sugar Smuggler
            new Entry(878764871, LastGasp, Kind.LaneEnemyDamage, 6, 0, "C93C71797B761ADBB5929A7F886EE139C46206A756C7857A3CAAC905746D41C2"), // Indoril Archmage (Guard)
            new Entry(-1085593596, LastGasp, Kind.LaneAllDamage, 1, 0, "7DF5CEB87AB9DA37872FA3D10021AA50B896543046FCC5F9C840E0F11F9FA3F5"), // Firepot Spider
            new Entry(85837010, LastGasp, Kind.MaxMagickaDraw, 7, 0, "C5AB0ECF653AED8D8520913110963EB518DC3C5F488041903D9798D389498C38"), // Sly Marshblade
            new Entry(-296994425, Slay, Kind.Draw, 1, 0, "D8FE0D60A1F46F32D5D6A5822E1152083F491889FCD9FC308AB048D772F2549C"), // Pahmar-raht Renegade
            new Entry(-306091386, Slay, Kind.Heal, 3, 0, "F28C5158C4B1CAEFD40705CF67ACD0F5BD19F06446837E5F0378716D2E607253"), // Emperor's Blade (Guard)
            new Entry(-1414651238, Slay, Kind.SelfBuff, 1, 1, "02E7F0161603D5EA13C1D7D4B4D65D49A9AA3558622742CB2985361D53D6C6B2"), // Stalking Shadowscale
            new Entry(-819919458, Slay, Kind.SelfBuff, 1, 1, "4E167D4D922C1BCFFAA18E1A976EBDFBC128AD0A94F7610C1195C30CD2AA8F67"), // Bleakcoast Troll
            new Entry(1153555094, Slay, Kind.MaxMagicka, 1, 0, "08D560CC5443C6A22DAA68BC7631F2D6CB11AC8B2787CF6427DC11748991347C"), // Archein Venomtongue (Lethal)
            new Entry(988424426, Pilfer, Kind.SelfBuff, 3, 0, "7C7D354CC23B9BC92C8A6A063E2A32541F8B0F2180F6BBF6B2F467E99196B4EE"), // Baandari Bruiser
            new Entry(-1572966262, Pilfer, Kind.SelfBuff, 1, 1, "CD576FBF9A2EB09FF3B71FA2D54FD36F6531C755D6D210F17C6906D91D604AEC"), // Daring Cutpurse (Prophecy)
            new Entry(-6629593, Pilfer, Kind.SelfBuff, 4, 4, "09EC45458B11120DB8B026C02FF1AD4AD041B946FF53DBDA8B14AF5EEAE5FD2A"), // Quin'rawl Burglar (Drain)
            new Entry(1137969808, Pilfer, Kind.SelfBuff, 4, 4, "DDE72CBDD1F79967E022ED55F267FD8DF07FB91AD482E23560DEC6B1BE8FF5DA"), // Quin'rawl Skulker
            new Entry(-756429718, Pilfer, Kind.Draw, 1, 0, "9981E686C84A8FAFB2CE13B6D421335FFB07D92A8278E90D9CBC2062E74E6AF2"), // Rajhini Highwayman
            new Entry(233969208, Pilfer, Kind.GainMagicka, 2, 0, "FED4400EFDFEAC7DE77B66A2460A642584B4B0ABBD1C7B6744DBD7F28D2CFB4B"), // Torval Crook (Charge)
            new Entry(-210215501, Pilfer, Kind.GainMagicka, 1, 0, "1E3A47819701A43CBC6B918B7D3A3B52BC3423391E32C69A670E15F35EDB3B36"), // Palace Prowler (Drain)
        };
        private static readonly Dictionary<Card, TriggeredEffect> owned = new Dictionary<Card, TriggeredEffect>();

        public static int Count { get { return entries.Length; } }
        public static bool IsOriginalId(int id) { return Find(id) != null; }
        public static Kind KindOf(int id) { return Get(id).Kind; }
        public static EffectTriggerType TriggerOf(int id) { return Get(id).Trigger; }
        public static int ValueA(int id) { return Get(id).A; }
        public static int ValueB(int id) { return Get(id).B; }
        private static Entry Get(int id) { Entry e = Find(id); if (e == null) throw new NotSupportedException("Unaudited original keyword-trigger ID: " + id); return e; }
        private static Entry Find(int id) { foreach (Entry e in entries) if (e.Id == id) return e; return null; }

        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).

        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }


        private static List<Card> ValidateRules(GameConfiguration config)
        {
            if (config == null) throw new ArgumentNullException("config");
            List<Card> result = new List<Card>();
            foreach (Entry e in entries)
            {
                Card card = config.FindCardById(e.Id);
                if (card == null || OriginalCardRules.Fingerprint(card) != e.Hash)
                    throw new NotSupportedException("Original keyword-trigger metadata differs: " + e.Id);
                TriggeredEffect trigger;
                if (owned.TryGetValue(card, out trigger))
                {
                    bool valid = (card.Auras == null || card.Auras.Count == 0) && (card.ActionPlayInformation == null || card.ActionPlayInformation.Count == 0)
                        && (card.PlayRequirements == null || card.PlayRequirements.PlayerRequirements.Count == 0)
                        && (card.BetrayRequirements == null || card.BetrayRequirements.PlayerRequirements.Count == 0)
                        && (card.ActivatedAbility == null || (card.ActivatedAbility.AbilityGroups.Count == 0 && card.ActivatedAbility.TargetSelectors.Count == 0
                        && card.ActivatedAbility.ActivationCardRequirements.Count == 0 && card.ActivatedAbility.ActivationPlayerRequirements.Count == 0))
                        && card.TriggeredEffects != null && card.TriggeredEffects.Count == 1 && card.TriggeredEffects[0] == trigger
                        && OriginalGraphDescriber.Trigger(trigger) == OriginalGraphDescriber.Trigger(Create(e));
                    if (!valid) throw new NotSupportedException("Owned original keyword-trigger graph changed or gained extra behavior: " + e.Id);
                }
                else OriginalCardRules.ValidateNoExtraRules(card);
                result.Add(card);
            }
            return result;
        }

        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards = Validate(config); // Atomic complete inventory preflight before mutations.
            List<int> ids = new List<int>();
            for (int i = 0; i < cards.Count; i++)
            {
                Card card = cards[i];
                if (!owned.ContainsKey(card))
                {
                    TriggeredEffect trigger = Create(entries[i]);
                    card.TriggeredEffects = new List<TriggeredEffect> { trigger };
                    owned.Add(card, trigger);
                }
                if (card.ActionPlayInformation == null) card.ActionPlayInformation = new List<PlayCardAbility>();
                if (card.Auras == null) card.Auras = new List<Aura>();
                if (card.PlayRequirements == null) card.PlayRequirements = new PlayCardRequirements();
                if (card.BetrayRequirements == null) card.BetrayRequirements = new PlayCardRequirements();
                if (card.ActivatedAbility == null) card.ActivatedAbility = new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }

        private static TriggeredEffect Create(Entry e)
        {
            TriggeredEffect trigger = new TriggeredEffect { TriggerType = e.Trigger, ClientTargetingType = PlayTargetingType.NoTarget, EffectGroup = new EffectGroup() };
            List<Effect> effects = trigger.EffectGroup.Effects;
            switch (e.Kind)
            {
                case Kind.OpponentDamageHeal: effects.Add(Player(OpponentDamage(e.A))); effects.Add(Card(OwnAvatarHeal(e.B))); break;
                case Kind.Heal: effects.Add(Card(OwnAvatarHeal(e.A))); break;
                case Kind.OpponentDamage: effects.Add(Player(OpponentDamage(e.A))); break;
                case Kind.GainMagicka:
                    // Native ModifyMana changes current magicka only; turn-start SetMana discards it, i.e. "this turn".
                    PlayerEffectAction mana = new PlayerEffectAction { Action = new PlayerActions.ModifyMana { Amount = new ActionValueData(e.A) } };
                    mana.Selectors.Add(Role(PlayerRole.Self)); effects.Add(Player(mana)); break;
                case Kind.LaneEnemyDamage: effects.Add(Card(LaneDamage(e.A, true))); break;
                case Kind.LaneAllDamage: effects.Add(Card(LaneDamage(e.A, false))); break;
                case Kind.MaxMagickaDraw:
                    Effect draw = Player(Draw());
                    PlayerRequirements.PlayerCountOfCardsRequirement max = new PlayerRequirements.PlayerCountOfCardsRequirement();
                    max.LhsPlayer = PlayerRole.Self;
                    max.Conditional.Count_Is = PlayerConditionals.CountOfCards.ComparisonType.GreaterThanOrEqualTo;
                    max.Conditional.Value = 1;
                    max.Conditional.Selectors.Add(Subtype(CardSubtype.Avatar)); max.Conditional.Selectors.Add(Board()); max.Conditional.Selectors.Add(Friends());
                    max.Conditional.Selectors.Add(new CardConditionals.AttributeValue { AttributeName = AttributeType.MaxMana,
                        Is = CardConditionals.AttributeValue.ComparisonType.GreaterThanOrEqualTo, Value = new ConditionalValueData(e.A) });
                    draw.ExecutePlayerRequirements.Add(max); effects.Add(draw); break;
                case Kind.Draw: effects.Add(Player(Draw())); break;
                case Kind.SelfBuff:
                    CardEffectAction buff = new CardEffectAction { HashId = -1, Action = new CardActions.Buff { Enchantment = Stats(AttributeType.Attack, e.A, AttributeType.MaxHealth, e.B) } };
                    buff.Selectors.Add(new CardConditionals.CardRoleType { Check = CardConditionals.CardRoleType.Requirement.Is, Role = CardRole.Self });
                    effects.Add(Card(buff)); break;
                case Kind.MaxMagicka:
                    CardEffectAction ramp = new CardEffectAction { HashId = -1, Action = new CardActions.Buff { Enchantment = Stats(AttributeType.MaxMana, e.A, AttributeType.None, 0) } };
                    ramp.Selectors.Add(Subtype(CardSubtype.Avatar)); ramp.Selectors.Add(Board()); ramp.Selectors.Add(Friends());
                    effects.Add(Card(ramp)); break;
            }
            return trigger;
        }

        private static CardEffectAction LaneDamage(int amount, bool enemiesOnly)
        {
            // Self is already in Deathrattle when Last Gasp resolves; native InSameLane.UseBoardLaneIndex compares the
            // retained BoardLaneIndex, which native ServerCard keeps from the creature's last board lane.
            CardEffectAction action = new CardEffectAction { HashId = -1, Action = new CardActions.Damage { HealthLoss = new ActionValueData(amount), DamageSource = CardRole.Self } };
            action.Selectors.Add(Subtype(CardSubtype.Creature)); action.Selectors.Add(Board());
            if (enemiesOnly) action.Selectors.Add(new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Enemies });
            action.Selectors.Add(new CardConditionals.InSameLane { Same = CardConditionals.InSameLane.Requirement.Is, OtherCard = CardRole.Self, UseBoardLaneIndex = true });
            return action;
        }
        private static PlayerEffectAction OpponentDamage(int amount)
        {
            PlayerEffectAction damage = new PlayerEffectAction { Action = new PlayerActions.DamageAvatarOfPlayer { Amount = new ActionValueData(amount), AssetHash = -1 } };
            damage.Selectors.Add(Role(PlayerRole.Opponent)); return damage;
        }
        private static CardEffectAction OwnAvatarHeal(int amount)
        {
            CardEffectAction heal = new CardEffectAction { HashId = -1, Action = new CardActions.Heal { HealthGain = new ActionValueData(amount), FullHeal = false, HealToAVDValue = false } };
            heal.Selectors.Add(Subtype(CardSubtype.Avatar)); heal.Selectors.Add(Board()); heal.Selectors.Add(Friends()); return heal;
        }
        private static PlayerEffectAction Draw()
        {
            PlayerEffectAction draw = new PlayerEffectAction { Action = new PlayerActions.DrawCards { NumCards = new ActionValueData(1), DrawFrom = PlayerActions.DrawCards.DrawDeck.Self,
                OperationType = ServerCardCatalog.MoveOperationType.DefaultDraw, StopAtHandLimit = false, IgnoreDeckingRule = false } };
            draw.Selectors.Add(Role(PlayerRole.Self)); return draw;
        }
        private static SPTESL.Enchantment Stats(AttributeType first, int a, AttributeType second, int b)
        {
            SPTESL.Enchantment enchantment = new SPTESL.Enchantment();
            if (a != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, first, a));
            if (b != 0) enchantment.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, second, b));
            return enchantment;
        }
        private static Effect Card(CardEffectAction action) { Effect effect = new Effect(); effect.CardActions.Add(action); return effect; }
        private static Effect Player(PlayerEffectAction action) { Effect effect = new Effect(); effect.PlayerActions.Add(action); return effect; }
        private static PlayerConditionals.PlayerRoleType Role(PlayerRole role)
        { return new PlayerConditionals.PlayerRoleType { Check = PlayerConditionals.PlayerRoleType.Requirement.Is, Role = role }; }
        private static CardConditionals.Subtype Subtype(CardSubtype subtype)
        { return new CardConditionals.Subtype { Check = CardConditionals.Subtype.Requirement.Is, SubtypeName = subtype }; }
        private static CardConditionals.Zone Board()
        { return new CardConditionals.Zone { Check = CardConditionals.Zone.Requirement.IsIn, ZoneTypeMask = new GameZoneTypeMask { Bits = GameZoneType.Board } }; }
        private static CardConditionals.Relationship Friends()
        { return new CardConditionals.Relationship { OtherCard = CardRole.Self, Are = CardConditionals.Relationship.RelationshipType.Friends }; }
    }
}
