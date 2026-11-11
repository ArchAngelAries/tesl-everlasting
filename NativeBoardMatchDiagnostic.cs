using System;
using System.Collections;
using System.IO;
using CcgRpc;
using Pixelplacement;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Bounded automated match: human decisions use received native client commands
    // and original input/packet producers. This does not test a physical mouse.
    public static class NativeBoardMatchDiagnostic
    {
        public static IEnumerator Run(BoardConductor board, Simulation sim, string directory)
        {
            if (!OfflineBootstrapPlugin.Active || Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-match-diagnostic") < 0)
                throw new InvalidOperationException("Automated board match needs its explicit diagnostic flag.");
            string path = Path.Combine(directory, "board-match-diagnostic.tsv");
            File.WriteAllText(path, "field\tvalue\nstatus\tplaying\nfixture\t12-card Alit decks; bounded ordinary rules; reconstructed strategy; synthetic local input\n");
            sim._disableAiDecisions = false;
            float deadline = Time.realtimeSinceStartup + 240f;
            int actions = 0, attacks = 0, plays = 0, passes = 0, oldUid = -1;
            float nextSnapshot = 0;
            float nextAnimationReport = 0;
            while (sim._gameState.GamePhase != GamePhase.Results)
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Automated native board match exceeded 240 seconds.");
                if (sim._turnStateStack.Count == 0) throw new InvalidOperationException("Automated board match lost its turn stack.");
                TurnState turn = sim._turnStateStack.Peek();
                CardCommandList list = NativeBoardInputDiagnostic.ClientCommands(board);
                if (Time.realtimeSinceStartup >= nextSnapshot)
                {
                    string snapshot = "field\tvalue\nactive_player\t" + turn.ActivePlayerIndex + "\nphase\t" + sim._gameState.GamePhase
                        + "\nserver_uid\t" + (turn.CurrentCommandItem == null ? -1 : turn.CurrentCommandItem.CommandList.ListUid)
                        + "\nclient_uid\t" + (list == null ? -1 : list.ListUid) + "\nlast_sent_uid\t" + oldUid
                        + "\nserver_sequence\t" + (turn.CurrentSequence == null ? "none" : turn.CurrentSequence.ToString())
                        + "\nclient_sequence\t" + board.HasSequenceInProgress
                        + "\ninput_ready\t" + (list != null && NativeBoardInputDiagnostic.IsReady(board, list.ListUid))
                        + "\nlocal_actions\t" + actions + "\n";
                    File.WriteAllText(Path.Combine(directory, "board-match-state.tsv"), snapshot);
                    nextSnapshot = Time.realtimeSinceStartup + 5f;
                }
                if (turn.ActivePlayerIndex == 0 && turn.CurrentSequence == null && turn.CurrentCommandItem != null
                    && list != null && list.ListUid != oldUid && list.ListUid == turn.CurrentCommandItem.CommandList.ListUid
                    && !board.HasSequenceInProgress && NativeBoardInputDiagnostic.IsReady(board, list.ListUid))
                {
                    Decision decision = StrategicDecisionRestoration.ChooseDecision(sim, list);
                    Card3D actor = decision.ActorCardNetUid == 0 ? null : board.BoardState.FindCardByNetUid(decision.ActorCardNetUid);
                    Card3D target = decision.TargetCardNetUid == 0 ? null : board.BoardState.FindCardByNetUid(decision.TargetCardNetUid);
                    if ((actor != null && actor.Animating) || (target != null && target.Animating))
                    {
                        if (Time.realtimeSinceStartup >= nextAnimationReport)
                        {
                        File.WriteAllText(Path.Combine(directory, "board-match-animation.tsv"), "field\tvalue\ndecision\t" + decision.Type
                            + "\nactor_uid\t" + decision.ActorCardNetUid + "\nactor_zone\t" + (actor == null ? "absent" : actor.ClientCard.GameZoneName.ToString())
                            + "\nactor_animating\t" + (actor != null && actor.Animating) + "\ntarget_uid\t" + decision.TargetCardNetUid
                            + "\ntarget_animating\t" + (target != null && target.Animating) + "\n");
                        nextAnimationReport = Time.realtimeSinceStartup + 5f;
                        }
                        yield return null; continue;
                    }
                    switch (decision.Type)
                    {
                        case CcgRpc.DecisionType.NoCommand: NativeBoardInputDiagnostic.EndTurn(board); passes++; break;
                        case CcgRpc.DecisionType.UseRing:
                            board.GetComponent<BoardInputController>().OnButtonActionClicked(BoardInputController.InputCommandType.Action_UseRing); break;
                        case CcgRpc.DecisionType.Attack:
                            NativeBoardInputDiagnostic.AttackThroughDrag(board, decision.ActorCardNetUid, decision.TargetCardNetUid); attacks++; break;
                        case CcgRpc.DecisionType.Play:
                            if (decision.EquipCardNetUid != 0 || decision.TargetCardNetUid != 0 || decision.LaneTarget != BoardLaneTarget.None)
                                throw new NotSupportedException("Automated board fixture supports untargeted creature play only.");
                            if (decision.DiscardCardNetUids.Count == 1)
                                GameNetworkManager.Instance.PlayCardWithDiscard(list.ListUid, new CardNetUid(decision.ActorCardNetUid),
                                    new CardNetUid(decision.DiscardCardNetUids[0]), decision.LaneIndex, decision.PositionIndex);
                            else if (decision.DiscardCardNetUids.Count == 0)
                                NativeBoardInputDiagnostic.PlayThroughDrag(board, decision.ActorCardNetUid, decision.LaneIndex);
                            else throw new NotSupportedException("Multiple sacrifices are outside the bounded board fixture.");
                            plays++; break;
                        case CcgRpc.DecisionType.ToHand:
                            GameNetworkManager.Instance.PlayCardToHand(list.ListUid, new CardNetUid(decision.ActorCardNetUid)); break;
                        default: throw new NotSupportedException("Automated board decision is outside the bounded fixture: " + decision.Type);
                    }
                    oldUid = list.ListUid;
                    if (++actions > 100) throw new InvalidOperationException("Automated native board exceeded its action bound.");
                    Debug.Log("OFFLINE BOARD MATCH: local action " + actions + " " + decision.Type + "; uid=" + oldUid);
                }
                yield return null;
            }
            float resultDeadline = Time.realtimeSinceStartup + 30f;
            while (Singleton<SettingsManager>.Instance.GameCompletionType == SettingsData.EndGameType.None)
            {
                if (Time.realtimeSinceStartup > resultDeadline) throw new TimeoutException("Native client did not consume the terminal match history.");
                yield return null;
            }
            if (attacks == 0 || plays == 0 || passes == 0) throw new InvalidOperationException("Automated match did not exercise plays, attacks and alternating turns.");
            File.WriteAllText(path, "field\tvalue\nstatus\tPASS\nfixture\t12-card Alit decks; bounded ordinary rules; reconstructed strategy; synthetic local input\nserver_phase\t"
                + sim._gameState.GamePhase + "\nclient_result\t" + Singleton<SettingsManager>.Instance.GameCompletionType
                + "\nwinner_id\t" + sim.WinnerId.Id + "\nloser_id\t" + sim.LoserId.Id + "\nlocal_actions\t" + actions
                + "\nlocal_plays\t" + plays + "\nlocal_attacks\t" + attacks + "\nlocal_end_turns\t" + passes
                + "\nfirst_player_turns\t" + sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value
                + "\nsecond_player_turns\t" + sim.Players[1].QueryAttribute(AttributeType.TurnCount).Value
                + "\nfirst_player_runes_lost\t" + sim.Players[0].QueryAttribute(AttributeType.NumRunesLost).Value
                + "\nsecond_player_runes_lost\t" + sim.Players[1].QueryAttribute(AttributeType.NumRunesLost).Value + "\n");
            Debug.Log("OFFLINE BOARD MATCH COMPLETE: original native client consumed a terminal match result over loopback TCP.");
        }
    }
}
