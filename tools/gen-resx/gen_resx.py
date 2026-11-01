#!/usr/bin/env python3
"""Generates Strings.resx (en-US, neutral) and Strings.fa-IR.resx from strings.tsv.
Usage: python3 tools/gen-resx/gen_resx.py   (run from the repository root)"""
import os
from xml.sax.saxutils import escape
here = os.path.dirname(os.path.abspath(__file__))
rows = []
for line in open(os.path.join(here, 'strings.tsv'), encoding='utf-8'):
    line = line.rstrip('\n')
    if not line or line.startswith('#'):
        continue
    key, en, fa = line.split('\t')
    rows.append((key, en, fa))
keys = [r[0] for r in rows]
assert len(keys) == len(set(keys)), 'duplicate keys'
HEAD = '''<?xml version="1.0" encoding="utf-8"?>
<!-- Generated from tools/gen-resx/strings.tsv. Edit the TSV and re-run gen_resx.py. -->
<root>
  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>
  <resheader name="version"><value>2.0</value></resheader>
  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>
'''
out = os.path.join(here, '..', '..', 'src', 'Naqsh.Localization', 'Resources')
for name, idx in (('Strings.resx', 1), ('Strings.fa-IR.resx', 2)):
    with open(os.path.join(out, name), 'w', encoding='utf-8', newline='\n') as f:
        f.write(HEAD)
        for r in rows:
            f.write(f'  <data name="{r[0]}" xml:space="preserve"><value>{escape(r[idx])}</value></data>\n')
        f.write('</root>\n')
print(f'{len(rows)} keys written')
