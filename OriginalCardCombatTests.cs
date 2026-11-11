using System;
using System.Collections.Generic;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Native original cards and algorithms. No keyword/combat statistic is fabricated.
    // Run with native generic definitions retained and OriginalCardRules integrated.
    public static class OriginalCardCombatTests
    {
        public static void Run(GameConfiguration config, Action<string, Action> test)
        {
            test("original-220-card-metadata-and-atomic-rule-preflight", delegate { OriginalCardRuleTests.AssertOriginalInventory(config); });
            test("original-charge-and-sleep-native-prerequisites", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard charge = f.CreateId(195169233, GameZoneName.CreatureBoard, 0, f.One); // Nord Firebrand 1/1 Charge
                ServerCard ordinary = f.CreateId(620729069, GameZoneName.CreatureBoard, 0, f.One); // Enraged Mudcrab 2/1
                Require(charge.QueryAttribute(AttributeType.Charge).Value == 1
                    && CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, charge), "Original Charge card remained asleep.");
                Require(!CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, ordinary), "Original ordinary creature bypassed sleep.");
            });
            test("original-guard-native-command-targets", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard attacker = f.CreateId(794235553, GameZoneName.CreatureBoard, 0, f.One); // original Crocodile 3/2
                attacker.OnStartTurn(f.One.NetId, f.State);
                ServerCard guard = f.CreateId(-1410566212, GameZoneName.CreatureBoard, 0, f.Two); // Hive Defender 3/6 Guard
                ServerCard ordinary = f.CreateId(620729069, GameZoneName.CreatureBoard, 0, f.Two);
                CardCommandList commands = f.Catalog.GenerateCardCommandItem(f.One, f.Two, f.Players).CommandList;
                CardCommandState attack = null;
                foreach (CardCommandState command in commands.ValidCommands)
                    if (command.CardNetUid == attacker.NetUid.Id) { attack = command; break; }
                Require(guard.QueryAttribute(AttributeType.Taunt).Value == 1 && attack != null && attack.AttackTargets.Contains(guard.NetUid.Id)
                    && !attack.AttackTargets.Contains(ordinary.NetUid.Id) && !attack.AttackTargets.Contains(f.AvatarTwo.NetUid.Id),
                    "Original Guard was absent from native commands or allowed bypass attacks.");
            });
            test("original-lethal-native-fight", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard attacker = f.CreateId(-171448078, GameZoneName.CreatureBoard, 0, f.One); // Deadly Draugr 1/1 Lethal
                ServerCard target = f.CreateId(1427457192, GameZoneName.CreatureBoard, 0, f.Two); // Swamp Leviathan 8/8
                Fight(f, attacker, target);
                Require(target.IsDying(), "Native original Lethal did not kill an 8-health creature after one damage.");
            });
            test("original-ward-native-fight", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard attacker = f.CreateId(620729069, GameZoneName.CreatureBoard, 0, f.One); // 2/1
                ServerCard target = f.CreateId(1479393784, GameZoneName.CreatureBoard, 0, f.Two); // Wind Keep Spellsword 2/2 Ward
                Fight(f, attacker, target);
                Require(target.CurrentHealth == 2 && target.QueryAttribute(AttributeType.Ward).Value == 0 && attacker.IsDying(),
                    "Native original Ward failed to absorb damage, expire or retain the normal counterattack.");
            });
            test("original-drain-native-fight", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                f.AvatarOne.SetAvatarHealthPreGame(25); // Wounded player fixture; original Giant Bat remains unchanged.
                ServerCard attacker = f.CreateId(40142447, GameZoneName.CreatureBoard, 0, f.One); // Giant Bat 2/2 Charge Drain
                Fight(f, attacker, f.AvatarTwo);
                Require(f.AvatarOne.CurrentHealth == 27 && f.AvatarTwo.CurrentHealth == 28,
                    "Native original Drain did not heal its owner for the damage actually dealt.");
            });
            test("original-breakthrough-native-fight", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard attacker = f.CreateId(1226173086, GameZoneName.CreatureBoard, 0, f.One); // Young Mammoth 4/4 Breakthrough
                ServerCard target = f.CreateId(620729069, GameZoneName.CreatureBoard, 0, f.Two); // Mudcrab 2/1
                Fight(f, attacker, target);
                Require(target.IsDying() && attacker.CurrentHealth == 2 && f.AvatarTwo.CurrentHealth == 27,
                    "Native original Breakthrough damage/counterattack differed from installed 4/4 versus 2/1.");
            });
            test("original-regenerate-shared-native-owner-turn", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard troll = f.CreateId(-1376524751, GameZoneName.CreatureBoard, 0, f.One); // Lowland Troll 4/8 Regenerate
                ServerCard target = f.CreateId(620729069, GameZoneName.CreatureBoard, 0, f.Two);
                Fight(f, troll, target);
                f.DrainTriggers();
                Require(troll.CurrentHealth == 6, "Original Troll fixture did not take its native two counterattack damage.");
                ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, f.Two));
                f.DrainTriggers();
                Require(troll.CurrentHealth == 6, "Original Regenerate healed at the opponent turn.");
                ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenPlayerTurnStarts, f.One));
                f.DrainTriggers();
                Require(troll.CurrentHealth == 8, "Original shared Regenerate graph did not restore full native health on owner turn.");
            });
            test("original-rally-shared-native-hand-buff", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard attacker = f.CreateId(-877465244, GameZoneName.CreatureBoard, 0, f.One); // Ald Velothi Assassin 1/2 Lethal Rally 1
                ServerCard hand = f.CreateId(620729069, GameZoneName.Hand, 0, f.One); // Only allied hand creature, original 2/1
                ServerEffectUtilities.TriggerEffectEvent(f.State, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenCardPreparesAttack, attacker, f.AvatarTwo, f.Players));
                f.DrainTriggers();
                Require(attacker.QueryAttribute(AttributeType.Rally).Value == 1 && hand.QueryAttribute(AttributeType.Attack).Value == 3
                    && hand.QueryAttribute(AttributeType.MaxHealth).Value == 2 && hand.CurrentHealth == 2,
                    "Original shared PrepareRally/Rally graph did not buff the only native hand creature by +1/+1.");
                Require(attacker.QueryAttribute(AttributeType.Attack).Value == 1 && attacker.CurrentHealth == 2,
                    "Original Rally changed its source creature's native statistics.");
            });
            test("original-snowy-sabre-cat-two-attacks-and-native-turn-reset", delegate {
                EngineTests.Fixture f = new EngineTests.Fixture(config);
                ServerCard cat = f.CreateId(OriginalSpecialCreatureRules.SnowySabreCatId, GameZoneName.CreatureBoard, 0, f.One);
                Require(cat.QueryAttribute(AttributeType.ManaCost).Value == 5 && cat.QueryAttribute(AttributeType.Attack).Value == 3
                    && cat.CurrentHealth == 7 && cat.QueryAttribute(AttributeType.NumAttacksPerTurn).Value == 2,
                    "Original Snowy Sabre Cat 5/3/7/two-attack statistics differ.");
                Require(!CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, cat), "Original Snowy bypassed sleep.");
                cat.OnStartTurn(f.One.NetId, f.State);
                Require(!cat.HasConsumedAllActionsForThisTurn() && CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, cat),
                    "Awake original Snowy lacks its first native attack.");
                Fight(f, cat, f.AvatarTwo); cat.IncrementActionCount();
                Require(!cat.HasConsumedAllActionsForThisTurn() && CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, cat)
                    && f.AvatarTwo.CurrentHealth == 27, "Original Snowy's first attack consumed both native actions.");
                Fight(f, cat, f.AvatarTwo); cat.IncrementActionCount();
                Require(cat.HasConsumedAllActionsForThisTurn() && !CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, cat)
                    && f.AvatarTwo.CurrentHealth == 24, "Original Snowy failed its second attack or allowed a third.");
                cat.OnEndTurn(f.One.NetId, f.State);
                Require(!cat.HasConsumedAllActionsForThisTurn(), "Native end-turn did not clear original Snowy's consumed actions.");
                cat.OnStartTurn(f.One.NetId, f.State);
                Require(!cat.HasConsumedAllActionsForThisTurn() && CardCommandUtilities.AttackCardPrerequisitesMet(f.Catalog, f.One, f.Two, cat),
                    "Original Snowy's two native attacks were unavailable after native end/start turn progression.");
                OriginalSpecialCreatureRules.Validate(config);
            });
        }

        private static void Fight(EngineTests.Fixture f, ServerCard attacker, ServerCard target)
        { CardActions.Fight.StaticExecute(f.State, attacker, new List<ServerCard> { target }, -1); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
