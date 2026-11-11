# Arena rank rules: source evidence (Claude, 2026-10-24)

Source: UESP, Legends:Solo_Arena (fetched 2026-10-24 via WebFetch). Short excerpts:
- Ranks, low to high: Pit Dog (9), Brawler (8), Bloodletter (7), Myrmidon (6), Warrior (5), Gladiator (4), Hero (3), Champion (2), Grand Champion (1).
- "Only seven wins are needed to rank up until you reach Hero." After Hero, promotion requires nine wins, up to Grand Champion.
- "every time you earn a rank, you earn a promotion reward of fifty gold"; the first promotion to Grand Champion (either arena) also grants a premium Adoring Fan.
- "Solo Arena has an independent ranking systems of the other Arenas", with the same progression and naming.
- Rank does not choose opponents (a hidden Elo does). Whether rank carries over or resets between runs is not stated (verification-needed tag).

Client facts (decompiled): PlayerProfileLandingWin / PlayerProfileWin read ratings "soloArena" / "versusArena" (default 9);
SoloArena / VersusArena animate DoEndSequenceRankUpgrade when rankSeen > rank.
Reconstruction choices: wins count when each validated match result is recorded (not at run end); the rank never decays.
