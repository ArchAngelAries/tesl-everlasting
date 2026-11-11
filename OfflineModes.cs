using System;
using System.Collections.Generic;
using System.IO;
using FullSerializer;
using SPTESL;
using UnityEngine;

namespace OfflineEngine
{
    // Mod game modes (<mod>/modes/modes.json). A mode is played as a practice match against the AI: the player picks
    // the mode (F10 offline menu, or OfflineGraphicsSettings.mode), then Play > Practice as usual. The match runs on the
    // Default scenario under the story/arena rule scope (the same path as arena matches) with:
    //   lanes:    1 or 2 lanes of any supported lane type (Field "Default", Shadow "Stealth", StoryLaneRules.Supported), size 2/4/6,
    //             built by the client's own Simulation.AddCustomLanesToStartInfo;
    //   player / opponent: health, starting magicka, runes (0..5), extra starting cards, cards placed in hand, lanes and
    //             supports at the start, deck ("own", a mod or original deck name, or "random" of N cards);
    //   opponent: AI difficulty, name; who goes first;
    //   rules:    extra draws, extra max magicka, damage or healing each turn, a random creature for the active player
    //             every N turns, lanes that change type every N turns;
    //   board (a base playmat or a mod board), music ("Mode:<mod>/<mode>" context), gold for a win.
    // Health, magicka and runes are set when the players register (as OfflinePuzzleSetups); cards and turn rules are
    // native actions at turn start (DecisionRestoration.ProcessStartTurn), so the client sees them as ordinary history.
    public static class OfflineModes
    {
        public sealed class Side
        {
            public int Health, Magicka, Runes = -1, ExtraCards; public string Deck = "own"; public int RandomSize = 30;
            public readonly Dictionary<string, List<string>> Cards = new Dictionary<string, List<string>>();   // hand, lane1, lane2, support
        }
        public sealed class Mode
        {
            public string Key, Mod, Slug, Name, Description, First = "random", Difficulty = "Expert", OpponentName, Board;
            public readonly List<string> LaneTypes = new List<string>(); public readonly List<int> LaneSizes = new List<int>();
            public Side Player = new Side(), Opponent = new Side();
            public int DrawPerTurn, MagickaPerTurn, DamagePerTurn, HealPerTurn, RandomCreatureEvery, LaneShuffleEvery, RewardGold;
            public bool NoMulligan; public string AiRef;   // AiRef: a mod opponent ("ai": "<mod>/<slug>")
        }
        public static readonly List<Mode> Modes = new List<Mode>();
        public static Mode Active;
        public static int TurnsSeen, CardsPlaced, RulesApplied, LaneShuffles;
        private static bool startDone;
        private static readonly HashSet<int> magickaDone = new HashSet<int>();
        private static CardLaneType[] currentLanes;
        private static readonly System.Random rng = new System.Random();

        public static CardLaneType[] LaneChoices()
        {
            List<CardLaneType> all = new List<CardLaneType> { CardLaneType.Default, CardLaneType.Stealth };
            all.AddRange(StoryLaneRules.Supported);
            return all.ToArray();
        }

        private static int I(Dictionary<string, fsData> d, string k, int def) { return d != null && d.ContainsKey(k) && !d[k].IsNull ? (int)(d[k].IsInt64 ? d[k].AsInt64 : (long)d[k].AsDouble) : def; }
        private static string S(Dictionary<string, fsData> d, string k, string def) { return d != null && d.ContainsKey(k) && d[k].IsString ? d[k].AsString : def; }
        private static Dictionary<string, fsData> D(Dictionary<string, fsData> d, string k) { return d != null && d.ContainsKey(k) && d[k].IsDictionary ? d[k].AsDictionary : null; }

        private static Side ReadSide(Dictionary<string, fsData> d)
        {
            Side s = new Side();
            if (d == null) return s;
            s.Health = I(d, "health", 0); s.Magicka = I(d, "magicka", 0); s.Runes = I(d, "runes", -1); s.ExtraCards = I(d, "extra_cards", 0);
            s.Deck = S(d, "deck", "own"); s.RandomSize = I(d, "random_size", 30);
            Dictionary<string, fsData> cards = D(d, "cards");
            if (cards != null)
                foreach (KeyValuePair<string, fsData> kv in cards)
                {
                    List<string> names = new List<string>();
                    if (kv.Value.IsList) foreach (fsData n in kv.Value.AsList) names.Add(n.AsString);
                    s.Cards[kv.Key] = names;
                }
            return s;
        }

        public static void Load(string modId, string dir, List<string> problems)
        {
            string file = Path.Combine(Path.Combine(dir, "modes"), "modes.json");
            if (!File.Exists(file)) return;
            foreach (fsData e in fsJsonParser.Parse(File.ReadAllText(file)).AsDictionary["modes"].AsList)
            {
                Mode m = Parse(e.AsDictionary, modId, null, problems);
                Modes.RemoveAll(delegate(Mode x) { return x.Key == m.Key; });
                Modes.Add(m);
            }
        }

        // One mode (or a story battle: OfflineStories passes its own key) from its JSON object.
        public static Mode Parse(Dictionary<string, fsData> d, string modId, string key, List<string> problems)
        {
            Mode m = new Mode { Mod = modId, Slug = S(d, "slug", "mode"), Name = S(d, "name", "Mode"), Description = S(d, "description", ""),
                First = S(d, "first", "random"), Difficulty = S(d, "difficulty", "Expert"), OpponentName = S(d, "opponent_name", null), Board = S(d, "board", null),
                Player = ReadSide(D(d, "player")), Opponent = ReadSide(D(d, "opponent")), RewardGold = I(d, "reward_gold", 0) };
            m.Key = key ?? modId + "/" + m.Slug;
            m.AiRef = S(d, "ai", null);
            if (d.ContainsKey("lanes") && d["lanes"].IsList)
                foreach (fsData l in d["lanes"].AsList)
                {
                    Dictionary<string, fsData> ld = l.AsDictionary;
                    string type = S(ld, "type", "Default");
                    CardLaneType parsed;
                    try { parsed = (CardLaneType)Enum.Parse(typeof(CardLaneType), type, true); }
                    catch (ArgumentException) { problems.Add(m.Key + ": unknown lane type " + type); parsed = CardLaneType.Default; }
                    if (Array.IndexOf(LaneChoices(), parsed) < 0) problems.Add(m.Key + ": lane type " + type + " is not implemented offline yet; it will play as a plain lane");
                    int size = I(ld, "size", 4);
                    if (size != 2 && size != 4 && size != 6) { problems.Add(m.Key + ": lane size must be 2, 4 or 6"); size = 4; }
                    m.LaneTypes.Add(parsed.ToString()); m.LaneSizes.Add(size);
                }
            if (m.LaneTypes.Count == 0) { m.LaneTypes.Add("Default"); m.LaneTypes.Add("Stealth"); m.LaneSizes.Add(4); m.LaneSizes.Add(4); }
            if (m.LaneTypes.Count > 2) { problems.Add(m.Key + ": at most 2 lanes"); m.LaneTypes.RemoveRange(2, m.LaneTypes.Count - 2); m.LaneSizes.RemoveRange(2, m.LaneSizes.Count - 2); }
            Dictionary<string, fsData> r = D(d, "rules");
            m.DrawPerTurn = I(r, "draw_per_turn", 0); m.MagickaPerTurn = I(r, "magicka_per_turn", 0); m.DamagePerTurn = I(r, "damage_per_turn", 0);
            m.HealPerTurn = I(r, "heal_per_turn", 0); m.RandomCreatureEvery = I(r, "random_creature_every", 0); m.LaneShuffleEvery = I(r, "lane_shuffle_every", 0);
            m.NoMulligan = r != null && r.ContainsKey("no_mulligan") && r["no_mulligan"].AsBool;
            return m;
        }

        public static Mode Find(string key) { return string.IsNullOrEmpty(key) ? null : Modes.Find(delegate(Mode m) { return m.Key == key; }); }
        public static Mode Selected { get { return Find(OfflineGraphics.Settings.mode); } }

        public static void Begin(Mode m) { Active = m; TurnsSeen = 0; startDone = false; currentLanes = null; magickaDone.Clear(); }
        private static GameProperties mulliganProps; private static bool mulliganWas;
        // No mulligan: the Default scenario's own GameProperties flag, restored when the match ends.
        public static void DisableMulligan(GameProperties props) { if (props == null) return; mulliganProps = props; mulliganWas = props.MulliganDisabled; props.MulliganDisabled = true; }
        public static void End() { Active = null; if (mulliganProps != null) { mulliganProps.MulliganDisabled = mulliganWas; mulliganProps = null; } }

        public static int Cost(Card c)
        {
            SPTESL.Attribute a = c.EntityAttributes.Find(delegate(SPTESL.Attribute x) { return x.Name == AttributeType.ManaCost; });
            IntAttribute i = a as IntAttribute;
            return i == null ? 0 : i.Value;
        }

        public static Card FindCard(GameConfiguration config, string name)
        {
            int id;
            if (int.TryParse(name, out id)) return config.FindCardById(id);
            foreach (CardSet s in config.CardCollection) foreach (Card c in s.Cards) if (c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        // A random constructed-style deck: collectible cards, up to 3 copies each (1 for uniques).
        public static List<int> RandomDeck(GameConfiguration config, int size)
        {
            List<Card> pool = new List<Card>();
            foreach (CardSet s in config.CardCollection)
                foreach (Card c in s.Cards)
                    if (c != null && c.Production && !string.IsNullOrEmpty(c.ExportCode) && !c.HasSubtype(CardSubtype.Avatar)) pool.Add(c);
            List<int> deck = new List<int>();
            Dictionary<int, int> copies = new Dictionary<int, int>();
            for (int guard = 0; deck.Count < size && guard < size * 50 && pool.Count > 0; guard++)
            {
                Card c = pool[rng.Next(pool.Count)];
                int have; copies.TryGetValue(c.ResourceId.Id, out have);
                if (have >= (c.Unique ? 1 : 3)) continue;
                copies[c.ResourceId.Id] = have + 1; deck.Add(c.ResourceId.Id);
            }
            return deck;
        }

        // The deck for one side, or null for "own" / "practice" (the caller's usual choice).
        public static List<int> DeckCards(GameConfiguration config, Side side)
        {
            if (side.Deck == null || side.Deck == "own" || side.Deck == "practice") return null;
            if (side.Deck == "random") return RandomDeck(config, Math.Max(10, side.RandomSize));
            DeckInfo info = (config.DeckInfo ?? new List<DeckInfo>()).Find(delegate(DeckInfo x) { return x != null && (x.TypeName == side.Deck || string.Equals(OfflineMods.Text(x.DisplayName), side.Deck, StringComparison.OrdinalIgnoreCase)); });
            if (info == null || info.Cards == null) return null;
            List<int> cards = new List<int>();
            foreach (DeckInfo.CardInfo c in info.Cards) for (int n = 0; n < Math.Max(1, c.Count); n++) cards.Add(c.Id);
            return cards;
        }

        public static void AddLanes(MatchConfiguration.StartInfo info, Mode m)
        {
            List<int> sizes = new List<int>(m.LaneSizes);
            if (sizes.Count == 1) sizes.Add(0);                 // one lane: the second is left out
            List<string> types = new List<string>(m.LaneTypes);
            if (types.Count == 1) types.Add("Default");
            Simulation.AddCustomLanesToStartInfo(types, sizes, null, null, info);
            currentLanes = new CardLaneType[m.LaneTypes.Count];
            for (int i = 0; i < currentLanes.Length; i++) currentLanes[i] = (CardLaneType)Enum.Parse(typeof(CardLaneType), m.LaneTypes[i]);
        }

        private static ServerCard Avatar(Simulation sim, PlayerState player)
        {
            ServerCard a = sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarDeck)
                ?? sim.CardCatalog.FindCardInDynamicZone(delegate(ServerCard c) { return c.HasSubtype(CardSubtype.Avatar); }, player.NetId, GameZoneName.AvatarBoard);
            return a;
        }

        // Right after the players register (before the game-start sequence), as the puzzle setups.
        public static void ApplyAtRegistration(Simulation sim)
        {
            if (Active == null) return;
            ServerEffectUtilities.TriggerGameState gs = new ServerEffectUtilities.TriggerGameState(sim.HistoryLog, sim.RandomGenerator, sim.CardCatalog, sim.Players,
                new ServerEffectUtilities.TriggerQueues(), sim.ActiveQuests, new List<CustomDisplayCard>(), sim._scenarioId);
            foreach (PlayerState player in sim.Players)
            {
                Side side = player.PlayerType == (int)PlayerType.Normal ? Active.Player : Active.Opponent;
                ServerCard avatar = Avatar(sim, player);
                if (side.Runes >= 0) { player.SetAttribute(AttributeType.NumRunes, Math.Min(5, side.Runes)); player.SetAttribute(AttributeType.NumStartingRunes, Math.Min(5, side.Runes)); }
                if (avatar == null) continue;
                if (side.Health > 0) { avatar.SetAttribute(AttributeType.StartingHealth, side.Health); avatar.SetAvatarHealthPreGame(side.Health); }
            }
            Debug.Log("OFFLINE MODES: " + Active.Key + " set up (lanes " + string.Join("/", Active.LaneTypes.ToArray()) + ")");
        }

        private static void Place(Simulation s, TurnState t, PlayerState owner, string where, string name)
        {
            GameConfiguration config = GameManager.Instance.Config;
            Card card = FindCard(config, name);
            if (card == null) { Debug.LogWarning("OFFLINE MODES: no card " + name); return; }
            ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, t);
            if (where == "hand")
            {
                GameZone.Position pos = s.CardCatalog.FindInsertionPosition(owner.NetId, GameZoneName.Hand, 0, CardInsertionType.Bottom);
                PlayerActions.CreateToken.StaticExecute(gs, null, card, false, GameZoneType.Hand, pos, owner.NetId, 1, -1, null);
            }
            else if (where == "deck")
            {
                GameZone.Position pos = s.CardCatalog.FindInsertionPosition(owner.NetId, GameZoneName.Deck, 0, CardInsertionType.Random);
                PlayerActions.CreateToken.StaticExecute(gs, null, card, false, GameZoneType.Deck, pos, owner.NetId, 1, -1, null);
            }
            else
            {
                GameZoneName zone = card.FindGameZoneName(GameZoneType.Board);
                int lane = where == "lane2" ? 1 : 0;
                if (zone == GameZoneName.CreatureBoard && currentLanes != null && lane >= currentLanes.Length) lane = 0;
                if (zone == GameZoneName.CreatureBoard && s.CardCatalog.IsLaneFull(zone, lane, owner.NetId)) return;
                GameZone.Position pos = s.CardCatalog.FindInsertionPosition(owner.NetId, zone, zone == GameZoneName.CreatureBoard ? lane : 0, CardInsertionType.Bottom);
                PlayerActions.CreateToken.StaticExecute(gs, null, card, false, GameZoneType.Board, pos, owner.NetId, 1, -1, null);
            }
            CardsPlaced++;
        }

        // As the natural magicka growth (DecisionRestoration.RestoreStartTurnMagicka): the native buff, then the avatar's
        // enchantments recalculated (a bare buff stays cached) and a card refresh for the client.
        private static void Buff(Simulation s, ServerEffectUtilities.TriggerGameState gs, ServerCard avatar, int amount)
        {
            avatar.AddMaxManaBuff(gs, amount);
            avatar.RecalculateEnchantmentsWithTriggers(gs, null);
            HistoryLoggingUtilities.LogFullCardRefresh(s.HistoryLog, s.CardCatalog, new List<ServerCard> { avatar }, false);
        }

        public static void OnTurnStart(Simulation s, TurnState t, PlayerState p)
        {
            Mode m = Active;
            if (m == null) return;
            try
            {
                TurnsSeen++;
                ServerEffectUtilities.TriggerGameState gs = DecisionRestoration.State(s, t);
                if (!startDone)
                {
                    startDone = true;
                    foreach (PlayerState owner in s.Players)
                    {
                        Side side = owner.PlayerType == (int)PlayerType.Normal ? m.Player : m.Opponent;
                        foreach (KeyValuePair<string, List<string>> kv in side.Cards) foreach (string name in kv.Value) Place(s, t, owner, kv.Key, name);
                        if (side.ExtraCards > 0) PlayerActions.DrawCards.StaticExecute(gs, owner, side.ExtraCards, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1);
                    }
                }
                ServerCard avatar = Avatar(s, p);
                if (avatar == null) { List<ServerCard> any = s.CardCatalog.GatherCardList(delegate(ServerCard c) { return c.OwnerNetId == p.NetId && c.HasSubtype(CardSubtype.Avatar); }); avatar = any.Count > 0 ? any[0] : null; }
                Side mine = p.PlayerType == (int)PlayerType.Normal ? m.Player : m.Opponent;
                if (avatar != null && magickaDone.Add(p.NetId.Id) && mine.Magicka > 1)
                {   // starting magicka on the side's first turn (a buff at registration is lost for the AI's avatar)
                    int have = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                    if (mine.Magicka > have) Buff(s, gs, avatar, Math.Min(12, mine.Magicka) - have);
                    PlayerActions.SetMana.StaticExecute(gs, p, avatar.QueryAttribute(AttributeType.MaxMana).Value);
                    Debug.Log("OFFLINE MODES: starting magicka " + have + " -> " + avatar.QueryAttribute(AttributeType.MaxMana).Value + " for player " + p.NetId.Id);
                }
                else if (avatar == null) Debug.LogWarning("OFFLINE MODES: no avatar for player " + p.NetId.Id + " at turn start");
                if (m.DrawPerTurn > 0) { PlayerActions.DrawCards.StaticExecute(gs, p, m.DrawPerTurn, PlayerActions.DrawCards.DrawDeck.Self, ServerCardCatalog.MoveOperationType.DefaultDraw, false, true, -1); RulesApplied++; }
                if (m.MagickaPerTurn > 0 && avatar != null)
                {
                    int max = avatar.QueryAttribute(AttributeType.MaxMana).Value;
                    int add = Math.Min(m.MagickaPerTurn, Math.Max(0, 12 - max));
                    if (add > 0) Buff(s, gs, avatar, add);
                    PlayerActions.SetMana.StaticExecute(gs, p, avatar.QueryAttribute(AttributeType.MaxMana).Value);
                    RulesApplied++;
                }
                if (m.DamagePerTurn > 0 && avatar != null) { PlayerActions.DamageAvatarOfPlayer.StaticExecute(gs, null, p, m.DamagePerTurn, -1); RulesApplied++; }
                if (m.HealPerTurn > 0 && avatar != null) { CardActions.Heal.StaticExecute(gs, avatar, new List<ServerCard> { avatar }, m.HealPerTurn, -1, false, false); RulesApplied++; }
                if (m.RandomCreatureEvery > 0 && TurnsSeen % m.RandomCreatureEvery == 0)
                {
                    List<Card> pool = new List<Card>();
                    int cap = Math.Max(1, (TurnsSeen + 1) / 2 + 1);
                    foreach (CardSet cs in GameManager.Instance.Config.CardCollection)
                        foreach (Card c in cs.Cards)
                            if (c != null && c.Production && !string.IsNullOrEmpty(c.ExportCode) && c.HasSubtype(CardSubtype.Creature) && Cost(c) <= cap) pool.Add(c);
                    if (pool.Count > 0)
                    {
                        int lane = rng.Next(currentLanes == null ? 2 : currentLanes.Length);
                        if (!s.CardCatalog.IsLaneFull(GameZoneName.CreatureBoard, lane, p.NetId))
                        {
                            Card pick = pool[rng.Next(pool.Count)];
                            GameZone.Position pos = s.CardCatalog.FindInsertionPosition(p.NetId, GameZoneName.CreatureBoard, lane, CardInsertionType.Bottom);
                            PlayerActions.CreateToken.StaticExecute(gs, null, pick, false, GameZoneType.Board, pos, p.NetId, 1, -1, null);
                            RulesApplied++;
                        }
                    }
                }
                if (m.LaneShuffleEvery > 0 && currentLanes != null && TurnsSeen > 1 && (TurnsSeen - 1) % m.LaneShuffleEvery == 0)
                {
                    CardLaneType[] choices = LaneChoices();
                    for (int i = 0; i < currentLanes.Length; i++)
                    {
                        CardLaneType next = choices[2 + rng.Next(choices.Length - 2)];
                        foreach (PlayerState owner in s.Players) PlayerActions.ChangeLaneType.StaticExecute(s.HistoryLog, s.CardCatalog, owner, currentLanes[i], next);
                        currentLanes[i] = next;
                    }
                    LaneShuffles++;
                }
            }
            catch (Exception e) { Debug.LogWarning("OFFLINE MODES: turn rules failed: " + e); }
        }
    }
}
