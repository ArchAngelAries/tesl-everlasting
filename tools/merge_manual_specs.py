"""Merge reviewed manual translations into tools/manual-specs.json (offline, stdlib only).

Usage: python -I merge_manual_specs.py <offline-engine dir> <worklist chunk.json> <translations.json> [<reviewer note>]
Re-validates every entry, takes name and exact raw description from the installed metadata audit (never from the
translation), keeps existing entries for other cards, and refuses to overwrite a different existing entry for the same id.
"""
import csv, json, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from validate_manual_specs import validate


def main():
    root, chunk_path, translations_path = sys.argv[1:4]
    reviewer = sys.argv[4] if len(sys.argv) > 4 else ''
    rows = {r['id']: r for r in csv.DictReader(open(os.path.join(root, 'exports', 'original-card-metadata-audit.tsv'), encoding='utf-8'), delimiter='\t')}
    chunk = {str(c['id']): c for c in json.load(open(chunk_path, encoding='utf-8'))}
    out_path = os.path.join(root, 'tools', 'manual-specs.json')
    merged = json.load(open(out_path, encoding='utf-8')) if os.path.exists(out_path) else {}
    added = rejected = kept = 0
    for cid, entry in json.load(open(translations_path, encoding='utf-8')).items():
        if cid not in chunk or cid not in rows: print('skip (not in worklist/audit):', cid); rejected += 1; continue
        errs = validate(chunk[cid], entry)
        if errs: print('skip invalid', cid, '|', '; '.join(errs)); rejected += 1; continue
        new = {'name': rows[cid]['name'], 'raw': rows[cid]['description'], 'abilities': entry['abilities'],
               'note': (entry.get('note') or '') + ((' | review: ' + reviewer) if reviewer else '')}
        if cid in merged and merged[cid]['abilities'] != new['abilities']:
            print('keep existing different entry', cid, rows[cid]['name']); kept += 1; continue
        merged[cid] = new; added += 1
    json.dump(merged, open(out_path, 'w', encoding='utf-8'), indent=1, ensure_ascii=False, sort_keys=True)
    print('added %d, rejected %d, kept existing %d, total %d' % (added, rejected, kept, len(merged)))


if __name__ == '__main__':
    main()
