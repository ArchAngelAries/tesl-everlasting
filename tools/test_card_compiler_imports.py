"""Regression checks for compiler ownership using actual installed card metadata."""
import csv
import json
import subprocess
import sys
import tempfile
from pathlib import Path

root = Path(__file__).resolve().parent.parent
metadata = root / 'exports/original-card-metadata-audit.tsv'
ids = {'157797006', '-503969220', '1690856288', '-761328653'}
with metadata.open(encoding='utf-8', newline='') as f:
    reader = csv.DictReader(f, delimiter='\t')
    fields = reader.fieldnames
    rows = [row for row in reader if row['id'] in ids]
assert {row['id'] for row in rows} == ids

with tempfile.TemporaryDirectory() as temp:
    fixture = Path(temp)
    (fixture / 'exports').mkdir()
    with (fixture / 'exports/original-card-metadata-audit.tsv').open('w', encoding='utf-8', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=fields, delimiter='\t')
        writer.writeheader()
        writer.writerows(rows)
    imports = fixture / 'imports.tsv'
    imports.write_text('card_id\tresult\n'
                       '157797006\tsuperseded\n157797006\treconstructed-spec-compiled\n'
                       '-503969220\tsuperseded\n-503969220\treconstructed-spec-compiled\n'
                       '1690856288\tstaged\n-761328653\tstaged\n', encoding='utf-8')
    output = fixture / 'specs.json'
    subprocess.run([sys.executable, '-I', str(root / 'tools/card_compiler.py'), str(fixture),
                    str(imports), str(output), str(fixture / 'report.tsv')],
                   check=True, stdout=subprocess.DEVNULL)
    actual = {str(card['id']) for card in json.loads(output.read_text(encoding='utf-8'))}
    assert actual == ids - {'-761328653'}, actual
    # An active non-spec graph takes precedence even when a superseded row exists.
    with imports.open('a', encoding='utf-8') as f:
        f.write('157797006\toriginal-native-rules\n')
    subprocess.run([sys.executable, '-I', str(root / 'tools/card_compiler.py'), str(fixture),
                    str(imports), str(output), str(fixture / 'report.tsv')],
                   check=True, stdout=subprocess.DEVNULL)
    actual = {str(card['id']) for card in json.loads(output.read_text(encoding='utf-8'))}
    assert actual == ids - {'-761328653', '157797006'}, actual
print('compiler import ownership: PASS')
