"""One bounded four-callback, three-runtime mechanism diagnostic."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import time
from run import records, interval, med
from jit_visibility import observe

CASES = ('Int32.raw', 'Int32Separate.raw', 'Int32.empty', 'Int32Separate.empty')
ORDERS = ('ABC', 'CBA', 'BCA', 'ACB', 'CAB', 'BAC', 'ABC', 'CBA')
CASE_ORDERS = ((0, 1, 2, 3), (3, 2, 1, 0), (1, 0, 3, 2), (2, 3, 0, 1)) * 2


def paired_ratios(data, name, numerator, denominator):
    runs = data[name]['runs']
    return [med(runs[numerator + str(i)], 'ns') / med(runs[denominator + str(i)], 'ns') for i in range(8)]


def interaction(data, shared, separate, numerator, denominator):
    return [a / b for a, b in zip(paired_ratios(data, shared, numerator, denominator),
                                 paired_ratios(data, separate, numerator, denominator))]


def main():
    parser = argparse.ArgumentParser()
    for side in ('a', 'b', 'c', 'work'): parser.add_argument('--' + side, type=Path, required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    roots = {side.upper(): getattr(args, side) for side in ('a', 'b', 'c')}
    cpu = min(os.sched_getaffinity(0))
    if cpu >= 63: raise RuntimeError('Unsupported affinity')
    hashes = {side: {key: hashlib.sha256((root / file).read_bytes()).hexdigest().upper()
                    for key, file in [('harness', 'Perf.dll'), ('core', 'SharpPack.Core.dll'), ('streaming', 'SharpPack.Streaming.dll')]}
              for side, root in roots.items()}
    if len({h['harness'] for h in hashes.values()}) != 1 or len({h['core'] for h in hashes.values()}) != 3:
        raise RuntimeError('Unexpected binary identity')
    data, environments, schedule, visibility, summaries = {}, {}, [], {}, {}

    def execute(name, side, label, counts=None):
        root = roots[side]
        dump = (args.work / (label + '.asm')).resolve()
        env = dict(os.environ, DOTNET_JitDisasm='*RunBatch*', DOTNET_JitStdOutFile=str(dump))
        command = ['taskset', '-c', str(cpu), 'dotnet', str(root / 'Perf.dll'), '--group', 'aggregate',
                   '--case', name, '--warmup', '2000']
        command += ['--calibrate', 'yes'] if counts is None else ['--counts', str(counts)]
        begin = time.time()
        result = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        text = result.stdout.decode(errors='replace')
        (args.work / (label + '.log')).write_text(text)
        if result.returncode: raise RuntimeError('Callback process failed: ' + label)
        envs = [json.loads(line[4:]) for line in text.splitlines() if line.startswith('ENV ')]
        if len(envs) != 1: raise RuntimeError('Missing process environment')
        observed = envs[0]
        if any(observed[key] != value for key, value in hashes[side].items()):
            raise RuntimeError('Loaded binary identity mismatch')
        if (observed['tieredCompilation'] != '1' or observed['tieredPGO'] != '1' or
            observed['jitDisasm'] != '*RunBatch*' or observed['affinity'] != 1 << cpu or
            observed['selectedCase'] != name or observed['group'] != 'aggregate' or
            observed['warmupMilliseconds'] != 2000 or observed['limited'] != (side != 'A')):
            raise RuntimeError('Runtime settings mismatch')
        key = name + ':' + side
        if key in environments and observed != environments[key]: raise RuntimeError('Metadata drift')
        environments[key] = observed
        rows = records(text, 'CAL' if counts is None else 'RESULT')
        if set(rows) != {name}: raise RuntimeError('Unexpected case inventory')
        row = rows[name]
        if counts is not None:
            try: raw = dump.read_bytes()
            except OSError: raw = None
            observation = observe(raw, row.get('jitBytesBefore'), row.get('jitBytesAfter'))
            summaries.update(observation.pop('summaries'))
            visibility[label] = observation
        schedule.append({'label': label, 'case': name, 'side': side,
                         'startedUtcEpoch': begin, 'seconds': time.time() - begin})
        return row

    for index, name in enumerate(CASES):
        cal = {side: execute(name, side, f'{index}-cal-{side}') for side in roots}
        if len({row['PayloadHash'] for row in cal.values()}) != 1: raise RuntimeError('Payload mismatch')
        count = max(row['iterations'] for row in cal.values())
        if not 128 <= count <= 100_000_128: raise RuntimeError('Invalid calibration')
        path = args.work / (str(index) + '-counts.json')
        path.write_text(json.dumps({name: count}))
        data[name] = {'count': count, 'countsPath': str(path), 'hash': cal['A']['PayloadHash'], 'runs': {}}
    for block in range(8):
        for index in CASE_ORDERS[block]:
            name = CASES[index]
            item = data[name]
            for side in ORDERS[block]:
                key = side + str(block)
                item['runs'][key] = execute(name, side, f'{index}-{key}', Path(item['countsPath']))
            if block in (2, 6):
                for side in 'BC':
                    for repeat in (1, 2):
                        key = f'control{side}{block}-{repeat}'
                        item['runs'][key] = execute(name, side, f'{index}-{key}', Path(item['countsPath']))
        print('Callback block completed: ' + str(block), flush=True)
    for item in data.values():
        for row in item['runs'].values():
            if row['iterations'] != item['count'] or row['PayloadHash'] != item['hash']:
                raise RuntimeError('Iteration/payload drift')
            for field in ('ns', 'threadBytes', 'totalBytes'): med(row, field)
    (args.work / 'data.json').write_text(json.dumps({'data': data, 'environments': environments,
        'schedule': schedule, 'visibility': visibility, 'jitSummaries': summaries}))
    output = ['# Bounded callback-shape diagnostic',
        'A=dev6666ada; B=originalPR56113519; C=rejectedResetff724136, diagnostic contrast only. No production modification.',
        'One identical IL harness. Four cases interleaved within eight balanced ABC blocks; B/B and C/C controls after blocks2/6; five samples; same counts; all samples retained.',
        'JIT observation is ON in these timing processes. File-prefix visibility is not proof of executing tier. No unknown process is excluded.',
        '| Case | B/A [95%] | C/A [95%] | C/B [95%] | A/B/C thread B/op | B/B controls | C/C controls |',
        '|---|---:|---:|---:|---:|---:|---:|']
    for name, item in data.items():
        ratios = []
        for numerator, denominator in [('B', 'A'), ('C', 'A'), ('C', 'B')]:
            point, low, high = interval(paired_ratios(data, name, numerator, denominator))
            ratios.append(f'{point:.4f} [{low:.4f},{high:.4f}]')
        allocation = '/'.join(f'{statistics.median(med(item["runs"][side + str(i)], "threadBytes") for i in range(8)):.3f}' for side in roots)
        controls = ['/'.join(f'{med(item["runs"][f"control{side}{i}-2"], "ns") / med(item["runs"][f"control{side}{i}-1"], "ns"):.4f}' for i in (2, 6)) for side in 'BC']
        output.append('| ' + name + ' | ' + ' | '.join(ratios + [allocation] + controls) + ' |')
    output += ['', '## Predeclared shape interactions', '| Comparison | shared effect / separate effect [95%] |', '|---|---:|']
    for label, shared, separate, numerator, denominator in [
        ('Rejected C/B raw', 'Int32.raw', 'Int32Separate.raw', 'C', 'B'),
        ('Original B/A context', 'Int32.empty', 'Int32Separate.empty', 'B', 'A')]:
        point, low, high = interval(interaction(data, shared, separate, numerator, denominator))
        output.append(f'| {label} | {point:.4f} [{low:.4f},{high:.4f}] |')
    output += ['', '## Every process median and allocation/GC observation',
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
            output.append(f'| {name}/{side} | {item["count"]} | {times} | ' + ' | '.join(ranges) + ' | ' + gc + ' |')
    output += ['', '## Same-binary absolute controls', '| Case/side | control2-1/2-2/6-1/6-2 ns |', '|---|---|']
    for name, item in data.items():
        for side in 'BC':
            times = [med(item['runs'][f'control{side}{i}-{repeat}'], 'ns') for i in (2, 6) for repeat in (1, 2)]
            output.append('| ' + name + '/' + side + ' | ' + ','.join(f'{x:.3f}' for x in times) + ' |')
    output += ['', '## Same-process JIT output visibility',
        'Complete listings visible before/within/after the snapshot window are observations only. Buffered output and unobserved callees remain limitations.',
        '| Case/side | main processes | Non-OSR Tier1 listing visible before timing | any later-visible listing | unknown |',
        '|---|---:|---:|---:|---:|']
    for index, name in enumerate(CASES):
        for side in roots:
            rows = [visibility[f'{index}-{side}{i}'] for i in range(8)]
            before = sum(any(summaries[v]['tier'] == 'Tier1' for v in row['before']) for row in rows)
            later = sum(bool(row['between'] or row['afterSnapshot']) for row in rows)
            unknown = sum(row['status'] != 'observed' for row in rows)
            output.append(f'| {name}/{side} | 8 | {before} | {later} | {unknown} |')
    output += ['', '## Verified environments and process order', json.dumps({'environments': environments, 'schedule': schedule})]
    text = '\n'.join(output)
    print(text, flush=True)
    for label, row in visibility.items(): print('JIT_VISIBILITY ' + json.dumps({'label': label, **row}), flush=True)
    for identity, row in summaries.items(): print('JIT_SUMMARY ' + json.dumps({'summaryId': identity, **row}), flush=True)
    (args.work / 'summary.md').write_text(text)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as stream: stream.write(text + '\n')


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print('Callback diagnosis failed: ' + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), flush=True)
        raise SystemExit(1)
