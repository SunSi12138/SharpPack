"""One-change candidate versus pinned PR and production baseline."""
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
    ('aggregate', 'Int32.raw'), ('aggregate', 'Int32.empty'),
    ('aggregate', 'Mixed.utf16'), ('aggregate', 'References32.raw'),
    ('aggregate', 'References32.empty'), ('surface', 'BytesSpan64.empty'),
    ('surface', 'BytesSequence2x64.empty'), ('surface', 'BytesSpan65536.empty'),
    ('surface', 'BytesSpan65536.custom'), ('surface', 'WriteBuffer64.empty'),
]


def main():
    parser = argparse.ArgumentParser()
    for side in ('a', 'b', 'c', 'work'): parser.add_argument('--' + side, type=Path, required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    roots = {side.upper(): getattr(args, side) for side in ('a', 'b', 'c')}
    cpu = min(os.sched_getaffinity(0))
    if cpu >= 63: raise RuntimeError('Unsupported affinity mask')
    hashes = {side: {key: hashlib.sha256((root / file).read_bytes()).hexdigest().upper()
                    for key, file in [('harness', 'Perf.dll'), ('core', 'SharpPack.Core.dll'), ('streaming', 'SharpPack.Streaming.dll')]}
              for side, root in roots.items()}
    if len({row['harness'] for row in hashes.values()}) != 1 or len({row['core'] for row in hashes.values()}) != 3:
        raise RuntimeError('Unexpected harness/runtime identity')
    data, environments, schedule = {}, {}, []

    def execute(group, name, side, label, counts=None):
        root = roots[side]
        command = ['taskset', '-c', str(cpu), 'dotnet', str(root / 'Perf.dll'), '--group', group,
                   '--case', name, '--warmup', '2000']
        command += ['--calibrate', 'yes'] if counts is None else ['--counts', str(counts)]
        begin = time.time()
        result = subprocess.run(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        text = result.stdout.decode(errors='replace')
        (args.work / (label + '.log')).write_text(text)
        if result.returncode: raise RuntimeError('Three-way process failed: ' + label)
        envs = [json.loads(line[4:]) for line in text.splitlines() if line.startswith('ENV ')]
        if len(envs) != 1: raise RuntimeError('Missing process environment')
        env = envs[0]
        if any(env[key] != expected for key, expected in hashes[side].items()):
            raise RuntimeError('Loaded binary mismatch')
        if (env['tieredCompilation'] != '1' or env['tieredPGO'] != '1' or env['selectedCase'] != name or
            env['group'] != group or env['warmupMilliseconds'] != 2000 or env['affinity'] != 1 << cpu or
            env['limited'] != (side != 'A')):
            raise RuntimeError('Runtime settings mismatch')
        rows = records(text, 'CAL' if counts is None else 'RESULT')
        if set(rows) != {name}: raise RuntimeError('Measured extra/missing cases')
        environments[label] = env
        schedule.append({'label': label, 'side': side, 'name': name, 'startedUtcEpoch': begin, 'seconds': time.time() - begin})
        return rows[name]

    for case_index, (group, name) in enumerate(CASES):
        cal = {side: execute(group, name, side, f'{case_index}-cal-{side}') for side in roots}
        if len({row['PayloadHash'] for row in cal.values()}) != 1: raise RuntimeError('Payload bytes differ')
        count = max(row['iterations'] for row in cal.values())
        if not 128 <= count <= 100_000_128: raise RuntimeError('Invalid iteration count')
        counts = args.work / f'{case_index}-counts.json'
        counts.write_text(json.dumps({name: count}))
        runs = {}
        for block, order in enumerate(('ABC', 'CBA', 'BCA', 'ACB', 'CAB', 'BAC', 'ABC', 'CBA')):
            for side in order:
                key = side + str(block)
                runs[key] = execute(group, name, side, f'{case_index}-{key}', counts)
            if block in (2, 6):
                for repeat in (1, 2):
                    key = f'control{block}-{repeat}'
                    runs[key] = execute(group, name, 'B', f'{case_index}-{key}', counts)
        for row in runs.values():
            if row['iterations'] != count or row['PayloadHash'] != cal['A']['PayloadHash']:
                raise RuntimeError('Iteration/payload drift')
            med(row, 'ns'); med(row, 'threadBytes'); med(row, 'totalBytes')
        data[name] = {'count': count, 'runs': runs}
        print('Three-way case complete: ' + name, flush=True)
    (args.work / 'data.json').write_text(json.dumps({'data': data, 'environments': environments, 'schedule': schedule}))
    summary = ['# Reader initialization inline experiment',
        'A dev 6666ada; B exact PR 56113519; C this validation head (only production change: AggressiveInlining on ReaderOptionalState.Init).',
        'Default tiered compilation/PGO ON. One case/process, two-second warmup through same non-inlined batch callsite, identical harness IL and iteration counts. All fixtures initialize before filtering.',
        'Eight balanced ABC triples; two same-binary B/B pairs per case. Within-host intervals resample eight paired log ratios; exploratory, unadjusted for multiple comparisons. No outlier exclusions.',
        '| Case | B/A [95%] | C/A [95%] | C/B [95%] | A/B/C thread B/op | B/B controls |',
        '|---|---:|---:|---:|---:|---:|']
    for name, item in data.items():
        runs = item['runs']
        ratios = []
        for numerator, denominator in [('B', 'A'), ('C', 'A'), ('C', 'B')]:
            point, low, high = interval([med(runs[numerator + str(i)], 'ns') / med(runs[denominator + str(i)], 'ns') for i in range(8)])
            ratios.append(f'{point:.4f} [{low:.4f},{high:.4f}]')
        allocation = '/'.join(f'{statistics.median(med(runs[side + str(i)], "threadBytes") for i in range(8)):.3f}' for side in roots)
        controls = '/'.join(f'{med(runs[f"control{i}-2"], "ns") / med(runs[f"control{i}-1"], "ns"):.4f}' for i in (2, 6))
        summary.append('| ' + name + ' | ' + ' | '.join(ratios) + ' | ' + allocation + ' | ' + controls + ' |')
    summary += ['', '## Every process median and allocation/GC observations',
                '| Case/side | iterations/sample | process0..7 ns | thread B/op range | total B/op range | GC0,1,2 totals |',
                '|---|---:|---|---:|---:|---:|']
    for name, item in data.items():
        for side in roots:
            rows = [item['runs'][side + str(i)] for i in range(8)]
            times = ','.join(f'{med(row, "ns"):.3f}' for row in rows)
            ranges = []
            for field in ('threadBytes', 'totalBytes'):
                values = [sample[field] for row in rows for sample in row['samples']]
                ranges.append(f'{min(values):.3f}–{max(values):.3f}')
            gc = ','.join(str(sum(sample['gen' + str(g)] for row in rows for sample in row['samples'])) for g in range(3))
            summary.append(f'| {name}/{side} | {item["count"]} | {times} | ' + ' | '.join(ranges) + ' | ' + gc + ' |')
    summary += ['', '## Same-binary absolute medians', '| Case | B-control2-1/2-2/6-1/6-2 ns |', '|---|---|']
    for name, item in data.items():
        times = [med(item['runs'][f'control{i}-{repeat}'], 'ns') for i in (2, 6) for repeat in (1, 2)]
        summary.append('| ' + name + ' | ' + ','.join(f'{value:.3f}' for value in times) + ' |')
    summary += ['', '## Process metadata/order', json.dumps({'environments': environments, 'schedule': schedule})]
    text = '\n'.join(summary)
    print(text, flush=True)
    (args.work / 'summary.md').write_text(text)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output: output.write(text + '\n')


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print('Three-way measurement failed: ' + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), flush=True)
        raise SystemExit(1)
