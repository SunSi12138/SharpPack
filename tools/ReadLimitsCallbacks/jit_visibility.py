"""Observe byte-prefix visibility, not the runtime's active executing tier."""
import hashlib
import json
import re
from jit_summary import blocks


def prefix_versions(raw, length):
    if not isinstance(length, int) or length < 0 or length > len(raw):
        return {'status': 'unknown', 'versions': [], 'incomplete': 0}
    prefix = raw[:length]
    starts = list(re.finditer(rb'^; Assembly listing for method ', prefix, re.M))
    result, incomplete = [], 0
    for i, start in enumerate(starts):
        stop = starts[i + 1].start() if i + 1 < len(starts) else len(prefix)
        segment = prefix[start.start():stop]
        header = segment.split(b'\n', 1)[0]
        if b'Program:' not in header or b'g__RunBatch' not in header or b'System.Action,int' not in header:
            continue
        # Require the complete end-marker line, including newline. A partial
        # footer or buffered tail cannot establish pre-snapshot visibility.
        end = re.search(rb'^; Total bytes of code \d+[^\r\n]*\r?\n', segment, re.M)
        if end is None:
            incomplete += 1
            continue
        try:
            rows = blocks(segment[:end.end()].decode('utf-8'))
        except (UnicodeDecodeError, RuntimeError):
            return {'status': 'unknown', 'versions': result, 'incomplete': incomplete + 1}
        if len(rows) != 1:
            return {'status': 'unknown', 'versions': result, 'incomplete': incomplete}
        row = rows[0]
        if not any(label in row['tier'] for label in ('Tier0', 'Tier1', 'FullOpts', 'MinOpts')):
            row['tier'] = 'unknown'
        row['osr'] = 'OSR' in row['tier'].upper()
        canonical = json.dumps(row, sort_keys=True)
        result.append({'startByte': start.start(), 'endByte': start.start() + end.end(),
                       'summaryId': hashlib.sha256(canonical.encode()).hexdigest()[:20], 'summary': row})
    return {'status': 'unknown' if any(r['summary']['tier'] == 'unknown' for r in result) else 'observed',
            'versions': result, 'incomplete': incomplete}


def observe(raw, before, after):
    if raw is None or not isinstance(before, int) or not isinstance(after, int) or before < 0 or after < before or after > len(raw):
        return {'status': 'unknown', 'beforeBytes': before, 'afterBytes': after,
                'exitBytes': None if raw is None else len(raw), 'before': [], 'between': [], 'afterSnapshot': [], 'summaries': {}}
    a, b, c = prefix_versions(raw, before), prefix_versions(raw, after), prefix_versions(raw, len(raw))
    versions = c['versions']
    def ids(rows): return [r['summaryId'] for r in rows]
    return {'status': 'observed' if versions and not c['incomplete'] and all(x['status'] == 'observed' for x in (a, b, c)) else 'unknown',
            'beforeBytes': before, 'afterBytes': after, 'exitBytes': len(raw),
            'before': ids(a['versions']),
            'between': ids([r for r in versions if before < r['endByte'] <= after]),
            'afterSnapshot': ids([r for r in versions if r['endByte'] > after]),
            'incompleteBefore': a['incomplete'], 'incompleteAfter': b['incomplete'],
            'incompleteExit': c['incomplete'],
            'summaries': {r['summaryId']: r['summary'] for r in versions},
            'note': 'File visibility only. Buffered output, callsite updates, OSR and unobserved callees prevent proof of executing tier.'}
