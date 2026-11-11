using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SPTESL;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OfflineEngine
{
    // Hidden rendered verification of an offline story chapter (explicit flag only):
    //   --tesl-offline-play --tesl-offline-story-diagnostic=<1..3>
    // Launches the chapter through the same launcher path as the player-facing
    // buttons. It captures frames (overlay UI included) whenever the visible text
    // changes, dumps that text, and presses only the scenario's own OK/Continue
    // dialog buttons so instructions advance. It never plays cards or ends turns.
    public static class OfflineStoryDiagnostic
    {
        private const string Flag = "--tesl-offline-story-diagnostic=";

        public static int RequestedChapter()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
                if (arg.StartsWith(Flag, StringComparison.Ordinal))
                {
                    int value;
                    if (!int.TryParse(arg.Substring(Flag.Length), out value) || value < 1 || value > TutorialScenarioRules.Chapters.Length)
                        throw new ArgumentException("Story diagnostic chapter must be 1.." + TutorialScenarioRules.Chapters.Length);
                    return value;
                }
            return 0;
        }

        public static IEnumerator Run(OfflinePracticeSession session, string directory, int number)
        {
            TutorialScenarioRules.Chapter chapter = TutorialScenarioRules.Chapters[number - 1];
            string prefix = Path.Combine(directory, "story-ch" + number + "-");
            StringBuilder texts = new StringBuilder("step\tseconds\tkind\ttext\n");
            StringBuilder presses = new StringBuilder("seconds\tbutton\n");
            float start = Time.realtimeSinceStartup;
            session.StartChapterForDiagnostic(chapter);
            // Optional: reconstructed strategy plays the human seat (server side) so
            // off-script trades/deaths occur; the client keeps rendering as player.
            bool autoplay = Array.IndexOf(Environment.GetCommandLineArgs(), "--tesl-offline-story-autoplay") >= 0;
            StringBuilder parity = new StringBuilder("seconds\tserver_phase\tturns\tstatus\tserver_board\tclient_board\tturn_player\tclient_floating\n");
            string lastSignature = null, lastMismatch = null; int step = 0, pressed = 0, mismatches = 0, persistent = 0;
            float limit = autoplay ? 480f : 240f;
            while (Time.realtimeSinceStartup - start < limit && step < 60)
            {
                yield return new WaitForSeconds(autoplay ? 2f : 3f);
                Simulation sim = OfflinePracticeSession.LastSimulation;
                // Paced: the human seat is released to the strategy for a moment only
                // while the client board already matches the server (client caught up).
                PlayerState humanSeat = null;
                if (sim != null) foreach (PlayerState player in sim.Players) if (player.PlayerType == (int)PlayerType.Normal) humanSeat = player;
                if (autoplay && humanSeat != null) humanSeat.IsHuman = true;
                if (sim != null && sim.Players.Count == 2 && sim._gameState.GamePhase == GamePhase.MainLoop)
                {
                    string server = ServerBoard(sim), client = ClientBoard();
                    bool same = server == client;
                    if (!same && client != null && lastMismatch == server + "|" + client)
                    {
                        persistent++;
                        if (persistent == 1) { yield return new WaitForEndOfFrame(); Capture(prefix + "parity-mismatch-" + mismatches.ToString("00") + ".png"); mismatches++; }
                    }
                    else if (same) persistent = 0;
                    lastMismatch = same ? null : server + "|" + client;
                    if (autoplay && same && humanSeat != null)
                    {
                        humanSeat.IsHuman = false;
                        yield return new WaitForSeconds(0.25f);
                        humanSeat.IsHuman = true;
                    }
                    int turns = sim.Players[0].QueryAttribute(AttributeType.TurnCount).Value + sim.Players[1].QueryAttribute(AttributeType.TurnCount).Value;
                    parity.Append((Time.realtimeSinceStartup - start).ToString("0")).Append('\t').Append(sim._gameState.GamePhase).Append('\t').Append(turns)
                        .Append('\t').Append(same ? "match" : persistent > 0 ? "MISMATCH" : "settling").Append('\t').Append(server).Append('\t').Append(client ?? "no-board")
                        .Append('\t').Append(sim._turnStateStack.Count > 0 ? sim.Players[sim._turnStateStack.Peek().TurnPlayerIndex].NetId.Id.ToString() : "")
                        .Append('\t').AppendLine(ClientFloating());
                }
                List<string> visible = VisibleTexts();
                string signature = string.Join("|", visible.ToArray());
                if (signature != lastSignature)
                {
                    lastSignature = signature; step++;
                    float seconds = Time.realtimeSinceStartup - start;
                    foreach (string line in visible) texts.Append(step).Append('\t').Append(seconds.ToString("0")).Append("\ttext\t").AppendLine(line);
                    yield return new WaitForEndOfFrame();
                    Capture(prefix + "step" + step.ToString("00") + ".png");
                }
                foreach (Button button in UnityEngine.Object.FindObjectsOfType<Button>())
                {
                    if (!button.isActiveAndEnabled || !button.interactable) continue;
                    string label = Label(button);
                    if (label != "OK" && label != "CONTINUE") continue;
                    presses.Append((Time.realtimeSinceStartup - start).ToString("0")).Append('\t').AppendLine(label);
                    button.onClick.Invoke(); pressed++;
                    break;
                }
                if (sim != null && sim._gameState.GamePhase == GamePhase.Results) break;
            }
            Simulation last = OfflinePracticeSession.LastSimulation;
            StringBuilder summary = new StringBuilder("field\tvalue\n");
            summary.AppendLine("chapter\t" + chapter.Name).AppendLine("captures\t" + step).AppendLine("dialog_presses\t" + pressed)
                .AppendLine("graphics\t" + SystemInfo.graphicsDeviceType).AppendLine("screen\t" + Screen.width + "x" + Screen.height)
                .AppendLine("rules_scope\t" + (TutorialScenarioRules.Active ? "tutorial" : "other"));
            if (last != null)
            {
                summary.AppendLine("server_scenario\t" + last._scenarioId).AppendLine("server_phase\t" + last._gameState.GamePhase);
                foreach (PlayerState player in last.Players)
                    summary.AppendLine("player_" + player.NetId.Id + "\thuman=" + player.IsHuman + " turns=" + player.QueryAttribute(AttributeType.TurnCount).Value
                        + " runes=" + player.QueryAttribute(AttributeType.NumRunes).Value + " ring=" + player.QueryAttribute(AttributeType.RingUsesLeft).Value);
            }
            summary.AppendLine("autoplay\t" + autoplay).AppendLine("persistent_board_mismatches\t" + mismatches);
            File.WriteAllText(prefix + "parity.tsv", parity.ToString());
            File.WriteAllText(prefix + "texts.tsv", texts.ToString());
            File.WriteAllText(prefix + "presses.tsv", presses.ToString());
            File.WriteAllText(prefix + "summary.tsv", summary.ToString());
            Debug.Log("OFFLINE STORY DIAGNOSTIC COMPLETE: " + chapter.Name + " captures=" + step + " presses=" + pressed);
            // The native SettingsManager intercepts Application.Quit with a confirmation
            // popup during a match, which would leave this hidden run alive. Results are
            // already written, so end only this diagnostic process.
            yield return new WaitForSeconds(1f);
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }

        // Creature boards as "owner:netUid=cardId@lane/health" sorted, server truth vs client views.
        internal static string ServerBoard(Simulation sim)
        {
            List<string> rows = new List<string>();
            foreach (PlayerState player in sim.Players)
                foreach (ServerCard card in sim.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, player.NetId, GameZoneName.CreatureBoard))
                    rows.Add(player.NetId.Id + ":" + card.NetUid.Id + "=" + card.CardId + "/" + card.CurrentHealth);
            rows.Sort(StringComparer.Ordinal);
            return string.Join(",", rows.ToArray());
        }

        internal static string ClientBoard()
        { OfflineGraphics.Mark("diag-board"); return ClientBoardInner(); }
        private static string ClientBoardInner()
        {
            BoardConductor board = UnityEngine.Object.FindObjectOfType<BoardConductor>();
            if (board == null || board.BoardState == null) return null;
            List<string> rows = new List<string>();
            foreach (Card3D view in BoardState.GatherAllCards(board.BoardState))
            {
                if (view == null || view.ClientCard == null || view.ClientCard.GameZoneName != GameZoneName.CreatureBoard) continue;
                rows.Add(view.ClientCard.OwnerNetId.Id + ":" + view.ClientCard.NetUid.Id + "=" + view.ClientCard.CardId + "/" + view.ClientCard.CurrentHealth);
            }
            rows.Sort(StringComparer.Ordinal);
            return string.Join(",", rows.ToArray());
        }

        // Client cards currently floating ("ready"): CardFullView raises its collider to y=2.
        private static string ClientFloating()
        {
            BoardConductor board = UnityEngine.Object.FindObjectOfType<BoardConductor>();
            if (board == null || board.BoardState == null) return "";
            List<string> rows = new List<string>();
            foreach (Card3D view in BoardState.GatherAllCards(board.BoardState))
            {
                if (view == null || view.ClientCard == null || view.ClientCard.GameZoneName != GameZoneName.CreatureBoard) continue;
                foreach (CardFullView full in view.GetComponentsInChildren<CardFullView>(true))
                {
                    BoxCollider box = full.GetComponent<BoxCollider>();
                    if (box != null && box.center.y > 1f) { rows.Add(view.ClientCard.NetUid.Id.ToString()); break; }
                }
            }
            rows.Sort(StringComparer.Ordinal);
            return string.Join(",", rows.ToArray());
        }

        internal static string Label(Component button)
        {
            TMP_Text tmp = button.GetComponentInChildren<TMP_Text>();
            if (tmp != null) return (tmp.text ?? "").Trim().ToUpperInvariant();
            Text text = button.GetComponentInChildren<Text>();
            return text == null ? "" : (text.text ?? "").Trim().ToUpperInvariant();
        }

        internal static List<string> VisibleTexts()
        { OfflineGraphics.Mark("diag-texts"); return VisibleTextsInner(); }
        private static List<string> VisibleTextsInner()
        {
            List<string> result = new List<string>();
            foreach (TMP_Text text in UnityEngine.Object.FindObjectsOfType<TMP_Text>())
                if (text.isActiveAndEnabled && !string.IsNullOrEmpty(text.text) && text.text.Trim().Length != 0)
                    result.Add(text.text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim());
            foreach (Text text in UnityEngine.Object.FindObjectsOfType<Text>())
                if (text.isActiveAndEnabled && !string.IsNullOrEmpty(text.text) && text.text.Trim().Length != 0)
                    result.Add(text.text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim());
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        // Batch mode has no swapchain: render scene cameras into a texture, with
        // overlay canvases temporarily re-targeted to the top camera, then restored.
        internal static void Capture(string path)
        { OfflineGraphics.Mark("diag-capture"); CaptureInner(path); }
        private static void CaptureInner(string path)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            // A visible window has a real back buffer: save exactly what is on screen (end of this frame).
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") < 0) { ScreenCapture.CaptureScreenshot(path); return; }
            int width = Screen.width > 0 ? Screen.width : 1280, height = Screen.height > 0 ? Screen.height : 720;
            Camera[] cameras = Camera.allCameras;
            Array.Sort(cameras, delegate(Camera a, Camera b) { return a.depth.CompareTo(b.depth); });
            Camera top = null;
            foreach (Camera camera in cameras) if (camera.targetTexture == null) top = camera;
            List<Canvas> moved = new List<Canvas>();
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previous = RenderTexture.active;
            Texture2D snapshot = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                if (top != null)
                    foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
                        if (canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay && canvas.isActiveAndEnabled)
                        {
                            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = top;
                            canvas.planeDistance = top.nearClipPlane + 0.05f; moved.Add(canvas);
                        }
                Canvas.ForceUpdateCanvases();
                foreach (Camera camera in cameras)
                {
                    if (camera.targetTexture != null) continue;
                    try { camera.targetTexture = target; camera.Render(); }
                    finally { camera.targetTexture = null; }
                }
                RenderTexture.active = target;
                snapshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                snapshot.Apply();
                File.WriteAllBytes(path, snapshot.EncodeToPNG());
            }
            finally
            {
                foreach (Canvas canvas in moved) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
                RenderTexture.active = previous; UnityEngine.Object.Destroy(snapshot); RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
