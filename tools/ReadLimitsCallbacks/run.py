"""Paired process experiment. Raw subprocess output stays on the temporary runner."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import random
import statistics
import subprocess
import time


def records(text, prefix):
    result = {}
    for line in text.splitlines():
        if line.startswith(prefix + ' '):
            row = json.loads(line[len(prefix) + 1:])
            name = row['Name']
            if name in result:
                raise RuntimeError('Duplicate measurement case')
            result[name] = row
    if not result:
        raise RuntimeError('Missing measurement records')
    return result


def interval(ratios):
    values = [math.log(x) for x in ratios]
    if not values or not all(math.isfinite(x) for x in values):
        raise RuntimeError('Nonfinite ratio')
    rng = random.Random(30)
    draws = sorted(math.exp(statistics.mean(rng.choices(values, k=len(values)))) for _ in range(5000))
    return math.exp(statistics.mean(values)), draws[124], draws[4874]


def med(row, field):
    values = [x[field] for x in row['samples']]
    if len(values) != 5 or not all(math.isfinite(x) and x >= 0 for x in values):
        raise RuntimeError('Invalid timed samples')
    result = statistics.median(values)
    if field == 'ns' and result <= 0:
        raise RuntimeError('Nonpositive timing')
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--a', type=Path, required=True)
    parser.add_argument('--b', type=Path, required=True)
    parser.add_argument('--work', type=Path, required=True)
    parser.add_argument('--group', choices=['surface', 'aggregate'], required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    cpu = sorted(os.sched_getaffinity(0))[0]
    if cpu >= 63:
        raise RuntimeError("Affinity mask exceeds supported signed range")
    expected = {side: {key: hashlib.sha256((root / file).read_bytes()).hexdigest().upper()
                       for key, file in [('harness', 'Perf.dll'), ('core', 'SharpPack.Core.dll'), ('streaming', 'SharpPack.Streaming.dll')]}
                for side, root in [('A', args.a), ('B', args.b)]}
    if expected['A']['harness'] != expected['B']['harness']:
        raise RuntimeError('Harness IL differs across revisions')
    if expected['A']['core'] == expected['B']['core']:
        raise RuntimeError('Candidate Core was not replaced')
    measurements, environments, schedule = {}, {}, []

    def execute(side, label, rotation, calibration=False):
        root = args.a if side == 'A' else args.b
        command = ['taskset', '-c', str(cpu), 'dotnet', str(root / 'Perf.dll'), '--group', args.group, '--rotation', str(rotation)]
        command += ['--calibrate', 'yes'] if calibration else ['--counts', str(args.work / 'counts.json')]
        started = time.time()
        result = subprocess.run(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=420)
        output = result.stdout.decode(errors='replace')
        (args.work / (label + '.log')).write_text(output)
        if result.returncode:
            # Never relay arbitrary exception text or local paths.
            raise RuntimeError('Benchmark process failed: ' + label + '; exit=' + str(result.returncode))
        metadata = [json.loads(line[4:]) for line in output.splitlines() if line.startswith('ENV ')]
        if len(metadata) != 1:
            raise RuntimeError('Missing process environment')
        env = metadata[0]
        if any(env[key] != expected[side][key] for key in expected[side]):
            raise RuntimeError('Loaded assembly hash mismatch')
        if env['limited'] != (side == 'B') or env['group'] != args.group or env['affinity'] != 1 << cpu or env['tieredCompilation'] != '1' or env['tieredPGO'] != '1':
            raise RuntimeError('Unexpected loaded mode/group/affinity')
        if args.group == 'aggregate' and side == 'B' and 'LIMIT_CHECKS payload-before-read,collection-before-replacement,depth,reference:passed' not in output:
            raise RuntimeError('Missing rejection controls')
        environments[label] = env
        schedule.append({'label': label, 'side': side, 'rotation': rotation, 'startedUtcEpoch': started, 'seconds': time.time() - started})
        print('Process finished: ' + label + '; seconds=' + str(round(schedule[-1]['seconds'], 2)), flush=True)
        return records(output, 'CAL' if calibration else 'RESULT')

    ca = execute('A', 'calibration-A', 0, True)
    cb = execute('B', 'calibration-B', 0, True)
    common = set(ca)
    if common != {name for name, row in cb.items() if not row['Limited']}:
        raise RuntimeError('Default-path case inventory differs')
    for name in common:
        if ca[name]['PayloadHash'] != cb[name]['PayloadHash']:
            raise RuntimeError('Common payload bytes differ')
    counts = {name: max(ca.get(name, {}).get('iterations', 0), row['iterations']) for name, row in cb.items()}
    if any(n < 128 or n > 100_000_128 for n in counts.values()):
        raise RuntimeError('Calibration out of bounds')
    (args.work / 'counts.json').write_text(json.dumps(counts))
    for block in range(8):
        rotation = block * 7
        for side in (('A', 'B') if block % 2 == 0 else ('B', 'A')):
            label = side + str(block)
            measurements[label] = execute(side, label, rotation)
        if block in (2, 6):
            for suffix in (1, 2):
                label = f'control{block}-{suffix}'
                measurements[label] = execute('A', label, rotation)

    for label, rows in measurements.items():
        expected_rows = ca if label.startswith('A') or label.startswith('control') else cb
        if set(rows) != set(expected_rows):
            raise RuntimeError('Measured case inventory changed')
        for name, row in rows.items():
            if row['iterations'] != counts[name] or row['PayloadHash'] != expected_rows[name]['PayloadHash']:
                raise RuntimeError('Measurement count or payload changed')
            med(row, 'ns'); med(row, 'threadBytes'); med(row, 'totalBytes')
    # Persist complete structured data locally, including all five timed samples and GC counts.
    (args.work / 'data.json').write_text(json.dumps({'environments': environments, 'schedule': schedule, 'counts': counts, 'measurements': measurements}))
    summary = [f'# Read-limit paired experiment: {args.group}',
        'A = dev6666ada (production sources identical to8efea5af); B = PR3056113519.',
        'Eight alternating process pairs per host.5000-resample percentile intervals use eight process-pair log ratios, not40 within-process samples.',
        'Per-case intervals are exploratory and unadjusted for many comparisons; eight pairs do not establish global95% no-regression confidence.',
        'Hosted-runner, fixed one-second per-case warmup, warmed allocation measurements. This is not BenchmarkDotNet or a full no-regression acceptance gate.',
        'Two interleaved same-binary controls diagnose noise only; they do not establish a validated regression threshold.',
        'Each invocation uses byte-identical managed harness IL and verified loaded assembly hashes. JIT tiering/PGO defaults remain enabled; one-second warmup does not prove tier convergence.',
        'Input buffers for Span/sequence are pinned/aligned to64 bytes. Transport fixture allocations include their explicitly labelled setup. All fixture ValueTasks must finish synchronously.',
        '| Case | B/A geometric mean [within-host95% interval] | A ns range | B ns range | A/B thread B/op | A/B total B/op | AA control ratios |',
        '|---|---:|---:|---:|---:|---:|---:|']
    for name in sorted(common):
        a = [med(measurements['A' + str(i)][name], 'ns') for i in range(8)]
        b = [med(measurements['B' + str(i)][name], 'ns') for i in range(8)]
        point, low, high = interval([right / left for left, right in zip(a, b)])
        thread = [statistics.median(med(measurements[side + str(i)][name], 'threadBytes') for i in range(8)) for side in ('A', 'B')]
        total = [statistics.median(med(measurements[side + str(i)][name], 'totalBytes') for i in range(8)) for side in ('A', 'B')]
        controls = [med(measurements[f'control{i}-2'][name], 'ns') / med(measurements[f'control{i}-1'][name], 'ns') for i in (2, 6)]
        summary.append(f'| {name} | {point:.4f} [{low:.4f},{high:.4f}] | {min(a):.3f}–{max(a):.3f} | {min(b):.3f}–{max(b):.3f} | {thread[0]:.2f}/{thread[1]:.2f} | {total[0]:.2f}/{total[1]:.2f} | {controls[0]:.4f}/{controls[1]:.4f} |')
    summary += ['', '## Candidate exact/limited settings versus candidate empty context',
                'Limited cases execute after the common cases; rotated common order mitigates but cannot remove within-process drift. These are accepted-payload costs, not rejection timings.',
                '| Case | limited/empty [within-host95% interval] | limited thread/total B/op |', '|---|---:|---:|']
    for name in sorted(set(cb) - common):
        empty = name.rsplit('.', 1)[0] + '.empty'
        if empty not in common:
            raise RuntimeError('Limited case has no empty-context control')
        ratios = [med(measurements['B' + str(i)][name], 'ns') / med(measurements['B' + str(i)][empty], 'ns') for i in range(8)]
        point, low, high = interval(ratios)
        thread = statistics.median(med(measurements['B' + str(i)][name], 'threadBytes') for i in range(8))
        total = statistics.median(med(measurements['B' + str(i)][name], 'totalBytes') for i in range(8))
        summary.append(f'| {name} | {point:.4f} [{low:.4f},{high:.4f}] | {thread:.2f}/{total:.2f} |')
    summary += ['', '## Environment and process order', json.dumps({'environments': environments, 'schedule': schedule}),
                '', '## Every process median (no process exclusions)',
                '| Case | A0..A7 ns/op | B0..B7 ns/op |', '|---|---|---|']
    for name in sorted(cb):
        values = []
        for side in ('A', 'B'):
            values.append(','.join(f'{med(measurements[side + str(i)][name], "ns"):.3f}' for i in range(8)) if name in common or side == 'B' else 'not supported')
        summary.append('| ' + name + ' | ' + ' | '.join(values) + ' |')
    summary += ['', '## Retained counts, GC and allocation ranges',
                '| Case | iterations/sample | A/B GC0,GC1,GC2 totals | A/B thread B/op ranges | A/B total B/op ranges |',
                '|---|---:|---|---|---|']
    for name in sorted(cb):
        gc_values, thread_values, total_values = [], [], []
        for side in ('A', 'B'):
            if side == 'A' and name not in common:
                gc_values.append('not supported'); thread_values.append('not supported'); total_values.append('not supported')
                continue
            rows = [measurements[side + str(i)][name] for i in range(8)]
            gc_values.append(','.join(str(sum(s['gen' + str(g)] for r in rows for s in r['samples'])) for g in range(3)))
            for field, target in [('threadBytes', thread_values), ('totalBytes', total_values)]:
                values = [s[field] for r in rows for s in r['samples']]
                target.append(f'{min(values):.3f}–{max(values):.3f}')
        summary.append(f'| {name} | {counts[name]} | ' + '/'.join(gc_values) + ' | ' + '/'.join(thread_values) + ' | ' + '/'.join(total_values) + ' |')
    text = '\n'.join(summary)
    (args.work / 'summary.md').write_text(text)
    print(text, flush=True)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output:
            output.write(text + '\n')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Error messages here are fixed harness diagnostics, not subprocess output.
        print('Performance matrix failed: ' + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), flush=True)
        raise SystemExit(1)
