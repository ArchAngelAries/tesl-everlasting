# Title sources: per-game feats, story wins, arena, collection, supporter (checkpoint 25, Claude)

The 197 installed Title quests ship without goals, so each condition is reconstructed from the installed
`TitleData.Description`. Code: `OfflineTitles.cs` (kinds, arena, collection, supporter) and `OfflineTitleFeats.cs`
(per-game feats and story wins). Report: `exports/native-title-goals.tsv` (column `kind`).

## Per-game feats (`kind=feat`)
Event-worded feats use exact events (The Untouchable, The Master of Flesh, The Runebreaker). State-worded feats are judged
from the running Simulation, sampled once per frame on the main thread and once more at the result: a state that rises
and falls inside one frame is not seen; "in play" means the human's creature lanes.

| Title | Rule |
|---|---|
| The Defiant / The Miraculous | win with avatar health 1 / below 0 at the result |
| The Clever | win with the human's native TurnCount ≤ 6 |
| The Untouchable | win, TurnCount ≥ 5, no native DamageValue event on the human's avatar (quests owned by both players, so enemy and self damage count) and no observed health drop (covers damage without an acting card; conservative) |
| The Immortal / The Hist Mage | avatar health ≥ 100 / avatar MaxMana ≥ 15 at any sample |
| The Gravedigger | ≥ 50 of the human's creatures in the Cemetery zone |
| The General | 8 "Imperial Grunt" in the human's lanes at once |
| The Master of Mirrors | 3 copies of one `Card.Unique` creature in the human's lanes |
| The Master of Flesh | native SummonCount of Flesh Atronach with power ≥ 20, judged at the summon (a later buff does not count) |
| The Unconquerable | Fighters Guild Elite with power ≥ 100 in play (sampled) |
| The Prophet | native player attribute PropheciesFromRuneBreak ≥ 5 |
| The Temple's Chosen | health ≥ 40 in chapter event 560661651 (DBH Temple of Akatosh, scenario PriestsOfTheHour) |
| The Runebreaker | lifetime count of opponent rune breaks ≥ 500, counted at each break in the restored `DecisionRestoration.CheckForRuneBreaks` (NumRunesLost is a deficit that rune restores reset) |

"Summon 20 Dragons in one game" (The Master of Dragons) is a `single-game` tracker: the lifetime goal parser on
the part before "in one game", judged on one match's added value only.

## Story wins (`kind=story`)
Granted when the human wins the named scenario (installed `Scenario.UniqueName`, `exports/native-campaign-scenarios.tsv`)
or chapter event; "Master" uses the match request's `campaignData.isMastery`. Installed Master scenario names are normalized for matching; legacy Master progress markers remain recognized. Mapping research: a read-only subagent
(UESP Legends:Titles, Legends:Night_Mother's_Crypt, Legends:The_Other_Half, Legends:Isle_of_Madness_(story)) against
installed data; every scenario name below was checked by the lead in the installed export.

| Title | Won | Confidence |
|---|---|---|
| The Animal Lover | DBH_Act1_Ep2_MonstersOfWayrest (Wayrest Plains), Master | high |
| The Giant Slayer | DBH_Act2_Ep3_HilltopGiants, Master, opponent health ≤ 0 at the result | medium |
| The Crypt Raider / The Assassin's Bane | DBH_Act3_Ep6_Alisanne / on Master | medium (name-based) |
| The Listener / The Night Mother's Keeper | DBH_Act3_Ep6_UtherNere / on Master | medium (name-based) |
| Child of Hircine | CWC_Act1_Ep2_PitFalmer_Werewolf (Sightless Pit werewolf branch) | high: installed GatedScenario title reference |
| Friend to Animals | CWC_Act1_Ep2_FrostTroll_B (Bleakcoast wolves branch), normal or Master | high: installed GatedScenario title reference |
| The Escape Artist | CWC_Act1_Ep1_MistveilGuard (event -493560241 also lists bundle `reward_the_escape_artist`) | high |
| The Truthbringer (IOM) | IOM_Act1_Ep1_TellTheKnight | high |
| The Ghost Whisperer (IOM) | event 591184772 (FortressPersonalChambers) | high: installed GatedScenario title reference |
| The Betrayer (IOM) | event -1909586255 (BthardzamSecretPassage) | high: installed GatedScenario title reference |
| The Prince of Order (IOM) | event -938799815 (ObeliskOfOrder) | high: installed GatedScenario title reference |
| The Ingenious | event -2037241312 (Divayth Sun-in-Shadow; bundle `rewards_sun_in_shadows_ingenious_title`) | high |
| The Conqueror of Madness / The Clearheaded | event -1463643075 (IOM final battle) / on Master | high |
| The Master of Stealth | both CastleDefenders_NoDisguise and EclipseSneak won (profile markers `story:<scenario>`) | medium |
| The Memory Master | Master wins of events -783094043, 27525883, 1074531588, 1933962168, 1271260546 | high |
| The Gateslayer (IOM) | Master Gates of Madness (-527834228) after Master Garden of Swords (1824998868) | medium |

| City's Savior / God of the Machine | CWC_Act3_Ep6_Mecinar1: use Heart / Master | installed gate and dialogue; win trigger reconstructed |
| City's Destroyer / God Slayer | CWC_Act3_Ep6_Mecinar2: destroy Heart / Master | installed gate and dialogue; win trigger reconstructed |
| Purveyor of Prophecy | Kwama Mine, Mushroom Tower and Meliel's Farm wins | installed dialogue; cross-difficulty markers reconstructed |

| King's Protector / Kingslayer | King Barynia ending choice Spare / Kill after victory | installed option text; grant timing reconstructed |

| The Smuggler / Savior of Cyrodiil | Forgotten Hero chapter 8 / 20 reward bundles | existing bundle grant, tested |
| The Felonious | Skooma Cat 20-puzzle completion | existing completion grant, tested |

| Monster Hunter | 150 enemy Undead/Daedra kills | native MonsterHunterTarget subtype and DestroyCount; source-less kills unverified |

| Blood Magic Lord | play 50 Blood Magic spells | native PlayCount; paid threshold test |

| Aundae Clan Bloodkin | 10 Blood Magic spells created into hand by Aundae | source-specific native title counter, full-hand behavior inferred |

| Deflector / Defender / Light Footed / Vigorous | Elixir Ward / Guard / Cover / health changes | source-specific native counters; repeated keyword semantics unverified |

| Walker of Mundus | 50 keywords from Mundus Stone | source-specific native counter; nested reactions unverified |

| The Centurion | 100 wins with a nonempty all-Neutral starting deck | native starting-deck color data; threshold tested |

## Still locked, with the reason
- **Card-ability counters** (Golden, Mastermind, Dawnbreaker, Wrothgar Forge, ...): their
  native QuestGoalTypes are never raised by the shipped engine; they need per-card hooks in the rules engine.
- **Other:** The Engineer (no Assemble event hook), The Lone Warrior (needs damage-source tracking), Jaws Perfect (no installed "every keyword" list),
  The Risen, The Pastry Chef, The Scrupulous, The Strategist, The Chamberlain of Sheogorath (no offline Chaos Arena),
  TESL Reborn tester/developer titles.

## Arena (`kind=arena`)
A finished run with 0 losses counts: Versus → The Champion of the Arena (1) and The Master of the Arena (5);
Solo → The Master of Prowess (5). Called from `OfflineArenaService.Finish`.

## Collection (`kind=collection`)
"Collect 100% of the X Set": one copy of every production card with a deck code in the installed set whose display name
matches. The native profile collection screen (PlayerProfileCollectionWin) shows a set's percentage as numTypesOwned /
numTypes (card types, not playsets); which printings the server counted as types is not shipped, so the card list is a
reconstruction. Checked on every `AddCards` for that set's cards and at match end.

## Supporter (`kind=supporter`)
"Early supporter of ..." / "Supporter of ..." and the FOM bundle titles were purchase or early-access grants. Offline
access must not depend on purchases (BACKLOG 2026-10-13), so they are available like starting titles. TESL Reborn
tester/developer titles stay locked.
