# Original card rule data

- `card-rule-graphs.jsonl.gz` (SHA-256 eec6c9a9d1af22032d4f65470f8a1c29e511bacb4be673fed8094758839df8e9): one installed
  CardSet per line in the game's own FullSerializer JSON form, including the card fields the shipped binary configuration
  strips (`[NonSerialized]`: TriggeredEffects, Auras, ActionPlayInformation, ActivatedAbility, PlayRequirements,
  BetrayRequirements, StrongHintRequirements, PlayWeight, HuntTypes, UpgradesInto, MobilizeRecruitId, ArenaDraftSubtypes).
  Graphs for 1,426 of 1,932 cards. Developer editor labels (`NameForEditor`), FX hashes and AI weights identify it as the
  original game data. All 219 `$type` names exist in the installed Assembly-CSharp (retail SHA-256 ab4b71b6…).
- `card-fx-lookups.tsv`: 266 FX hash → CardFxDefinition name rows the graphs reference.
- `card-source-index.tsv`: their per-card index (id, alternate, set, name, export code, has graph).

Obtained 2026-11-03 from https://github.com/crow2cr0w/tesl-offline (`data/retail-rule-graphs.jsonl.gz`,
`retail-rule-fx.tsv`, `retail-card-source-index.tsv`; commit a46dafb). The user decided on 2026-11-03 to use it. It is
Bethesda's game data: keep it a separate, user-supplied file and do not bundle it into a redistributable mod without a
decision. Loaded by `OriginalGraphRules.cs`; cards that fail our tests with it are listed in `excluded.tsv`.
