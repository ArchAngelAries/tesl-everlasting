using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CcgRpc;
using HarmonyLib;
using SPTESL;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OfflineEngine
{
    // Explicit bounded board/transport diagnostic using the reversible ordinary
    // rules scope. Diagnostic decks do not establish full collection readiness.
    public static class OfflineBoardDiagnostic
    {
        public static IEnumerator Run(GameConfiguration config, string directory)
        {
            if (!OfflineBootstrapPlugin.Active || Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-diagnostic") < 0)
                throw new InvalidOperationException("Explicit offline board diagnostic flag is required before ordinary rule preparation.");
            Scenario scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.Id == OrdinaryMatchRules.DefaultScenarioId; });
            if (scenario == null) throw new InvalidOperationException("Native Default scenario is unavailable.");
            SinglePlayerFirstToAct originalFirst = scenario.StartingPlayer;
            using (IDisposable ordinary = OrdinaryMatchRules.Prepare(config))
            {
                try { yield return RunCore(config, directory); }
                finally { scenario.StartingPlayer = originalFirst; }
            }
        }

        private static IEnumerator RunCore(GameConfiguration config, string directory)
        {
            if (!OfflineBootstrapPlugin.Active || Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-diagnostic") < 0)
                throw new InvalidOperationException("Explicit offline board diagnostic flag is required.");
            File.WriteAllText(Path.Combine(directory, "board-diagnostic.tsv"), "field\tvalue\nstatus\tstarting\nfixture\tbounded reconstructed Default/base Avatar/field/shadow/ring rules; reviewed 12-card Alit decks; AI disabled\n");
            RuleImporter.Load(config, directory);
            Harmony harmony = new Harmony("local.tesl.offline.board.diagnostic");
            CatalogMovementRestoration.Install(harmony);
            CommandRestoration.Install(harmony);
            DecisionRestoration.Install(harmony);
            LifecycleRestoration.Install(harmony);
            OfflineBoardAdapter.Install(harmony);
            NativeBoardInputDiagnostic.Install(harmony);
            Scenario scenario = config.Scenarios.Find(delegate(Scenario s) { return s.ResourceId.UniqueName == "Default"; });
            if (scenario == null) throw new InvalidOperationException("Native Default scenario is unavailable.");
            if (!OrdinaryMatchRules.Active || scenario.AvatarOverrides == null || scenario.PlayerOverrides == null || config.LaneTriggers == null)
                throw new InvalidOperationException("The bounded ordinary rule scope was not prepared.");
            scenario.StartingPlayer = SinglePlayerFirstToAct.HumanPlayer; // Deterministic TEST transport fixture.
            Card avatar = config.FindCardById(OrdinaryMatchRules.BaseAvatarId), alit = null;
            foreach (CardSet set in config.CardCollection)
                foreach (Card card in set.Cards)
                {
                    if (card.Name == "Afflicted Alit" && RuleImporter.IsSupported(card.ResourceId.Id)) alit = card;
                }
            if (avatar == null || alit == null) throw new InvalidOperationException("Reviewed native diagnostic cards are unavailable.");
            Deck deck = new Deck { Name = "Offline board TEST fixture", AvatarCardId = avatar.ResourceId.Id };
            deck.SetCardCount(alit.ResourceId.Id, 12);
            GameManager.Instance.SelectedDeck = deck;
            GameManager.Instance.AiOpponentSelectedDeck = deck;
            GameNetworkManager.Instance.DebugScenarioId = scenario.ResourceId;
            harmony.Patch(AccessTools.Method(typeof(LockablePlayerDescriptions), "LoadPlayerDescription"), null,
                new HarmonyMethod(typeof(OfflineBoardDiagnostic), "DescribeLocalFixture"), null, null, null);
            yield return OfflineAudioAssets.InspectAndInstall(directory, true);
            yield return OfflineBoardAdapter.Prepare();
            yield return OfflineMusicPreparation.Prepare(directory);
            OfflineBoardAdapter.LaunchNativeDiagnostic();
            float deadline = Time.realtimeSinceStartup + 90f;
            BoardConductor board = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                board = UnityEngine.Object.FindObjectOfType<BoardConductor>();
                if (board != null && board.BoardState != null && board.BoardState.RejoinState.DidBoardLayoutInit) break;
                yield return null;
            }
            bool initialized = board != null && board.BoardState != null && board.BoardState.RejoinState.DidBoardLayoutInit;
            string evidence = "field\tvalue\nstatus\t" + (initialized ? "native-layout-initialized" : "layout-timeout")
                + "\nscene\t" + SceneManager.GetActiveScene().name + "\nboard_present\t" + (board != null)
                + "\nlocal_player_id\t" + GameNetworkManager.Instance.GetLocalPlayerNetId().Id
                + "\nfixture\tbounded reconstructed Default/base Avatar/field/shadow/ring rules; reviewed 12-card Alit decks; AI disabled\n";
            File.WriteAllText(Path.Combine(directory, "board-diagnostic.tsv"), evidence);
            Debug.Log("OFFLINE BOARD DIAGNOSTIC: " + evidence);
            if (!initialized) throw new TimeoutException("Native board did not initialize within 90 seconds; see scoped runtime log.");
            yield return new WaitForSeconds(12f);
            Simulation sim = (Simulation)AccessTools.Field(typeof(GameNetworkManager), "_embeddedMatchServer").GetValue(GameNetworkManager.Instance);
            if (sim == null || sim.Players.Count != 2 || !sim.Players[0].IsHuman) throw new InvalidOperationException("Deterministic native transport fixture differs.");
            yield return Wait(delegate { return sim._gameState.GamePhase == GamePhase.Mulligan; }, "Native mulligan did not arrive.");
            // Use the exact original UI producer and encrypted TCP path for the human. One card is marked for redraw through
            // the client's own toggle (owner 2026-11-06: the redrawn card never appeared in the hand).
            List<ServerCard> opening = sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, sim.Players[0].NetId, GameZoneName.Hand);
            ServerCard swapped = opening[0];
            yield return Wait(delegate { return board.BoardState.FindCardByNetUid(swapped.NetUid.Id) != null; }, "The opening hand card is not shown by the client.");
            board.BoardState.MatchUI.MulliganController.ToggleMulliganCard(board.BoardState.FindCardByNetUid(swapped.NetUid.Id), swapped.NetUid);
            board.BoardState.MatchUI.MulliganController.OnMulliganConfirmed();
            sim.ReportMulliganDecision(sim.Players[1], new MulliganResponse { Action = MulliganAction.Commit, MulliganCardNetworkUids = new List<int>() });
            yield return Wait(delegate { return Ready(sim, 0); }, "Native human mulligan packet did not reach the main turn.");
            List<int> drawnUids = new List<int>();
            foreach (ServerCard c in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, sim.Players[0].NetId, GameZoneName.Hand))
                if (!opening.Contains(c)) drawnUids.Add(c.NetUid.Id);
            Require(drawnUids.Count >= 1, "The server drew no replacement.");
            // the redraw animation runs about 4 s; then each new card must be a visible card in the client's hand
            float until = Time.realtimeSinceStartup + 20f;
            string handState = "";
            while (Time.realtimeSinceStartup < until)
            {
                handState = "";
                bool shown = true;
                foreach (int uid in drawnUids)
                {
                    Card3D c3 = board.BoardState.FindCardByNetUid(uid);
                    bool ok = c3 != null && c3.gameObject.activeInHierarchy && c3.ClientCard != null && c3.ClientCard.GameZoneName == GameZoneName.Hand;
                    handState += uid + (c3 == null ? ":absent " : ":" + (c3.ClientCard == null ? "noclient" : c3.ClientCard.GameZoneName.ToString()) + (c3.gameObject.activeInHierarchy ? "/visible " : "/hidden "));
                    if (!ok) shown = false;
                }
                if (shown) break;
                yield return new WaitForSeconds(0.5f);
            }
            Require(handState.IndexOf("absent", StringComparison.Ordinal) < 0 && handState.IndexOf("hidden", StringComparison.Ordinal) < 0 && handState.IndexOf("noclient", StringComparison.Ordinal) < 0
                && handState.IndexOf(":Hand", StringComparison.Ordinal) >= 0, "Redrawn card not shown in the client's hand: " + handState);
            Card3D old = board.BoardState.FindCardByNetUid(swapped.NetUid.Id);
            bool oldInHand = swapped.CurrentGameZoneName == GameZoneName.Hand;   // drawn again at turn start
            Require(oldInHand || old == null || old.ClientCard == null || old.ClientCard.GameZoneName != GameZoneName.Hand, "The returned card is still shown in the hand.");
            evidence += "native_mulligan_redraw_shown\tPASS\t" + handState.Trim() + "\n";
            Debug.Log("OFFLINE BOARD DIAGNOSTIC: mulligan redraw shown " + handState.Trim() + " (returned " + swapped.NetUid.Id + ", now " + swapped.CurrentGameZoneName + ")");
            yield return Wait(delegate { return NativeBoardInputDiagnostic.IsReady(board, sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid); }, "Native client first-turn commands/input readiness did not arrive.");
            NativeBoardInputDiagnostic.EndTurn(board);
            yield return Wait(delegate { return Ready(sim, 1); }, "Native human end-turn packet did not transfer control.");
            // Explicit deterministic opponent injection; this does not validate strategic AI.
            sim.PlayerServiceClients[1]._decisionMessages.Enqueue(new Decision { Type = CcgRpc.DecisionType.NoCommand,
                CommandUid = sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid });
            yield return Wait(delegate { return Ready(sim, 0); }, "Injected diagnostic opponent did not return control.");
            CardCommandList list = sim._turnStateStack.Peek().CurrentCommandItem.CommandList;
            yield return Wait(delegate { return NativeBoardInputDiagnostic.IsReady(board, list.ListUid); }, "Native client second-turn commands/input readiness did not arrive.");
            Decision play = DecisionRestoration.ChooseDecision(sim, list);
            if (play.Type != CcgRpc.DecisionType.Play) throw new InvalidOperationException("Reviewed second-turn creature play is absent.");
            ServerCard played = sim.CardCatalog.FindDynamicCard(new CardNetUid(play.ActorCardNetUid));
            int before = list.ListUid;
            // the card drawn at turn start may still be in its draw animation; a card animating past 20 s is stuck (a real fault)
            yield return Wait(delegate { Card3D c3 = board.BoardState.FindCardByNetUid(played.NetUid.Id); return c3 != null && !c3.Animating; },
                "The card to play (" + played.NetUid.Id + ") stayed in its animation for 20 s.");
            int selectedPosition = NativeBoardInputDiagnostic.PlayThroughDrag(board, played.NetUid.Id, 0);
            yield return Wait(delegate { return Ready(sim, 0) && sim._turnStateStack.Peek().CurrentCommandItem.CommandList.ListUid != before
                && played.CurrentGameZoneName == GameZoneName.CreatureBoard; }, "Native human creature packet did not settle.");
            yield return new WaitForSeconds(6f);
            evidence += "native_human_mulligan\tPASS\nnative_human_end_turn\tPASS\nnative_human_play\tPASS\nnative_client_command_readiness\tPASS\nnative_mulligan_ui_handler\tPASS\nnative_end_turn_button_handler\tPASS\nnative_synthetic_pointer_drag\tPASS\nnative_pointer_kind\tdiagnostic raw samples; physical OS mouse untested\nselected_native_position\t" + selectedPosition + "\nplayed_card_uid\t" + played.NetUid.Id
                + "\nclient_played_card_zone\t" + (board.BoardState.FindGameZoneWithCard(played.NetUid.Id) == null ? "absent" : board.BoardState.FindGameZoneWithCard(played.NetUid.Id).GetType().Name) + "\n";
            File.WriteAllText(Path.Combine(directory, "board-diagnostic.tsv"), evidence);
            CaptureBoard(directory);
            OfflineAudioAssets.WriteMusicState(Path.Combine(directory, "audio-music-after-board.tsv"));
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-board-match-diagnostic") >= 0)
            {
                yield return NativeBoardMatchDiagnostic.Run(board, sim, directory);
                CaptureBoard(directory, "board-match-complete.png");
            }
            Debug.Log("OFFLINE BOARD DIAGNOSTIC COMPLETE: original human packet producers passed mulligan/end-turn/play; deterministic opponent only.");
            Application.Quit();
        }

        private static bool Ready(Simulation sim, int player)
        {
            if (sim._gameState.GamePhase != GamePhase.MainLoop || sim._turnStateStack.Count != 1) return false;
            TurnState turn = sim._turnStateStack.Peek();
            return turn.ActivePlayerIndex == player && turn.CurrentSequence == null && turn.CurrentCommandItem != null
                && turn.CurrentCommandItem.CommandList != null && turn.EffectTriggerQueues.Active.Count == 0
                && turn.EffectTriggerQueues.Pending.Count == 0 && turn.EffectTriggerQueues.After.Count == 0;
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

        private static IEnumerator Wait(Func<bool> done, string message)
        {
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!done())
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException(message);
                yield return null;
            }
        }

        private static void CaptureBoard(string directory, string fileName = "board-diagnostic.png")
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            // Batch mode cannot capture the swapchain. Render the original active
            // scene cameras into a local image, retaining their scene composition.
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24);
            RenderTexture previous = RenderTexture.active;
            Texture2D snapshot = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                Camera[] cameras = Camera.allCameras;
                Array.Sort(cameras, delegate(Camera a, Camera b) { return a.depth.CompareTo(b.depth); });
                foreach (Camera camera in cameras)
                {
                    if (camera.targetTexture != null) continue;
                    RenderTexture old = camera.targetTexture;
                    try { camera.targetTexture = target; camera.Render(); }
                    finally { camera.targetTexture = old; }
                }
                RenderTexture.active = target;
                snapshot.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                snapshot.Apply();
                File.WriteAllBytes(Path.Combine(directory, fileName), snapshot.EncodeToPNG());
            }
            finally { RenderTexture.active = previous; UnityEngine.Object.Destroy(snapshot); RenderTexture.ReleaseTemporary(target); }
        }

        private static void DescribeLocalFixture(PlayerType __0, PlayerDescription __result)
        {
            __result.PlayerName = __0 == PlayerType.Normal ? "Offline Player" : "Diagnostic Opponent";
            __result.AiOpponentId = UniqueResourceId.GetInvalid().Id;
        }
    }
}
