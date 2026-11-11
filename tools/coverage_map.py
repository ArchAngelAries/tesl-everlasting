#!/usr/bin/env python3
"""Re-run the compiler and inventory runtime coverage without changing runtime specs.

Static triage only: a tag is a review candidate, never proof a card is restored.
Run from the engine root: python -I tools/coverage_map.py --out exports/coverage
"""
import argparse
import collections
import csv
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess
import sys


# Whole words matter: e.g. "recover" must not be classified as Cover.
PATTERNS = {
    'copy': r'\bcop(?:y|ies)\b',
    'set-stats': r'\bset\b.{0,55}\b(?:power|health|cost)\b|\b(?:power|health)\b.{0,35}\b(?:equal|becomes?|is 1|to 1)\b',
    'consume': r'\bconsum(?:e|ed|ing)\b',
    'exalt': r'\bexalt(?:ed)?\b',
    'discard': r'\bdiscard\b',
    'choose-one': r'\bchoose\b|\bor\b',
    'wax-wane': r'\b(?:wax|wane)\b',
    'shout-levels': r'\bshout\b|\[.*?level',
    'empower': r'\bempower\b',
    'treasure-hunt': r'\btreasure hunt\b',
    'beast-form': r'\bbeast form\b',
    'assemble': r'\bassemble\b',
    'rally': r'\brally\b',
    'cost-for-each': r'\bcosts?\b.{0,90}\bfor each\b|\bfor each\b.{0,90}\bcosts?\b',
    'cost-aura': r'\b(?:costs?|costing)\b.{0,40}\b(?:less|more)\b',
    'while-condition': r'\bwhile\b|\bas long as\b',
    'hand-manipulation': r'\b(?:in|from|into|to)\b.{0,25}\bhand\b',
    'deck-manipulation': r'\b(?:top|bottom|shuffle)\b|\b(?:in|from|into|to)\b.{0,25}\bdeck\b',
    'transform': r'\btransform\b|\bchange\b.{0,40}\binto\b',
    'equip': r'\bequip(?:s|ped)?\b',
    'move': r'\bmove\b',
    'lane-effect': r'\blane\b',
    'summon-create': r'\bsummon\b|\bcreate\b',
    'dynamic-count': r'\bfor each\b|\bequal to\b|\[var:|\[add:',
    'restriction': r"\b(?:can't|cannot|must|only)\b",
    'win-condition': r'\b(?:win|lose)\b.{0,25}\bgame\b',
    'silence': r'\bsilence\b',
    'steal': r'\bsteal\b|\btake control\b',
    'banish': r'\bbanish\b',
    'damage': r'\bdamage\b',
    'heal': r'\b(?:heal|restore)\b|\bgain\b.{0,20}\bhealth\b',
    'draw': r'\bdraw\b',
    'stats-keywords': r'\b[+-]\d|[+-]\d+/[+-]?\d+|\b(?:guard|charge|ward|lethal|drain|breakthrough|cover)\b',
    'turn-event': r'\b(?:start|end) of\b|\bnext turn\b',
    'trigger-event': r'\b(?:when|whenever|after|before|pilfer|slay|last gasp|veteran|plot|expertise)\b',
}

# Static source anchors, deliberately not a claim that an entire family passes runtime tests.
SUPPORT = {
    'consume': ('partial', 'OriginalSpecRules.cs; OfflineConsumeChoices.cs; tools/SPEC-FORMAT.md', 'Summon/Pilfer cemetery choices and captured stats exist; variants still need review.'),
    'while-condition': ('partial', 'OriginalSpecRules.cs; OfflineAuraRestoration.cs', 'Stats/keyword auras and several conditions exist; other continuous effects differ.'),
    'copy': ('missing-spec-action', 'OriginalSpecRules.cs; tools/SPEC-FORMAT.md', 'No general instance-copy spec action; fixed token creation is not equivalent.'),
    'set-stats': ('missing-spec-action', 'OriginalSpecRules.cs', 'Stats action adds modifiers; absolute replacement needs separate semantics.'),
    'discard': ('partial', 'CatalogMovementRestoration.cs; DecisionRestoration.cs', 'Discard zone/events exist; voluntary hand choices/effects are separate.'),
    'exalt': ('missing-spec-mechanic', 'OriginalSpecRules.cs; tools/SPEC-FORMAT.md', 'Payment/choice/reward pipeline not exposed by the spec emitter.'),
    'choose-one': ('partial', 'OfflineConsumeChoices.cs; OriginalSpecRules.cs', 'TargetCemetery choices exist; general branch UI is distinct.'),
    'cost-aura': ('partial', 'OriginalSpecRules.cs', 'One-time cost effect exists; continuous cost aura is not emitted.'),
    'cost-for-each': ('partial', 'OriginalSpecRules.cs; tools/SPEC-FORMAT.md', 'Board-count values exist; continuous costs and other zones differ.'),
    'item-wielder-trigger': ('partial', 'OfflineItemTriggers.cs; OriginalSpecRules.cs', 'Wielder trigger forwarding exists; inspect the reward, not just the trigger.'),
}
for _family in ('wax-wane', 'shout-levels', 'empower', 'treasure-hunt', 'beast-form', 'assemble'):
    SUPPORT[_family] = ('missing-spec-mechanic', 'OriginalSpecRules.cs; tools/SPEC-FORMAT.md', 'No corresponding spec dispatch; native primitives may still be reusable.')
for _family in ('damage', 'heal', 'draw', 'stats-keywords', 'summon-create', 'transform', 'move', 'silence', 'turn-event', 'trigger-event'):
    SUPPORT[_family] = ('partial', 'OriginalSpecRules.cs', 'Base effects/triggers exist; residual text requires per-card semantic review.')


def read_tsv(path):
    with path.open(encoding='utf-8-sig', newline='') as f:
        return list(csv.DictReader(f, delimiter='\t'))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write_tsv(path, rows, fields):
    with path.open('w', encoding='utf-8', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=fields, delimiter='\t', lineterminator='\n')
        writer.writeheader()
        writer.writerows(rows)


def tags(text):
    return {key for key, pattern in PATTERNS.items() if re.search(pattern, text, re.I)}


def generate(root, baseline, out, snapshot=None):
    metadata = root / 'exports/original-card-metadata-audit.tsv'
    imports = baseline / 'rule-import-results.tsv'
    totals_file = baseline / 'rule-import-summary.tsv'
    legacy = root / 'exports/manual-worklist-4/unsupported-tags.tsv'
    emission_file = root / 'exports/spec-emission-report.tsv'
    inputs = [metadata, imports, totals_file, root / 'tools/card_compiler.py', root / 'tools/manual-specs.json', Path(__file__).resolve()]
    if legacy.exists():
        inputs.append(legacy)
    if emission_file.exists():
        inputs.append(emission_file)
    before = {str(p): digest(p) for p in inputs}
    rows = read_tsv(metadata)
    by_id = {r['id']: r for r in rows}
    if len(rows) != len(by_id):
        raise ValueError('Duplicate installed card IDs')
    imported = read_tsv(imports)
    # RuleImporter commits staged rules into supported, but superseded alone is not support.
    statuses = collections.defaultdict(set)
    for r in imported:
        if r['result'] != 'superseded':
            statuses[r['card_id']].add(r['result'])
    supported = set(statuses)
    if supported - set(by_id):
        raise ValueError('Import contains IDs absent from installed metadata')
    missing = set(by_id) - supported
    totals = {r['field']: int(r['value']) for r in read_tsv(totals_file)}
    observed = {'playable_card_ids': len(rows), 'supported_card_ids': len(supported), 'metadata_only_card_ids': len(missing)}
    if any(totals[k] != v for k, v in observed.items()):
        raise ValueError('Unique ID reconciliation differs from runtime summary: ' + repr(observed))
    out.mkdir(parents=True, exist_ok=True)
    subprocess.run([sys.executable, '-I', str(root / 'tools/card_compiler.py'), str(root), str(imports), str(out / 'compiled-card-specs.json'), str(out / 'compiled-card-report.tsv')], check=True, stdout=subprocess.PIPE, text=True)
    specs = {str(c['id']): c for c in json.loads((out / 'compiled-card-specs.json').read_text(encoding='utf-8'))}
    if missing - set(specs):
        raise ValueError('Compiler did not revisit all metadata-only IDs')
    module_spec = importlib.util.spec_from_file_location('card_compiler', root / 'tools/card_compiler.py')
    compiler = importlib.util.module_from_spec(module_spec)
    sys.dont_write_bytecode = True
    module_spec.loader.exec_module(compiler)
    old_tags = {}
    emission = {r['card_id']: r for r in read_tsv(emission_file)} if emission_file.exists() else {}
    if legacy.exists():
        with legacy.open(encoding='utf-8-sig', newline='') as f:
            old_tags = {r[0]: r[2] for r in csv.reader(f, delimiter='\t') if len(r) >= 4}
    coverage, ledger = [], []
    mentions, blockers, sole = collections.Counter(), collections.Counter(), collections.Counter()
    for card_id, row in sorted(by_id.items(), key=lambda x: (x[1]['name'], int(x[0]))):
        partial = statuses.get(card_id) == {'staged'}
        ledger.append({'id': card_id, 'name': row['name'], 'coverage': 'metadata-only' if card_id in missing else 'supported-partial-development' if partial else 'supported', 'import_status': '|'.join(sorted(statuses.get(card_id, [])))})
        if card_id not in missing:
            continue
        spec = specs[card_id]
        text = compiler.normalize(row['description'])
        mentioned = tags(text)
        remaining = ' || '.join(spec['unparsed'])
        identified = tags(remaining) if remaining else set()
        if row['definition'] == 'Item' and re.search(r'\b(slay|pilfer|last gasp|veteran|summon)\b', text, re.I):
            mentioned.add('item-wielder-trigger')
            if remaining:
                identified.add('item-wielder-trigger')
        if row['definition'] == 'Double':
            identified.add('double-card')
        if not text.strip():
            identified.add('missing-description')
        elif spec['status'] == 'compiled':
            identified.add('compiled-not-runtime-supported')
        elif not identified:
            identified.add('unclassified')
        mentions.update(mentioned)
        blockers.update(identified)
        if len(identified) == 1:
            sole.update(identified)
        coverage.append({'id': card_id, 'name': row['name'], 'definition': row['definition'], 'source': row['source'], 'completion': row['completion'], 'compiler_status': spec['status'], 'mentioned_families': '|'.join(sorted(mentioned)), 'candidate_blockers': '|'.join(sorted(identified)), 'sole_identified_family': next(iter(identified)) if len(identified) == 1 else '', 'historical_tags_unverified': old_tags.get(card_id, ''), 'last_export_emission_result': emission.get(card_id, {}).get('result', ''), 'last_export_emission_detail': emission.get(card_id, {}).get('detail', ''), 'unparsed': remaining, 'text': text})
    ranking = []
    for family in sorted(set(mentions) | set(blockers), key=lambda t: (-sole[t], -blockers[t], -mentions[t], t)):
        support, source, note = SUPPORT.get(family, ('unreviewed', '', 'Static classification only; inspect native code and exact card behavior.'))
        ranking.append({'family': family, 'mentions': mentions[family], 'candidate_blockers': blockers[family], 'sole_identified_family': sole[family], 'engine_support_static': support, 'source_evidence': source, 'note': note})
    write_tsv(out / 'card-coverage.tsv', ledger, ['id', 'name', 'coverage', 'import_status'])
    write_tsv(out / 'metadata-only.tsv', coverage, list(coverage[0]))
    write_tsv(out / 'mechanic-worklist.tsv', ranking, list(ranking[0]))
    staged = [r for r in ledger if r['coverage'] == 'supported-partial-development']
    summary = {
        **observed,
        'supported_nonstaged': len(supported) - len(staged),
        'supported_partial_development': staged,
        'metadata_only_compiler_status': dict(sorted(collections.Counter(c['compiler_status'] for c in coverage).items())),
        'metadata_only_definitions': dict(sorted(collections.Counter(c['definition'] for c in coverage).items())),
        'metadata_only_completion': dict(sorted(collections.Counter(c['completion'] for c in coverage).items())),
        'historical_tag_overlap': len(missing & set(old_tags)),
        'historical_tag_missing': len(missing - set(old_tags)),
        'classification': 'Regex triage of normalized text and unparsed clauses; overlapping families; sole identified family is not an unlock promise. Historical tags are unverified hints only.',
        'baseline': str(baseline),
        'inputs_sha256': before,
        'runtime_retested': False,
        'emission_evidence': 'Optional live exports/spec-emission-report.tsv is hashed separately; not present in checkpoint 23, not regenerated or asserted to be from that run.',
    }
    if any(digest(Path(p)) != h for p, h in before.items()):
        raise ValueError('Inputs changed during generation; retry against a stable checkpoint')
    (out / 'summary.json').write_text(json.dumps(summary, indent=2) + '\n', encoding='utf-8')
    lines = ['# Card coverage pilot - 2026-10-24', '', 'Static inventory against checkpoint 23; no engine behavior changed or runtime tests run.', '', f"Playable **{len(rows)}** = runtime-supported **{len(supported)}** + metadata-only **{len(missing)}**.", f"Supported includes **{len(staged)} partial-development entries**; non-staged supported: **{len(supported)-len(staged)}**.", '']
    lines += [f"- {r['name']} ({r['id']}): staged partial development rules, counted supported by RuleImporter." for r in staged]
    lines += ['', '## Reproduction and evidence', '', 'Run from the engine folder: `python -I tools/coverage_map.py --out exports/coverage --snapshot ORIGINAL-COVERAGE-PILOT.md`.', '', '`exports/coverage/summary.json` records hashes for installed metadata, baseline imports, compiler, manual specs, and this generator. Runtime specs in exports/compiled-card-specs.json are not overwritten.', '', 'Full ID ledger: `exports/coverage/card-coverage.tsv`. Every missing card: `metadata-only.tsv`. Family ranking: `mechanic-worklist.tsv`.', '', '## Method and limits', '', 'Mentions are regex matches on normalized full text. Candidate blockers are matches on compiler-unparsed clauses, plus explicit structural/no-text states. A sole identified family means only one classifier family was found, not that all dependencies were identified. Counts overlap; never sum family counts into coverage or promise that implementing a family unlocks that many cards.', '', 'Historical tags are retained separately as unverified hints, never used to determine current classifications. This deliberately avoids claiming an old tag proves missing engine support. Existing native primitives and trigger support must be checked per card.', '', 'Static engine-support labels cite inspected source files, not new runtime verification. Cards with unclassified text, missing descriptions or compiled-but-unemitted specs need manual review before assigning engineering work.', '', '## Compiler state among metadata-only cards', '']
    lines += [f'- {k}: {v}' for k, v in summary['metadata_only_compiler_status'].items()]
    lines += ['', '### Compiled but not runtime-supported', '', 'The optional last-export emission report is separately hashed. It is not part of checkpoint 23 and was not regenerated. Reasons below are triage evidence, not fresh runtime results.', '', '| Card | ID | Last-export emitter result | Detail |', '|---|---:|---|---|']
    lines += [f"| {r['name']} | {r['id']} | {r['last_export_emission_result'] or 'unavailable'} | {r['last_export_emission_detail']} |" for r in coverage if r['compiler_status'] == 'compiled']
    lines += ['', '## Ranked triage (sole identified family, then candidate count)', '', '| Family | Mentions | Candidate blockers | Sole identified family | Static support |', '|---|---:|---:|---:|---|']
    lines += [f"| {r['family']} | {r['mentions']} | {r['candidate_blockers']} | {r['sole_identified_family']} | {r['engine_support_static']} |" for r in ranking]
    lines += ['', '## Prioritization', '', 'Review unclassified and compiled-but-unsupported records first to avoid hiding work behind coarse tags. Then examine item/wielder rewards and existing Consume/while-condition paths for translations using established mechanics. Absolute set-stats and instance-copy effects are distinct emitter investigations. Broad summon/draw/damage/trigger tags describe symptoms, not whole missing subsystems.', '', '## Input hashes', '']
    lines += [f'- `{Path(p).name}`: `{h}`' for p, h in before.items()]
    report = '\n'.join(lines) + '\n'
    (out / 'coverage-report.md').write_text(report, encoding='utf-8')
    if snapshot:
        snapshot.write_text(report, encoding='utf-8')
    print(json.dumps({k: v for k, v in summary.items() if k not in ('inputs_sha256', 'classification', 'baseline')}, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--baseline', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--snapshot', type=Path)
    args = parser.parse_args()
    baseline = args.baseline or args.root / 'checkpoints/runs/20261022-031105-claude-fable2-fixes-monthlies-219-groups-pass'
    generate(args.root.resolve(), baseline.resolve(), args.out.resolve(), args.snapshot.resolve() if args.snapshot else None)


if __name__ == '__main__':
    main()
