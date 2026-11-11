using System;
using System.IO;
using System.Text;
using CcgRpc;
using SPTESL;

namespace OfflineEngine
{
    // Protocol tests use genuine installed protobuf classes, with no synthetic game rules.
    public static class EngineCommandTests
    {
        public static int Run(string outputDirectory)
        {
            StringBuilder report = new StringBuilder("test\tresult\tdetail\n");
            int failures = 0;
            Check(report, ref failures, "legal-attack", delegate { return Validate(Attack(), List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-stale-command", delegate { Decision d = Attack(); d.CommandUid--; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-unlisted-actor", delegate { Decision d = Attack(); d.ActorCardNetUid = 999; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-unlisted-attack-target", delegate { Decision d = Attack(); d.TargetCardNetUid = 999; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-unavailable-action", delegate { Decision d = Attack(); d.Type = CcgRpc.DecisionType.Activate; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-injected-attack-lane", delegate { Decision d = Attack(); d.LaneTarget = BoardLaneTarget.OtherRightLane; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-injected-attack-equipment", delegate { Decision d = Attack(); d.EquipCardNetUid = 8; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-negative-native-uid", delegate { Decision d = Attack(); d.TargetCardNetUid = -1; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "legal-untargeted-action", delegate { return Validate(Play(), List(CardCommand.PlayPositionless, 0)); });
            Check(report, ref failures, "reject-injected-target-untargeted-action", delegate { Decision d = Play(); d.TargetCardNetUid = 7; return !Validate(d, List(CardCommand.PlayPositionless, 0)); });
            Check(report, ref failures, "reject-injected-lane-untargeted-action", delegate { Decision d = Play(); d.LaneTarget = BoardLaneTarget.MyLeftLane; return !Validate(d, List(CardCommand.PlayPositionless, 0)); });
            Check(report, ref failures, "legal-creature-play-lane", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; return Validate(Play(), l); });
            Check(report, ref failures, "reject-unlisted-creature-play-lane", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; Decision d = Play(); d.LaneIndex = 1; return !Validate(d, l); });
            Check(report, ref failures, "reject-shift-wrapped-lane", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; Decision d = Play(); d.LaneIndex = 32; return !Validate(d, l); });
            Check(report, ref failures, "legal-nonzero-creature-position-syntax", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; Decision d = Play(); d.PositionIndex = 2; return Validate(d, l); });
            Check(report, ref failures, "reject-negative-creature-position", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; Decision d = Play(); d.PositionIndex = -1; return !Validate(d, l); });
            Check(report, ref failures, "reject-positionless-action-position", delegate { Decision d = Play(); d.PositionIndex = 1; return !Validate(d, List(CardCommand.PlayPositionless, 0)); });
            Check(report, ref failures, "reject-attack-insertion-position", delegate { Decision d = Attack(); d.PositionIndex = 1; return !Validate(d, List(CardCommand.Attack, 7)); });
            Check(report, ref failures, "reject-target-choice-insertion-position", delegate { Decision d = Play(); d.PositionIndex = 1; d.TargetCardNetUid = 7; return !Validate(d, List(CardCommand.ChooseTarget, 7)); });
            Check(report, ref failures, "legal-targeted-action", delegate { Decision d = Play(); d.TargetCardNetUid = 7; return Validate(d, List(CardCommand.PlayPositionless, 7)); });
            Check(report, ref failures, "reject-wrong-targeted-action-target", delegate { Decision d = Play(); d.TargetCardNetUid = 8; return !Validate(d, List(CardCommand.PlayPositionless, 7)); });
            Check(report, ref failures, "reject-missing-required-action-target", delegate { return !Validate(Play(), List(CardCommand.PlayPositionless, 7)); });
            Check(report, ref failures, "legal-lane-target-action", delegate { CardCommandList l = List(CardCommand.PlayToLane, 0); l.ValidCommands[0].LaneTargets.Add(BoardLaneTarget.OtherRightLane); Decision d = Play(); d.LaneTarget = BoardLaneTarget.OtherRightLane; return Validate(d, l); });
            Check(report, ref failures, "reject-double-card-and-lane-choice", delegate { CardCommandList l = List(CardCommand.ChooseTarget, 7); l.ValidCommands[0].LaneTargets.Add(BoardLaneTarget.OtherRightLane); Decision d = Play(); d.TargetCardNetUid = 7; d.LaneTarget = BoardLaneTarget.OtherRightLane; return !Validate(d, l); });
            Check(report, ref failures, "legal-either-card-or-lane-choice", delegate { CardCommandList l = List(CardCommand.ChooseTarget, 7); l.ValidCommands[0].LaneTargets.Add(BoardLaneTarget.OtherRightLane); Decision d = Play(); d.TargetCardNetUid = 7; return Validate(d, l); });
            Check(report, ref failures, "reject-discard-outside-full-lane", delegate { Decision d = Play(); d.DiscardCardNetUids.Add(99); return !Validate(d, List(CardCommand.PlayPositionless, 0)); });
            Check(report, ref failures, "reject-missing-full-lane-sacrifice", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; l.ValidCommands[0].FullBoardLanes = 1; l.FullLaneCardUids.Add(99); return !Validate(Play(), l); });
            Check(report, ref failures, "legal-listed-full-lane-sacrifice", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; l.ValidCommands[0].FullBoardLanes = 1; l.FullLaneCardUids.Add(99); Decision d = Play(); d.DiscardCardNetUids.Add(99); return Validate(d, l); });
            Check(report, ref failures, "reject-unlisted-full-lane-sacrifice", delegate { CardCommandList l = List(CardCommand.PlayToPosition, 0); l.ValidCommands[0].ValidBoardLanes = 1; l.ValidCommands[0].FullBoardLanes = 1; l.FullLaneCardUids.Add(99); Decision d = Play(); d.DiscardCardNetUids.Add(98); return !Validate(d, l); });
            Check(report, ref failures, "reject-required-choice-skipped", delegate { CardCommandList l = List(CardCommand.ChooseTarget, 7); l.MustSelectCommand = true; return !Validate(new Decision { CommandUid = 42, Type = CcgRpc.DecisionType.NoCommand }, l); });
            Check(report, ref failures, "reject-required-choice-injected-decline-target", delegate { CardCommandList l = List(CardCommand.DeclineToPlay, 0); l.MustSelectCommand = true; return !Validate(new Decision { CommandUid = 42, Type = CcgRpc.DecisionType.NoCommand, TargetCardNetUid = 8 }, l); });
            Check(report, ref failures, "legal-required-choice-explicit-decline", delegate { CardCommandList l = List(CardCommand.DeclineToPlay, 0); l.MustSelectCommand = true; return Validate(new Decision { CommandUid = 42, Type = CcgRpc.DecisionType.NoCommand }, l); });
            File.WriteAllText(Path.Combine(outputDirectory, "engine-command-test-results.tsv"), report.ToString());
            return failures;
        }

        private static bool Validate(Decision decision, CardCommandList list) { return CommandRestoration.ValidateDecision(decision, list); }
        private static Decision Play() { return new Decision { CommandUid = 42, ActorCardNetUid = 3, Type = CcgRpc.DecisionType.Play }; }
        private static Decision Attack() { return new Decision { CommandUid = 42, ActorCardNetUid = 3, TargetCardNetUid = 7, Type = CcgRpc.DecisionType.Attack }; }
        private static CardCommandList List(CardCommand mask, int target)
        {
            CardCommandList list = new CardCommandList { ListUid = 42 };
            CardCommandState card = new CardCommandState { CardNetUid = 3, ValidCardCommandMask = (int)mask };
            if (target != 0)
            {
                if (mask == CardCommand.Attack) card.AttackTargets.Add(target);
                else if (mask == CardCommand.ActiveAbility) card.ActiveAbilityTargets.Add(target);
                else card.PlayTargets.Add(target);
            }
            list.ValidCommands.Add(card);
            return list;
        }
        private static void Check(StringBuilder report, ref int failures, string name, Func<bool> body)
        {
            try { if (!body()) throw new InvalidOperationException("Validation returned an unexpected result."); report.AppendLine(name + "\tPASS\t"); }
            catch (Exception ex) { failures++; report.AppendLine(name + "\tFAIL\t" + ex.Message.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ')); }
        }
    }
}
