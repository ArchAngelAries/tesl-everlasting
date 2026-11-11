using System;
using System.Collections.Generic;
using System.Reflection;
using CcgRpc;
using Google.Protobuf;
using HarmonyLib;
using SPTESL;

namespace OfflineEngine
{
    public static class EngineMulliganTests
    {
        private static bool captureRequested;
        private static Decision captured;

        // Supply a fresh native fixture stopped in Mulligan with AI decisions
        // disabled. Calls the installed UI producer, capturing its outbound
        // packet before transport; the packet then passes through protobuf.
        public static void AssertNativeProtocol(Simulation sim)
        {
            Require(sim._gameState.GamePhase == GamePhase.Mulligan && sim._disableAiDecisions,
                "Mulligan protocol test needs a fresh fixture paused at Mulligan.");
            PlayerState one = sim.Players[0], two = sim.Players[1];
            List<ServerCard> hand = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard card) { return true; }, one.NetId, GameZoneName.Hand);
            List<ServerCard> other = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard card) { return true; }, two.NetId, GameZoneName.Hand);
            Require(hand.Count >= 2 && other.Count > 0, "Native opening hands are unavailable.");
            Harmony probe = new Harmony("local.tesl.offline.tests.mulliganproducer");
            MethodInfo send = AccessTools.Method(typeof(GameNetworkManager), "SendDecision");
            probe.Patch(send, new HarmonyMethod(typeof(EngineMulliganTests).GetMethod("CapturePrefix", BindingFlags.Static | BindingFlags.Public)), null, null, null, null);
            try
            {
                Decision first = Produce(new int[] { hand[0].NetUid.Id }, false);
                Require(first.CommandUid == 0 && first.ActorCardNetUid == 0 && first.DiscardCardNetUids.Count == 1,
                    "Installed producer does not match recovered zero-command/full-list contract.");
                Require(LifecycleRestoration.TryApplyMulliganDecision(sim, one, first), "Native single selection was rejected.");
                RequireSelection(sim, one, new int[] { hand[0].NetUid.Id });
                Decision full = Produce(new int[] { hand[0].NetUid.Id, hand[1].NetUid.Id }, false);
                Require(LifecycleRestoration.TryApplyMulliganDecision(sim, one, full), "Native full selection was rejected.");
                RequireSelection(sim, one, new int[] { hand[0].NetUid.Id, hand[1].NetUid.Id });

                Decision bad = first.Clone(); bad.DiscardCardNetUids.Add(hand[0].NetUid.Id);
                Reject(sim, one, bad, "duplicate UID");
                bad = first.Clone(); bad.DiscardCardNetUids.Add(other[0].NetUid.Id);
                Reject(sim, one, bad, "opponent hand UID");
                bad = first.Clone(); bad.DiscardCardNetUids.Add(Int32.MaxValue);
                Reject(sim, one, bad, "unknown UID after a valid UID");
                bad = first.Clone(); bad.DiscardCardNetUids[0] = 0;
                Reject(sim, one, bad, "zero UID");
                bad = first.Clone(); bad.CommandUid = one.LastSentCommandItem.CommandList.ListUid;
                Require(bad.CommandUid != 0, "Native command list unexpectedly uses UID zero.");
                Reject(sim, one, bad, "command-list UID instead of native mulligan sentinel");
                bad = first.Clone(); bad.ActorCardNetUid = hand[0].NetUid.Id;
                Reject(sim, one, bad, "actor-toggle payload");
                bad = first.Clone(); bad.Type = CcgRpc.DecisionType.Play;
                Reject(sim, one, bad, "play during mulligan");
                bad = first.Clone(); bad.PositionIndex = 1;
                Reject(sim, one, bad, "unused position field");
                bad = first.Clone(); bad.DiscardCardNetUids.Clear();
                for (int n = 0; n <= hand.Count; n++) bad.DiscardCardNetUids.Add(hand[0].NetUid.Id);
                Reject(sim, one, bad, "selection exceeding hand count");
                GamePhase phase = sim._gameState.GamePhase;
                sim._gameState.GamePhase = GamePhase.MainLoop;
                try { Reject(sim, one, first, "late mulligan packet outside phase"); }
                finally { sim._gameState.GamePhase = phase; }

                Require(LifecycleRestoration.TryApplyMulliganDecision(sim, one, Produce(new int[0], false)), "Native clear selection was rejected.");
                RequireSelection(sim, one, new int[0]);
                int replacedUid = hand[1].NetUid.Id;
                Decision commit = Produce(new int[] { replacedUid }, true);
                Require(LifecycleRestoration.TryApplyMulliganDecision(sim, one, commit), "Native final commit list was rejected.");
                Require(one.MulliganState == PlayerMulliganState.Mulligan_Complete
                    && sim._gameState.MulliganPlayerResponses[one.NetId.Id].Action == MulliganAction.Commit
                    && sim.CardCatalog.FindCardCount(GameZoneName.Hand, one.NetId) == hand.Count
                    && hand[1].CurrentGameZoneName == GameZoneName.Deck && hand[0].CurrentGameZoneName == GameZoneName.Hand,
                    "Native commit did not replace exactly its final selected card.");
                Reject(sim, one, commit, "replayed final commit");
                Require(two.MulliganState == PlayerMulliganState.Mulligan_Draw,
                    "One player's commit mutated the opponent's mulligan state.");
                Require(LifecycleRestoration.TryApplyMulliganDecision(sim, two, Produce(new int[0], true)), "Native keep-all commit was rejected.");
                Require(sim.IsAllPlayerMulliganDecisionsMade(), "Both native commits did not settle responses.");
            }
            finally
            {
                captureRequested = false; captured = null;
                probe.Unpatch(send, HarmonyPatchType.Prefix, probe.Id);
            }
        }

        private static Decision Produce(int[] ids, bool commit)
        {
            List<CardNetUid> list = new List<CardNetUid>();
            foreach (int id in ids) list.Add(new CardNetUid(id));
            captured = null; captureRequested = true;
            try { GameNetworkManager.Instance.SendMulliganDecision(list, commit); }
            finally { captureRequested = false; }
            Require(captured != null, "Native SendMulliganDecision did not reach captured SendDecision.");
            return Decision.Parser.ParseFrom(captured.ToByteArray());
        }

        public static bool CapturePrefix(Decision __0)
        {
            if (!captureRequested) return true;
            captured = __0.Clone();
            return false;
        }

        private static void Reject(Simulation sim, PlayerState player, Decision decision, string reason)
        {
            MulliganResponse before = sim._gameState.MulliganPlayerResponses[player.NetId.Id];
            List<int> selected = before.MulliganCardNetworkUids == null ? new List<int>() : new List<int>(before.MulliganCardNetworkUids);
            int hand = sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId);
            Require(!LifecycleRestoration.TryApplyMulliganDecision(sim, player, decision), "Accepted invalid mulligan: " + reason);
            RequireSelection(sim, player, selected.ToArray());
            Require(sim._gameState.MulliganPlayerResponses[player.NetId.Id].Action == before.Action
                && sim.CardCatalog.FindCardCount(GameZoneName.Hand, player.NetId) == hand,
                "Invalid mulligan partially mutated state: " + reason);
        }

        private static void RequireSelection(Simulation sim, PlayerState player, int[] ids)
        {
            List<int> list = sim._gameState.MulliganPlayerResponses[player.NetId.Id].MulliganCardNetworkUids;
            Require((list == null ? 0 : list.Count) == ids.Length, "Mulligan selection count differs.");
            for (int n = 0; n < ids.Length; n++) Require(list[n] == ids[n], "Mulligan selection ordering differs.");
        }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
