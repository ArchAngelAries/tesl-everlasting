# Card spec format (input to OriginalSpecRules.cs)

Produced by `card_compiler.py` from installed text, or written per card in `manual-specs.json` (reviewed translations).
Every value below is exactly what the emitter accepts. Anything else makes the card skip with a reason, so do not invent fields.

## manual-specs.json

```json
{ "<card id>": { "name": "...", "raw": "<exact installed description, filled in by the merge script>",
                 "abilities": [ ... ], "note": "how the text maps; any reconstruction choice" } }
```

The compiler uses an entry only when `raw` equals the installed description. A manual entry replaces all compiled abilities, so include every sentence, including `Native` ones.

## Ability

`{"trigger": [T], "source": "<the sentence>", "effects": [E, ...]}`

| T | Meaning | Card kinds |
|---|---|---|
| `Play` | action resolution | Action |
| `Summon`, `LastGasp`, `Slay`, `Pilfer`, `Veteran` | keyword triggers (a list like `["Summon","LastGasp"]` is allowed) | Creature, Item (item: Summon = when played onto a creature; the others fire when the wielder's do; `self` = the wielder); Support for `Summon` on play |
| `Activate` | support activation (one per card) | Support |
| `Plot` | "Plot:" (fires if another card was played earlier this turn); item source remains the item, suitable for owner/hand effects | Creature/Action/Item |
| `Expertise` | end of your turn if you played an action, item or support | Creature/Support |
| `StartOfTurn` / `EndOfTurn` | "At the start/end of your turn" (source on board) | Creature/Support |
| `StartOfEachTurn` / `EndOfEachTurn` | "... of each turn" | Creature/Support |
| `SummonOther` | "When you summon another creature" (the new creature is not targetable as "it") | Creature/Support |
| `AfterAction` | "After you play an action" | Creature/Support |
| `Aura` | continuous `stats` or `keyword`, target `self` or card `pick: all`; optional `condition` checked continuously | Creature/Support |
| `CreatureSummoned` | "When you summon a Daedra / another creature" / "When your opponent summons..."; optional ability `event` filter, including `costMin` for current cost | Creature/Support |
| `CreatureDies` | "When a friendly/enemy creature dies"; `event` filter (default: another friendly creature) | Creature/Support |
| `CardDrawn` | "When you draw a card / a creature"; `event` filter (default: any friendly card) | Creature/Support |
| `CreatureAttacks` | "When a friendly creature attacks"; optional `event` filter (default another friendly creature) | Creature/Support |
| `CreatureMoves` | "When a friendly creature moves"; optional `event` filter (default another friendly creature) | Creature/Support |
| `CreatureDamaged` | "When a friendly creature takes damage"; optional `event` filter (default another friendly creature) | Creature/Support |
| `SelfDamaged` / `SelfAttacks` / `SelfWardBroken` / `SelfMoves` | "When this takes damage / attacks / its Ward is broken / moves" | Creature |
| `SelfDamagedByCreature` | "When this takes damage from a creature"; eventTarget is the damage source | Creature |
| `SelfDamagedSurvives` | "When this takes damage and survives"; native dying state must be false | Creature |
| `SelfDefends` | "When a creature attacks this"; the defending source is inciter and the attacking creature is eventTarget | Creature |
| `SelfAttacksGuard` | "When this attacks a Guard"; eventTarget must have Guard | Creature |
| `AfterSelfAttacks` | "After this attacks" using the native completed-attack event | Creature |
| `SelfEquipped` | "When this equips an item" (the event card is the item) | Creature |
| `FriendlyCreatureEquipped` | "When a friendly creature equips an item"; eventTarget is the wielder, including the source | Creature/Support |
| `RuneDestroyed` | "When an enemy rune is destroyed" (`event: {"side":"enemy"}`) or "When one of your runes is destroyed" (`"friendly"`) | Creature/Support |
| `HealthGained` | "When you gain health" | Creature/Support |
| `SelfConsumed` | "When this is Consumed"; source is already in Purgatory, inciter is consumer | Creature |
| `SelfConsumes` | Source consumes a card; emitted but no translated-card runtime coverage yet | Creature |
| `Native` | sentence fully covered by installed attributes (keyword lists, immunities, item stat prefix, avatar loss rule); `effects: []` | any |

**Choosing targets:** `pick: "choose"` targets are allowed in every trigger except `Aura` and `Native`.
- Play and plain Summon choices are made when the card is played.
- All other choices are made as a native ChooseTarget decision when the trigger resolves; this includes lane-relative Summon choices.
- Each ability may have only one chosen target.
- Creature Summon abilities may set `postPlacement: true` to choose after the creature enters play, allowing the source to be chosen. It requires a single Summon trigger and a chosen target.

Event filter (optional ability field `"event"`, for CreatureSummoned/SummonOther/CreatureDies/CardDrawn/CreatureAttacks/CreatureMoves/CreatureDamaged/RuneDestroyed):
`{"side":"friendly"|"enemy"|"any","tribe":..,"cardType":"Creature"|"Spell"|"Item"|"Support","keyword":..,"other":true,"lane":"this"}`.
In those abilities the event card is targetable as `{"kind":"inciter"}` ("give it +1/+1").

## Effect E

| action | fields |
|---|---|
| `damage` | `amount`, `target` (card or player; player `who: inciter` selects the event player), optional `per` (amount × count); `damageSource: "inciter"` only for `CreatureAttacks` damage to opponent |
| `heal` | `amount`, target `{"kind":"player","who":"self"}`; optional `per` card count |
| `healTo` | `amount` absolute health threshold, target `{"kind":"player","who":"self"}` |
| `breakRune` | player `target`; native damage to the next rune, or to zero health when no runes remain |
| `draw` | `amount`, target player self / opponent / both |
| `drawCopy` | random opponent deck card or creature (`kind: card`, `cardType: Card` or `Creature`, `side: enemy`, `zone: deck`, `pick: random`), or chosen friendly board creature (`cardType: Creature`, `side: friendly`, `zone: board`, `pick: choose`); creates a copy in the player's hand |
| `magicka` | `amount` (gain this turn, self) |
| `maxMagicka` | `amount` (self) |
| `stats` | `attack`, `health`, `target`, optional `temporary: true` ("this turn"), optional `per` (positive values only) |
| `doubleStats` | card `target`; doubles current power and current health through native buffs |
| `doublePower` | card `target`; doubles current power through a native buff |
| `powerFromHealth` | card `target`; gives power equal to its current health through a native buff |
| `keyword` | `keyword` ∈ Taunt (Guard), Ward, Deathtouch (Lethal), Drain, Breakthrough, Charge, Regenerate, Cover; `target`; optional `temporary` |
| `loseKeyword` | `keyword`, card `target`; uses native RemoveCover for Cover and the native keyword-stop enchantment for other keywords |
| `stealEnemyKeywords` | self `target`; native action steals all standard keywords from enemy creatures |
| `copyFriendlyKeywords` | self `target`; native action copies all standard keywords from friendly creatures |
| `destroy` / `shackle` / `silence` / `unsummon` / `battle` / `banish` / `move` | `target` (card); `battle` makes the source fight it; `move` = change lane |
| `transform` | `target`, `token` (exact installed card name), optional `attack`/`health` to disambiguate |
| `equipToken` | card `target`, `token` (exact installed item name), optional `attack`/`health` to disambiguate; creates and equips the native item |
| `handToken` | `count`, `token` |
| `summonToken` | `count`, `token`, optional `attack`/`health`, `place` ∈ same / other / each, optional `fill: true` ("Fill this lane with X"; creature source) |
| `handRandom` | `count`, `pool` |
| `summonRandom` | `count`, `pool`, `zone` ∈ collection / deck / discard / opponentDiscard, `place` ∈ same / each / random; collection may have a player `target`; opponentDiscard transfers the creature to the caster |
| `tutor` | `count`, `zone` ∈ deck / discard, `pool` (draw a random matching card; `cardId` matches one installed resource) |
| `invade` | `count` |
| `heal` (creature) | `amount`, card `target` (amount 0 = heal fully) |
| `randomKeyword` | `count`, `target` (gains that many random keywords) |
| `sacrifice` | `target` (destroy as a sacrifice) |
| `cost` | `amount` (negative = costs less), `target` (usually `zone: "hand"`), optional `temporary` |
| `extraAttack` | `target` (may attack an extra time this turn) |
| `steal` | chosen `target` (take control of it in its lane) |
| `shuffleToken` | `count`, `token` (shuffle copies of an installed card into your deck) |

Damage, healing, and nonnegative stat effects on `SelfDamaged`, `SelfDamagedByCreature`, or `HealthGained` may use `valueFrom: "eventValue"` instead of `amount`; for stats the attack/health fields are per-unit multipliers.
Damage or nonnegative stats may use `valueFrom: "turnDeaths"` to snapshot total creature deaths across both sides this turn. Damage is applied as one native hit.
Nonnegative stats may use `valueFrom: "enemyRunesLost"` to scale by the opposing player's destroyed runes.
Damage or draw may use `valueFrom: "selfPower"` for the source's current power at resolution.

Optional `condition` on any effect:
- `{"type":"equipped"}`: source has a native attached item.
- `{"type":"selfKeyword","keyword":"Ward"}`: source currently has the given keyword.
- `{"type":"turn","who":"self"|"opponent"}`: whose turn it is.
- `{"type":"health","min":N}`: owner's avatar current health, not maximum health.
- `{"type":"maxMagicka","min":N}`
- `{"type":"moreHealth"}`
- `{"type":"creaturesDiedThisTurn","min":N}`: creature deaths on both sides since the current turn began.
- `{"type":"friendlyDiedThisTurn","min":N}`: friendly creature deaths since the current turn began.
- `{"type":"opponentMoreHand"}`: the opponent currently has more cards in hand than the source's controller.
- `{"type":"enemyDiedThisTurn","min":N}`: enemy creature deaths since the current turn began. For Activate, an `activationCondition` on the ability controls whether activation is offered.
- `{"type":"opponentDestroyedRune"}`
- `{"type":"otherCreatures","min":N,"attribute":null|"Strength"|...}`
- `{"type":"creatureEachLane"}`
- `{"type":"count","target":<card target>,"min":N}` or `"max":N`: "if you have another Orc" = count of `{"kind":"card","pick":"all","side":"friendly","tribe":"Orc","other":true}` with min 1; "if you have no cards in hand" = count of `{..."zone":"hand","cardType":"Card","side":"friendly"}` with max 0

## Target

- Player: `{"kind":"player","who":"self"|"opponent"|"both"}`
- The event card / event target in event triggers: `{"kind":"inciter"}` / `{"kind":"eventTarget"}`
- The source card: `{"kind":"self"}`
- Card: `{"kind":"card","pick":"choose"|"random"|"all","side":"enemy"|"friendly"|"any", ...}`, with optional:
  - `"cardType":"Avatar"`: a player avatar only;
  - `"lane":"this"`: the source's lane;
  - `"lane":"other"`: the other creature lane;
  - `"other":true`: excludes the source;
  - `"avatar":true`: players' avatars are also eligible ("an enemy", "all enemies", "everything");
  - `"wounded":true`;
  - `"tribe":"<CardSubtype>"`;
  - `"power":["less"|"more",N]`;
  - `"zone":"board"|"hand"|"deck"|"discard"` (default board);
  - `"cardType":"Creature"|"Support"|"Item"|"Spell"|"Card"` (default Creature; "Card" = any);
  - `"cardId":N` matches a specific installed card resource, including copies in another zone;
  - `"keyword":<keyword>` (has that keyword).

`per` uses the same card-target shape and counts matching cards on the board (e.g. "for each other friendly creature" = `{"kind":"card","pick":"all","side":"friendly","other":true}`).

## Pool

`{"types":[CardSubtype...],"costMin":N|null,"costMax":N|null,"keyword":AttributeType|null}`. Types use native names:
- card kinds: Creature, Spell (= action), Item, Support, Shout, Elixir;
- races and creature types: Orc, Nord, Dark_Elf, Wood_Elf, High_Elf, Khajiit, Argonian, Redguard, Imperial, Breton, Daedra, Dwemer, Animal, Atronach, Dragon, Skeleton, Spider, Undead, Vampire, Werewolf, Goblin, Spriggan, Beast, Mummy, Spirit, Reachman, Fish, Kwama, Falmer, Troll, Giant, Netch, Harpy, Imp, Wraith, Factotum, Fabricant, Automaton, Mammoth, Reptile, Insect, Minotaur, Gargoyle, Centaur, Recruit, Wolf.

"Draw" without "from your discard pile" means from your own deck.

## Not expressible yet (tag these instead of approximating)

Aura conditions are emitted into native requirements. Stats Aura supports `per` board counts through native stack counts; keyword Aura `per` and all Aura `temporary` remain unsupported. Ward/equipment conditions and changing counts are runtime-tested.

`consume: true` on a single Creature/Item Summon or Pilfer ability opens native TargetCemetery selection of own discarded creatures. Its effects are the reward after native Consume; decline/empty discard grants nothing. One independent chosen board target is supported: native Consume completes before a separate RequiredTarget reward prompt. With no legal target it resolves automatically without a target. Multiple independent targets and other Consume trigger kinds still require implementation. Pilfer uses a single outer native trigger and private neutral choice phases so journal/quest/match stats count once. Item effects use the wielder as source. The consumed instance is EventTarget in the queued reward, while CustomUITarget is cleared with the cemetery display. Six `SelfConsumed` translations resolve synchronously after the cemetery move and before consumer rewards, using native Instant; this ordering is reconstructed with reference corroboration. Chosen-target SelfConsumed would need queued resolution and has not been translated/tested. No original per-card graph recovery is claimed.

For cards needing any of these, report the mechanic; do not approximate:
- Consume variants outside the supported Summon/consumed-card cases above, Exalt, Treasure Hunt, Beast Form, Wax/Wane, Assemble, Shout levels, Rally, Empower;
- copies, discard, choose-one cards, "while" (continuous conditional) effects other than Aura;
- "if/instead" alternatives other than the conditions listed;
- "for each" values other than counting board cards;
- set stats, swap, prophecy/rune effects, damage redirection, "can't" rules, and win/lose or scripted story cards.

## Captured Consume values (2026-10-15)

Damage/heal/stats may replace amount with `valueFrom: "consumedPower"` or `"consumedHealth"`, only inside consume:true. Do not combine with amount/per or mix value kinds within one ability. For stats, attack/health are nonnegative multipliers (Bone Armor: attack0,health1,consumedHealth). Native Attack/MaxHealth are captured before Consume and its reactions; native TriggerValue carries that immutable per-resolution amount. Consumed health is not damaged CurrentHealth or printed base health. Creature healing with a captured zero does not become full-heal.

`{"action":"stats","valueFrom":"consumedStats","attack":1,"health":1,"target":{"kind":"self"}}` is supported as the sole reward effect with no temporary/condition. It captures both attributes before Consume and applies native stat buffs afterward. No mutable snapshot is stored in a shared graph. This restores Senche-raht Graveprowler; its real Pilfer tests assert one match-stat increment for selection, decline and empty grave.
