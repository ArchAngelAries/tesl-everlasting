using System;
using System.Collections.Generic;
using CcgRpc;
using SparkleServices;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Offline-only cheats (user request 2026-11-03): currency, unlock all cards and decks, and in-match cheats.
    // Meta cheats change the local player state and refresh the client's own Profile/Inventory fetches.
    // Match cheats run only while the human is being asked for a main-phase decision with no effect pending; they use
    // native actions inside a history event, then regenerate the human's command list and send both through the native
    // Simulation.SendSequenceHistoryToClients, so the client board, hand and playable highlights follow the change.
    public static class OfflineCheats
    {
        // ---- meta ------------------------------------------------------------------------------------
        public static void AddCurrency(OfflinePlayerStateData d, int gold, int gems, int tickets)
        {
            d.gold = Math.Max(0, d.gold + gold); d.gems = Math.Max(0, d.gems + gems); d.tickets = Math.Max(0, d.tickets + tickets);
        }

        // Every collectible card type up to its playset (installed MaxCopies, 1 for a unique card). Returns copies added.
        public static int UnlockAllCards(OfflinePlayerStateData d)
        {
            int added = 0;
            foreach (Inventory.CardTypeLoadable type in OfflineInventoryService.CatalogTypes)
            {
                int missing = type.maxCopies - d.TotalCopies(type.typeHash);
                if (missing <= 0) continue;
                d.AddCards(type.typeHash, missing); added += missing;
            }
            return added;
        }

        // Every installed premade deck not owned yet (its cards join the collection). Returns decks granted.
        public static int UnlockAllDecks(OfflinePlayerStateData d)
        {
            int granted = 0;
            foreach (DeckInfo info in OfflineInventoryService.AllDecks)
            {
                if (info == null || info.Cards == null || info.Cards.Count == 0) continue;
                if (d.decks.Exists(delegate(Inventory.Deck x) { return x.deckTypeHash == info.TypeNameHash; })) continue;
                if (OfflineInventoryService.GrantDeck(d, info)) granted++;
            }
            return granted;
        }

        public static void RefreshClient()
        {
            ServicesManager services = Pixelplacement.Singleton<ServicesManager>.Instance;
            if (services == null) return;
            if (services.Profile != null) services.Profile.Refresh();
            if (services.Inventory != null) services.Inventory.Refresh();
        }

        // ---- match -----------------------------------------------------------------------------------
        public enum MatchCheat { Magicka, MaxMagicka, Draw, Heal, Win, ReturnFromDiscard, BeastForm, HealOpponent, RestoreRune, RestoreOpponentRune }

        // The human is being asked for an ordinary main-phase decision and nothing is resolving.
        public static string Unavailable(Simulation s)
        {
            if (s == null || s._gameState == null || s._turnStateStack == null || s._turnStateStack.Count == 0) return "no offline match running";
            if (s._gameState.GamePhase != GamePhase.MainLoop) return "the match is not in progress";
            TurnState t = s._turnStateStack.Peek();
            PlayerState active = s.Players[t.ActivePlayerIndex];
            if (active.PlayerType != (int)PlayerType.Normal) return "wait for your turn";
            if (t.Phase != TurnPhase.PlayCards) return "wait until you can play cards";
            ServerEffectUtilities.TriggerQueues q = t.EffectTriggerQueues;
            if (q.Active.Count > 0 || q.Pending.Count > 0 || q.After.Count > 0 || q.CardDrawInterrupt.Count > 0) return "wait for effects to finish";
            if (t.CurrentCommandItem == null) return "wait for your decision prompt";
            return null;
        }

        public static string Apply(Simulation s, MatchCheat cheat) { return Apply(s, cheat, -1); }

        // The human's discard pile, oldest first (the menu's picker for ReturnFromDiscard).
        public static List<ServerCard> DiscardPile(Simulation s)
        {
            if (s == null || s._turnStateStack == null || s._turnStateStack.Count == 0) return new List<ServerCard>();
            PlayerState me = s.Players[s._turnStateStack.Peek().ActivePlayerIndex];
            return s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, me.NetId, GameZoneName.Cemetery);
        }

        public const int HandLimit = 10;

        // pick: index into DiscardPile for ReturnFromDiscard (-1 = the most recent card).
        public static string Apply(Simulation s, MatchCheat cheat, int pick)
        {
            string why = Unavailable(s);
            if (why != null) return why;
            TurnState t = s._turnStateStack.Peek();
            PlayerState me = s.Players[t.ActivePlayerIndex], foe = s.Players[1 - t.ActivePlayerIndex];
            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, t);
            ServerCard avatar = Avatar(s, me), foeAvatar = Avatar(s, foe);
            ServerCard back = null;
            if (cheat == MatchCheat.ReturnFromDiscard)
            {
                List<ServerCard> pile = DiscardPile(s);
                if (pile.Count == 0) return "your discard pile is empty";
                if (s.CardCatalog.GatherCardsInZone(delegate(ServerCard c) { return true; }, me.NetId, GameZoneName.Hand).Count >= HandLimit) return "your hand is full";
                back = pile[pick < 0 || pick >= pile.Count ? pile.Count - 1 : pick];
            }
            if (cheat == MatchCheat.RestoreRune && me.QueryAttribute(AttributeType.NumRunes).Value >= me.QueryAttribute(AttributeType.NumStartingRunes).Value) return "all your runes are intact";
            if (cheat == MatchCheat.RestoreOpponentRune && foe.QueryAttribute(AttributeType.NumRunes).Value >= foe.QueryAttribute(AttributeType.NumStartingRunes).Value) return "all the opponent's runes are intact";
            s.HistoryLog.StartEvent(HistoryType.EventTrigger, me.NetId, CardNetUid.GetInvalid(), CardNetUid.GetInvalid(), -1);
            try
            {
                switch (cheat)
                {
                    case MatchCheat.Magicka: PlayerActions.SetMana.StaticExecute(gs, me, me.QueryAttribute(AttributeType.Mana).Value + 5); break;
                    case MatchCheat.MaxMagicka:
                        // The native Buff card action (as OriginalMagickaConditionRules' max-magicka cards use): a bare
                        // AddMaxManaBuff attaches the enchantment but the queried MaxMana stays cached.
                        if (avatar != null)
                        {
                            SPTESL.Enchantment e = new SPTESL.Enchantment();
                            e.AttributeModifications.Add(new EnchantmentAttributeModification(EnchantmentAttributeModificationType.Add, AttributeType.MaxMana, 1));
                            new CardActions.Buff { Enchantment = e }.Execute(new CardActionArguments(gs, new List<ServerCard> { avatar },
                                new CardConditionalArguments(s.CardCatalog, avatar, null, null, null, null, LaneIdentifier.GetInvalid()),
                                new PlayerConditionalArguments(s.CardCatalog, me, null, null, null, foe), new TriggerConditionalArguments(0, AttributeType.None), -1, true));
                        }
                        PlayerActions.SetMana.StaticExecute(gs, me, me.QueryAttribute(AttributeType.Mana).Value + 1);
                        break;
                    case MatchCheat.Draw: PlayerActions.DrawCards.StaticExecute(gs, me, 1, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1); break;
                    case MatchCheat.Heal: if (avatar != null) CardActions.Heal.StaticExecute(gs, avatar, new List<ServerCard> { avatar }, 10, -1, false, false); break;
                    case MatchCheat.Win: if (foeAvatar != null) PlayerActions.DamageAvatarOfPlayer.StaticExecute(gs, null, foe, foeAvatar.CurrentHealth + 1, -1); break;
                    // a native move from the discard pile to the hand (counts as a draw, as the game's own discard-to-hand cards do)
                    case MatchCheat.ReturnFromDiscard: CardActions.ReturnToHand.StaticExecute(gs, avatar, back, me, false, false, false, -1, false, false); break;
                    // the event an enemy rune break raises: your werewolves change form (enemy ones do not)
                    case MatchCheat.BeastForm: if (foeAvatar != null) ServerEffectUtilities.TriggerEffectEvent(gs, new ServerEffectUtilities.TriggerEvent(EffectTriggerType.WhenBeastFormChange, foeAvatar, foeAvatar, s.Players)); break;
                    case MatchCheat.HealOpponent: if (foeAvatar != null) CardActions.Heal.StaticExecute(gs, foeAvatar, new List<ServerCard> { foeAvatar }, 10, -1, false, false); break;
                    case MatchCheat.RestoreRune: RestoreRune(gs, me, avatar); break;
                    case MatchCheat.RestoreOpponentRune: RestoreRune(gs, foe, foeAvatar); break;
                }
            }
            finally { s.HistoryLog.EndEvent(); }
            DecisionRestoration.ProcessDiscards(s);
            if (cheat != MatchCheat.Win)
            {
                OfflineDoubleCards.SplitHands(s, t);
                t.CurrentCommandItem = s.CardCatalog.GenerateCardCommandItem(me, foe, s.Players);
            }
            s.SendSequenceHistoryToClients();
            Debug.Log("OFFLINE CHEATS: " + cheat + " applied.");
            return null;
        }

        // Heal the player just above the last broken rune (at least 5), then restore it with the native HealRunes: a rune
        // restored below its line would break again on the next hit and draw another Prophecy card.
        private static void RestoreRune(ServerEffectUtilities.TriggerGameState gs, PlayerState p, ServerCard avatar)
        {
            if (avatar == null) return;
            int runes = p.QueryAttribute(AttributeType.NumRunes).Value, size = p.QueryAttribute(AttributeType.RuneHealth).Value;
            int heal = Math.Max(5, (runes + 1) * size + 1 - avatar.CurrentHealth);
            CardActions.Heal.StaticExecute(gs, avatar, new List<ServerCard> { avatar }, heal, -1, false, false);
            PlayerActions.HealRunes.StaticExecute(gs, new List<PlayerState> { p }, 1, false);
        }

        // Menu status text.
        public static string Done(MatchCheat cheat)
        {
            switch (cheat)
            {
                case MatchCheat.ReturnFromDiscard: return "Card returned to your hand.";
                case MatchCheat.BeastForm: return "Beast Form triggered.";
                case MatchCheat.HealOpponent: return "Opponent healed 10.";
                case MatchCheat.RestoreRune: return "Rune restored.";
                case MatchCheat.RestoreOpponentRune: return "Opponent's rune restored.";
                default: return cheat + " applied.";
            }
        }

        private static ServerCard Avatar(Simulation s, PlayerState p)
        {
            return s.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, p.NetId, GameZoneName.AvatarBoard);
        }
    }

    // F10 toggles the offline cheat menu (native-offline mode only).
    public sealed class OfflineCheatMenu : MonoBehaviour
    {
        private static OfflinePlayerState state;
        private bool open;
        private string status = "";
        private Rect window = new Rect(40, 60, 360, 680);
        private Vector2 scroll;
        private int discardPick = -1;

        public static void Install(MonoBehaviour host, OfflinePlayerState playerState)
        {
            state = playerState;
            OfflineMenuInputGuard.Install();
            if (host.gameObject.GetComponent<OfflineCheatMenu>() == null) host.gameObject.AddComponent<OfflineCheatMenu>();
        }

        private bool storiesForGold, freeDecks;
        private void Update()
        {
            OfflineMenuInputGuard.Track(open, window);   // clicks on the menu do not reach the board or buttons behind it
            if (!Input.GetKeyDown(KeyCode.F10)) return;
            open = !open;
            if (open && state != null) { OfflinePlayerStateData s = state.Snapshot(); storiesForGold = s.storiesForGold; freeDecks = s.freeDeckBuilding; }
        }

        private void OnGUI()
        {
            if (!open) return;
            GUI.depth = -20000;
            window = GUI.Window(0x7E51, window, Draw, "TESL: Everlasting - offline (F10)");
        }

        private void Draw(int id)
        {
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Offline only. Changes your local profile.");
            GUILayout.Label("Currency");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1000 gold")) Meta(delegate(OfflinePlayerStateData d) { OfflineCheats.AddCurrency(d, 1000, 0, 0); return "Added 1000 gold."; });
            if (GUILayout.Button("+500 gems")) Meta(delegate(OfflinePlayerStateData d) { OfflineCheats.AddCurrency(d, 0, 500, 0); return "Added 500 soul gems."; });
            if (GUILayout.Button("+5 tickets")) Meta(delegate(OfflinePlayerStateData d) { OfflineCheats.AddCurrency(d, 0, 0, 5); return "Added 5 arena tickets."; });
            GUILayout.EndHorizontal();
            GUILayout.Label("Collection");
            if (GUILayout.Button("Unlock all cards (full playsets)")) Meta(delegate(OfflinePlayerStateData d) { return "Added " + OfflineCheats.UnlockAllCards(d) + " card copies."; });
            if (GUILayout.Button("Unlock all premade decks")) Meta(delegate(OfflinePlayerStateData d) { return "Granted " + OfflineCheats.UnlockAllDecks(d) + " decks."; });
            if (GUILayout.Button(freeDecks ? "Free deck building: ON (any number of copies; turn off)" : "Free deck building: OFF (allow any number of copies of a card)"))
            {
                bool turnOn = !freeDecks;
                Meta(delegate(OfflinePlayerStateData d) { d.freeDeckBuilding = turnOn;
                    return turnOn ? "Free deck building on: add as many copies as you like (decks up to 100 cards; offline only)."
                                  : "Free deck building off: the usual copy limits apply when you edit a deck."; });
                freeDecks = turnOn;
            }
            GUILayout.Label("Stories");
            if (GUILayout.Button(storiesForGold ? "Stories cost gold: ON (make all free again)" : "Stories are free: make unplayed acts cost gold"))
            {
                bool turnOn = !storiesForGold;
                Meta(false, delegate(OfflinePlayerStateData d) {
                    int n = OfflineGoldStore.SetStoriesForGold(d, turnOn);
                    return turnOn ? n + " unplayed acts now in the store (Stories tab)." : n + " acts owned again."; });
                storiesForGold = turnOn;
                OfflineGoldStore.OnMainThread(OfflineGoldStore.RefreshClient);
            }
            OfflineGraphics.DrawOptions();
            if (OfflineLaunchMode.RebornInstalled()) { if (GUILayout.Button("Switch to online mode (TESL-Reborn; restarts the game)")) OfflineLaunchMode.SwitchTo("online"); }
            else GUILayout.Label("Online play needs TESL-Reborn (not installed); the game starts offline.");
            OfflineGraphicsSettings g = OfflineGraphics.Settings;
            if (OfflineBoards.Boards.Count > 0)
            {
                OfflineBoards.Board current = OfflineBoards.Find(g.board);
                if (GUILayout.Button("Board: " + (current == null ? "game's choice" : current.Name)))
                    OfflineGraphics.Change(delegate(OfflineGraphicsSettings x) {
                        int i = OfflineBoards.Boards.FindIndex(delegate(OfflineBoards.Board b) { return b.Key == x.board; });
                        x.board = i + 1 < OfflineBoards.Boards.Count ? OfflineBoards.Boards[i + 1].Key : ""; });
            }
            if (OfflineModes.Modes.Count > 0)
            {
                OfflineModes.Mode mode = OfflineModes.Find(g.mode);
                if (GUILayout.Button("Practice mode: " + (mode == null ? "ordinary practice" : mode.Name)))
                    OfflineGraphics.Change(delegate(OfflineGraphicsSettings x) {
                        int i = OfflineModes.Modes.FindIndex(delegate(OfflineModes.Mode m) { return m.Key == x.mode; });
                        x.mode = i + 1 < OfflineModes.Modes.Count ? OfflineModes.Modes[i + 1].Key : ""; });
                if (mode != null) GUILayout.Label(mode.Description + "  (Play > Practice to play it)");
            }
            if (OfflineOpponents.All.Count > 0 || OfflineMods.Decks.Count > 0)
            {
                OfflineOpponents.Opponent who = OfflineOpponents.Find(g.opponent);
                if (OfflineOpponents.All.Count > 0 && GUILayout.Button("Practice opponent: " + (who == null ? "the game's choice" : who.Name + (string.IsNullOrEmpty(who.Title) ? "" : ", " + who.Title))))
                    OfflineGraphics.Change(delegate(OfflineGraphicsSettings x) { x.opponent = OfflineOpponents.Next(x.opponent); });
                if (GUILayout.Button("Mod opponents and decks in random Practice: " + (g.modOpponents ? "on" : "off")))
                    OfflineGraphics.Change(delegate(OfflineGraphicsSettings x) { x.modOpponents = !x.modOpponents; });
            }
            GUILayout.Label("Current match (your turn)");
            Simulation s = OfflineNativeMatch.CurrentSimulation;
            string why = OfflineCheats.Unavailable(s);
            GUI.enabled = why == null;
            if (GUILayout.Button("+5 magicka now")) Match(OfflineCheats.MatchCheat.Magicka);
            if (GUILayout.Button("+1 max magicka")) Match(OfflineCheats.MatchCheat.MaxMagicka);
            if (GUILayout.Button("Draw a card")) Match(OfflineCheats.MatchCheat.Draw);
            if (GUILayout.Button("Heal 10")) Match(OfflineCheats.MatchCheat.Heal);
            if (GUILayout.Button("Win the match")) Match(OfflineCheats.MatchCheat.Win);
            if (GUILayout.Button("Trigger Beast Form (your werewolves)")) Match(OfflineCheats.MatchCheat.BeastForm);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Restore a rune")) Match(OfflineCheats.MatchCheat.RestoreRune);
            if (GUILayout.Button("Restore opponent's rune")) Match(OfflineCheats.MatchCheat.RestoreOpponentRune);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Heal opponent 10")) Match(OfflineCheats.MatchCheat.HealOpponent);
            List<ServerCard> pile = why == null ? OfflineCheats.DiscardPile(s) : new List<ServerCard>();
            if (pile.Count > 0)
            {
                if (discardPick < 0 || discardPick >= pile.Count) discardPick = pile.Count - 1;
                GUILayout.Label("Discard pile (" + pile.Count + "):");
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("<", GUILayout.Width(28))) discardPick = (discardPick + pile.Count - 1) % pile.Count;
                GUILayout.Label(pile[discardPick].GetCardName());
                if (GUILayout.Button(">", GUILayout.Width(28))) discardPick = (discardPick + 1) % pile.Count;
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Return it to your hand")) { Match(OfflineCheats.MatchCheat.ReturnFromDiscard, discardPick); discardPick = -1; }
            }
            else if (why == null) GUILayout.Label("Discard pile: empty");
            GUI.enabled = true;
            if (why != null) GUILayout.Label("(" + why + ")");
            // Installed expansions' own sections (EverlastingHost.RegisterMenuPage).
            foreach (EverlastingHost.MenuPage page in EverlastingHost.MenuPages)
            {
                GUILayout.Space(6);
                GUILayout.Label(page.Title);
                try { page.Draw(); }
                catch (Exception e) { GUILayout.Label("(this section failed: " + e.Message + ")"); }
            }
            GUILayout.Space(6);
            GUILayout.Label(status);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Support TESL: Everlasting:");
            if (GUILayout.Button("Ko-fi")) Application.OpenURL("https://ko-fi.com/archangelaries");
            if (GUILayout.Button("Patreon")) Application.OpenURL("https://www.patreon.com/c/ArchAngelAries");
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            if (GUILayout.Button("Close")) open = false;
            GUI.DragWindow();
        }

        private void Meta(Func<OfflinePlayerStateData, string> change) { Meta(true, change); }

        // assist: a cheat (the profile loses the "earned the hard way" standing that expansions can show)
        private void Meta(bool assist, Func<OfflinePlayerStateData, string> change)
        {
            if (state == null) { status = "Local profile unavailable."; return; }
            string result = null;
            try { state.Mutate(delegate(OfflinePlayerStateData d) { result = change(d); if (assist) d.assistUsed = true; }); OfflineCheats.RefreshClient(); status = result;
                if (assist) EverlastingHost.RaiseAssistUsed(); }
            catch (Exception e) { status = "Failed: " + e.Message; Debug.LogError("OFFLINE CHEATS: " + e); }
        }

        private void Match(OfflineCheats.MatchCheat cheat) { Match(cheat, -1); }

        private void Match(OfflineCheats.MatchCheat cheat, int pick)
        {
            try
            {
                string why = OfflineCheats.Apply(OfflineNativeMatch.CurrentSimulation, cheat, pick); status = why ?? OfflineCheats.Done(cheat);
                if (why == null && state != null) { state.Mutate(delegate(OfflinePlayerStateData d) { d.assistUsed = true; }); EverlastingHost.RaiseAssistUsed(); }
            }
            catch (Exception e) { status = "Failed: " + e.Message; Debug.LogError("OFFLINE CHEATS: " + e); }
        }
    }
}
