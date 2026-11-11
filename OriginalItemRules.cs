using System;
using System.Collections.Generic;
using SPTESL;
namespace OfflineEngine
{
    // Exact stat-only originals use intact native SetItemTarget/Equip enchantments.
    public static class OriginalItemRules
    {
        private static readonly Dictionary<int,string> originals = new Dictionary<int,string> {
            {521751635, "A47B2A0EEA4614F9B0B5E2EBAAA92554D0C64AA44F15BAFFEC348ACE3519EBD5"}, // Dreugh Shell Armor
            {-2134829026, "0C8D82024A266940AA7473538618D1E3D593F2F068290A0488913C6443B674AE"}, // Dwarven Armaments
            {844499977, "CD73D431A9912F3259DF1A86C7B8FA323BAE2B89C96F18DBB5C8C6B01F4C0691"}, // Heavy Battleaxe
            {235363863, "E429DC98C5CAD18D3BA135FFCB185050428C9AEC4BD78A0138BCCC6279FF46B5"}, // Imperial Armor
            {-1808851552, "E0B9058FBA5DA2082B75F82E8B83A234A452AF47A132B0C5CFD3ECBD442C556F"}, // Iron Sword
            {598565408, "08017620EDFD2C689C03DFF70A983850858837A5448E9820E6DE0BAC76CEF894"}, // Maple Shield
            {394534272, "8F190A479ACD849B24864EFFD3E9E902F1C879C3F19EDA732C1956668E2A6C6A"}, // Maple Shield
            {939372786, "10F1351F4DCCC6C825B802828E9F9CB506DD7A6ABA8CE87D3EC9D68D8D112E96"}, // Steel Dagger
            {342030232, "73A60E70C75CEC334F472521CCA61C9D9E2C111B4796B5D4201843E9CB3639D0"}, // Steel Scimitar
            {-1126089104, "71A2EAD8FB1F4D172F17AE492F0F5C57D87220B88E94E0E25556B0DC734B1616"}, // Steel Sword
            {-2101722782, "FD4C4E1F12AAE81D58EE131CDC0D69B4DA3B06333E3C6151501B2A760FE85DE6"}, // Trusty Sword
            {-984005078, "4C974F286BA05CD598D24D7A3CBD6D2FB03E22CC5760EA78E560741C7D5D1387"}, // Cloak
            {-1835182663, "0708FCE396056C3397965DD77B501BA12F0D045EB3B3236FEDB06C81CEEF0FA6"}, // Dagger
            {-888772598, "A8450034AB4DED25A6EEB523B376765EE93AE2B1DA7AE7E00ABEA9E9E23343D1"}, // Horned Helm
        };
        public static bool IsOriginalId(int id) { return originals.ContainsKey(id); }
        // The exact-shape check runs on the rules as built (OriginalFxOverlay copies card FX in afterwards).
        public static List<Card> Validate(GameConfiguration config) { using (OriginalFxOverlay.Hide()) return ValidateRules(config); }

        private static List<Card> ValidateRules(GameConfiguration config)
        {
            CardDefinition definition=config.CardDefinitions.Find(delegate(CardDefinition d){return d.Name=="Item";});
            if(definition==null || !definition.Subtypes.Contains(CardSubtype.Item)
                || definition.ZoneLookupBoard!=GameZoneName.ItemBoard || definition.ZoneLookupCemetery!=GameZoneName.Cemetery)
                throw new NotSupportedException("Original Item definition differs.");
            List<Card> cards=new List<Card>();
            foreach(KeyValuePair<int,string> pair in originals)
            {
                Card card=config.FindCardById(pair.Key);
                if(card==null || OriginalCardRules.Fingerprint(card)!=pair.Value)
                    throw new NotSupportedException("Original stat-only item metadata differs: "+pair.Key);
                OriginalCardRules.ValidateNoExtraRules(card); cards.Add(card);
            }
            return cards;
        }
        public static int[] Restore(GameConfiguration config)
        {
            List<Card> cards=Validate(config); List<int> ids=new List<int>();
            foreach(Card card in cards)
            {
                if(card.TriggeredEffects==null)card.TriggeredEffects=new List<TriggeredEffect>();
                if(card.Auras==null)card.Auras=new List<Aura>();
                if(card.ActionPlayInformation==null)card.ActionPlayInformation=new List<PlayCardAbility>();
                if(card.PlayRequirements==null)card.PlayRequirements=new PlayCardRequirements();
                if(card.BetrayRequirements==null)card.BetrayRequirements=new PlayCardRequirements();
                if(card.ActivatedAbility==null)card.ActivatedAbility=new ActivatedAbility();
                ids.Add(card.ResourceId.Id);
            }
            return ids.ToArray();
        }
    }
}
