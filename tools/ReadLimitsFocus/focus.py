"""Exploratory single-case process controls for observed matrix signals."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import time
from run import records, interval, med

CASES = [
    ('aggregate', 'Int32.raw', None),
    ('aggregate', 'Int32.empty', 'Int32.limited'),
    ('aggregate', 'Depth32.empty', 'Depth32.limited'),
    ('aggregate', 'References32.raw', None),
    ('aggregate', 'References32.empty', 'References32.limited'),
    ('aggregate', 'Mixed.utf16', None),
    ('surface', 'BytesSequence2x64.raw', None),
    ('surface', 'BytesSpan64.empty', 'BytesSpan64.limited'),
]


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--a', type=Path, required=True)
    p.add_argument('--b', type=Path, required=True)
    p.add_argument('--work', type=Path, required=True)
    p.add_argument('--tiered', choices=['0', '1'], required=True)
    args = p.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    cpu = min(os.sched_getaffinity(0))
    if cpu >= 63: raise RuntimeError('Unsupported affinity mask')
    hashes = {side: {key: hashlib.sha256((root / file).read_bytes()).hexdigest().upper()
                    for key, file in [('harness', 'Perf.dll'), ('core', 'SharpPack.Core.dll'), ('streaming', 'SharpPack.Streaming.dll')]}
              for side, root in [('A', args.a), ('B', args.b)]}
    if hashes['A']['harness'] != hashes['B']['harness'] or hashes['A']['core'] == hashes['B']['core']:
        raise RuntimeError('Unexpected harness/runtime identity')
    data, environments, schedule = {}, {}, []

    def execute(group, name, side, label, counts=None):
        root = args.a if side == 'A' else args.b
        command = ['taskset', '-c', str(cpu), 'dotnet', str(root / 'Perf.dll'), '--group', group,
                   '--case', name, '--warmup', '2000']
        command += ['--calibrate', 'yes'] if counts is None else ['--counts', str(counts)]
        begin = time.time()
        r = subprocess.run(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        text = r.stdout.decode(errors='replace')
        (args.work / (label + '.log')).write_text(text)
        if r.returncode: raise RuntimeError('Focused process failed: ' + label + '; exit=' + str(r.returncode))
        envs = [json.loads(line[4:]) for line in text.splitlines() if line.startswith('ENV ')]
        if len(envs) != 1: raise RuntimeError('Missing focused environment')
        env = envs[0]
        expected = hashes['A' if side == 'A' else 'B']
        if any(env[k] != expected[k] for k in expected): raise RuntimeError('Loaded focused binary mismatch')
        if (env['tieredCompilation'] != args.tiered or env['tieredPGO'] != args.tiered or
            env['selectedCase'] != name or env['group'] != group or env['warmupMilliseconds'] != 2000 or
            env['affinity'] != 1 << cpu or env['limited'] != (side != 'A')):
            raise RuntimeError('Focused runtime settings mismatch')
        rows = records(text, 'CAL' if counts is None else 'RESULT')
        if set(rows) != {name}: raise RuntimeError('Focused process measured extra/missing cases')
        environments[label] = env
        schedule.append({'label': label, 'side': side, 'name': name, 'startedUtcEpoch': begin, 'seconds': time.time() - begin})
        return rows[name]

    for case_index, (group, name, limited) in enumerate(CASES):
        sides = {'A': name, 'B': name}
        if limited: sides['C'] = limited
        cal = {side: execute(group, selected, side, f'{case_index}-cal-{side}') for side, selected in sides.items()}
        if len({x['PayloadHash'] for x in cal.values()}) != 1:
            raise RuntimeError('Focused matched payload bytes differ')
        count = max(x['iterations'] for x in cal.values())
        if count < 128 or count > 100_000_128: raise RuntimeError('Focused iteration count invalid')
        counts = args.work / f'{case_index}-counts.json'
        counts.write_text(json.dumps({selected: count for selected in sides.values()}))
        runs = {}
        for block in range(8):
            order = ('ABC', 'CBA', 'BCA', 'ACB', 'CAB', 'BAC', 'ABC', 'CBA')[block] if limited else ('AB' if block % 2 == 0 else 'BA')
            for side in order:
                label = f'{case_index}-{side}{block}'
                runs[side + str(block)] = execute(group, sides[side], side, label, counts)
            if block in (2, 6):
                for repeat in (1, 2):
                    key = f'control{block}-{repeat}'
                    runs[key] = execute(group, name, 'A', f'{case_index}-{key}', counts)
        for key, row in runs.items():
            if row['iterations'] != count or row['PayloadHash'] != cal['A']['PayloadHash']:
                raise RuntimeError('Focused iteration/payload drift')
            med(row, 'ns'); med(row, 'threadBytes'); med(row, 'totalBytes')
        data[name] = {'count': count, 'limited': limited, 'runs': runs}
        print('Focused case complete: ' + name, flush=True)
    (args.work / 'focused-data.json').write_text(json.dumps({'data': data, 'environments': environments, 'schedule': schedule}))
    summary = [f'# Isolated read-limit diagnosis, tiered={args.tiered}',
        'A dev6666ada; B exact PR3056113519; C same candidate with configured limits (exact boundaries where applicable). Same managed harness IL across revisions.',
        'Exploratory cases selected after the broad matrix. Each process measures one case, warmed through the same non-inlined batch function for2s. Eight pairs/triples and two same-binary pairs per case.',
        'ON/OFF tiering modes are separate hosted jobs, not the same physical host. This diagnoses stable signals; it cannot isolate tiering causality across hosts or replace production-mode evidence.',
        'All processes retained.95% percentile intervals resample8 process log ratios, are per-case/unadjusted for multiple comparisons, and do not establish a general no-regression claim.',
        '| Case | B/A [within-host95% interval] | C/B [within-host95% interval] | A/B/C median thread B/op | AA ratios |',
        '|---|---:|---:|---:|---:|']
    for name, item in data.items():
        runs = item['runs']
        ratios = [med(runs['B' + str(i)], 'ns') / med(runs['A' + str(i)], 'ns') for i in range(8)]
        p, low, high = interval(ratios)
        first = f'{p:.4f} [{low:.4f},{high:.4f}]'
        second = 'not tested'
        sides = ['A', 'B']
        if item['limited']:
            sides.append('C')
            p, low, high = interval([med(runs['C' + str(i)], 'ns') / med(runs['B' + str(i)], 'ns') for i in range(8)])
            second = f'{p:.4f} [{low:.4f},{high:.4f}]'
        allocated = '/'.join(f'{statistics.median(med(runs[side + str(i)], "threadBytes") for i in range(8)):.3f}' for side in sides)
        controls = '/'.join(f'{med(runs[f"control{i}-2"], "ns") / med(runs[f"control{i}-1"], "ns"):.4f}' for i in (2, 6))
        summary.append(f'| {name} | {first} | {second} | {allocated} | {controls} |')
    summary += ['', '## Every process median and GC/allocation observations',
                '| Case/side | iterations/sample | process0..7 median ns | thread B/op range | total B/op range | GC0,1,2 totals |',
                '|---|---:|---|---:|---:|---:|']
    for name, item in data.items():
        for side in ['A', 'B'] + (['C'] if item['limited'] else []):
            rows = [item['runs'][side + str(i)] for i in range(8)]
            medians = ','.join(f'{med(row, "ns"):.3f}' for row in rows)
            ranges = []
            for field in ['threadBytes', 'totalBytes']:
                values = [s[field] for row in rows for s in row['samples']]
                ranges.append(f'{min(values):.3f}–{max(values):.3f}')
            gc = ','.join(str(sum(s['gen' + str(g)] for row in rows for s in row['samples'])) for g in range(3))
            summary.append(f'| {name}/{side} | {item["count"]} | {medians} | ' + ' | '.join(ranges) + ' | ' + gc + ' |')
    summary += ['', '## Same-binary control absolute medians', '| Case | control2-1/2-2/6-1/6-2 ns/op |', '|---|---|']
    for name, item in data.items():
        values = [med(item['runs'][f'control{i}-{n}'], 'ns') for i in (2, 6) for n in (1, 2)]
        summary.append('| ' + name + ' | ' + ','.join(f'{x:.3f}' for x in values) + ' |')
    summary += ['', '## Verified process metadata/order', json.dumps({'environments': environments, 'schedule': schedule})]
    text = '\n'.join(summary)
    print(text, flush=True)
    (args.work / 'summary.md').write_text(text)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output: output.write(text + '\n')


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print('Focused diagnosis failed: ' + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), flush=True)
        raise SystemExit(1)
