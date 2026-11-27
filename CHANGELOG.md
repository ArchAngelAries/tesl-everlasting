# Changelog

## 1.0.5 — faster start, changed cards

- **No more getting stuck on a reward screen:** if a reward cannot be applied, the game now logs it and moves on instead of
  showing the same (empty) reward again forever, and saving your profile falls back to a plain copy if Windows refuses
  the normal save. Thanks for the report on GitHub (issue #1).
- **Smoother start:** the longest freeze while the game starts went from about 3.4 seconds to under 0.6 seconds with
  several mods enabled. Mod cards are prepared while the game is still loading, and the remaining start-up steps no
  longer run all at once.
- **For modders: change an original card in place.** A mod can now change any original card (cost, power, health,
  name, text, rarity and abilities). Every deck that holds the card plays the changed version. Like all gameplay mods,
  changes apply to offline play only. Made with TESL Forge 1.0.2.
- **For modders: arena drafts.** A mod can keep its cards out of Solo and Versus Arena drafts, or keep out single cards.
  By default mod cards join the drafts like official cards.

## 1.0.4 — story rewards in their own books

- **Story rewards arrive as the game shows them:** rewards from The Fall of the Dark Brotherhood, Return to Clockwork City
  and Isle of Madness now come in each story's own reward book. The Forgotten Hero keeps its Elder Scroll. Thanks for
  the report on GitHub (issue #1).
- **For expansions:** event battles play the deck the event names (the event's own deck, or a premade deck the player
  picked), not the player's first saved deck.

## 1.0.3 — titles announced, lighter mod art

- **New titles are announced:** when you earn a title, the reward screen now shows it ("New Title Earned" with its name and
  description). Before, earned titles were added to your profile without being shown. Story choice titles, such as The
  Kingslayer or The King's Protector, appear when the chapter ends.
- **Mod card art uses less memory:** card art from mods is loaded GPU-compressed with mipmaps, about a quarter of the memory,
  and stays sharp when cards are drawn small.
- **For modders:** mods can read the player's profile name (for example for their own leaderboards).

## 1.0.2 — more titles, quieter creatures

- **45 more titles can be earned offline:** the card titles such as The Golden, The Mastermind, The Dawnbreaker, The Forger,
  The Engineer and The Archmage, the Isle of Madness, Alliance War, Moons of Elsweyr and Jaws of Oblivion card titles, and
  The Lone Warrior, The Pastry Chef, The Scrupulous, The Ironic, The Troublemaker, Pact Warleader, Imperial Tactician,
  Covenant Armsmaster, Servant of Destruction and The Artist.
- **Creature opponents no longer talk:** wolves, spiders, mammoths, Dwemer constructs, dragons and other creatures don't
  emote with a borrowed person's voice any more.
- **For modders:** TESL Forge can now preview a card's deck-list row as the game draws it.

## 1.0.1 — fixes

- **Pure-Blood Elder** works: "When you gain max magicka, double the amount you gain" now doubles the magicka you gain each
  turn, as in the original. Before, the match could stop at the start of a turn while it was in play.
- **Stealing an equipped creature** (for example with Miraak, Dragonborn) works: the creature's items come with it, as in the
  original. Before, the match could stop.

## 1.0.0 — first public release

The Elder Scrolls: Legends, playable offline against the in-game AI, with nothing from the original servers needed.

- **Every card plays with its full rules:** all 1,718 cards, including Prophecy, runes, lanes, Betray, Exalt, Consume,
  Plot, Assemble, Mobilize, Invade, Treasure Hunt, double cards, Slay and linked cards.
- **The whole game offline:** the stories (Normal and Master), puzzles, Solo and Versus Arena, ranked and casual play
  against the AI, quests, rewards, levels, titles, the store (packs, premade decks, card backs, story acts for gold) and
  card crafting.
- **F10 menu:** graphics options and quality presets, window mode, and offline-only cheats (currency, unlock all cards and
  decks, free deck building with any number of copies, and in-match magicka, draw, heal, win, Beast Form, rune restore, healing the opponent and taking a card back from
  your discard pile).
- **Mods made with TESL Forge:** new cards, decks, avatars, card backs, boards, music, sounds and voices, game modes,
  stories, AI opponents, card packs, translations and remastered art.
- **Works beside TESL-Reborn:** with Reborn installed the game starts online and only local visual and audio mods are
  used; the F10 menu switches between online and offline. Cheats and gameplay mods never work against other players.
- Anti-aliasing is never fully off (2× minimum): without it some story scenes drew a black table.
