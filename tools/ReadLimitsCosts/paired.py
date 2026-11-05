"""Diagnostic component and allocation/copy controls, no production mutations."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import statistics
import subprocess
import time
from run import records, interval, med


def cases(group):
    names = []
    if group == 'component':
        names = ['InitOnly', 'InitResetExit', 'ResetClean', 'CtorSpan64', 'CtorSpan65536', 'ManualReadInt', 'DeserializeInt']
    else:
        for size in (65504, 65536, 65568):
            names += [f'ReadAlloc.empty.{size}', f'ReadReuse.empty.normal.{size}',
                      f'CopyAlloc.raw.{size}', f'CopyReuse.raw.normal.{size}']
        for mode in ('raw', 'custom'):
            names += [f'ReadAlloc.{mode}.65536', f'ReadReuse.{mode}.normal.65536']
        for placement in ('pinned0', 'pinned32'):
            names += [f'ReadReuse.empty.{placement}.65536', f'CopyReuse.raw.{placement}.65536']
    specs = [(name, ('A', name), ('B', name)) for name in names]
    if group == 'component':
        for mode in ('Default', 'Exact'):
            specs.append(('GuardDiagnostic' + mode, ('B', 'GuardNoop' + mode), ('B', 'GuardAccepted' + mode)))
    return specs


def main():
    parser = argparse.ArgumentParser()
    for name in ('a', 'b', 'work'): parser.add_argument('--' + name, type=Path, required=True)
    parser.add_argument('--group', choices=['component', 'copy'], required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    roots = {'A': args.a, 'B': args.b}
    cpu = min(os.sched_getaffinity(0))
    if cpu >= 63: raise RuntimeError('Unsupported affinity mask')
    hashes = {side: {key: hashlib.sha256((root / file).read_bytes()).hexdigest().upper()
                    for key, file in [('harness', 'Perf.dll'), ('core', 'SharpPack.Core.dll'), ('streaming', 'SharpPack.Streaming.dll')]}
              for side, root in roots.items()}
    if hashes['A']['harness'] != hashes['B']['harness'] or hashes['A']['core'] == hashes['B']['core']:
        raise RuntimeError('Unexpected harness/runtime identity')
    data, environments, schedule, observations = {}, {}, [], {}

    def execute(endpoint, label, counts=None):
        side, name = endpoint
        root = roots[side]
        command = ['taskset', '-c', str(cpu), 'dotnet', str(root / 'Perf.dll'), '--group', args.group,
                   '--case', name, '--warmup', '2000']
        command += ['--calibrate', 'yes'] if counts is None else ['--counts', str(counts)]
        begin = time.time()
        result = subprocess.run(command, cwd=root, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=60)
        text = result.stdout.decode(errors='replace')
        (args.work / (label + '.log')).write_text(text)
        if result.returncode: raise RuntimeError('Component/copy process failed: ' + label)
        envs = [json.loads(line[4:]) for line in text.splitlines() if line.startswith('ENV ')]
        if len(envs) != 1: raise RuntimeError('Missing process environment')
        env = envs[0]
        if any(env[key] != value for key, value in hashes[side].items()): raise RuntimeError('Loaded binary mismatch')
        if (env['tieredCompilation'] != '1' or env['tieredPGO'] != '1' or env['selectedCase'] != name or
            env['group'] != args.group or env['warmupMilliseconds'] != 2000 or env['affinity'] != 1 << cpu or
            env['limited'] != (side == 'B')):
            raise RuntimeError('Unexpected runtime settings')
        key = side + ':' + name
        if key in environments and environments[key] != env: raise RuntimeError('Process metadata changed')
        environments[key] = env
        rows = records(text, 'CAL' if counts is None else 'RESULT')
        if set(rows) != {name}: raise RuntimeError('Unexpected measured case')
        if counts is not None:
            obs = [json.loads(line[4:]) for line in text.splitlines() if line.startswith('OBS ')]
            if len(obs) != 1: raise RuntimeError('Missing post-timing observation')
            if args.group == 'copy':
                observation = obs[0]
                if observation['bufferMod64'] != 0 or observation['dataMod64'] != 4:
                    raise RuntimeError('Input data alignment changed')
                if sum(observation['outputMod64'].values()) != 64 or sum(observation['outputMod4096'].values()) != 64:
                    raise RuntimeError('Invalid alignment observations')
                if observation['placement'].startswith('pinned'):
                    target = observation['placement'][6:]
                    if observation['outputMod64'] != {target: 64}:
                        raise RuntimeError('Pinned output alignment changed')
            observations[label] = obs[0]
        schedule.append({'label': label, 'runtime': side, 'case': name, 'startedUtcEpoch': begin, 'seconds': time.time() - begin})
        return rows[name]

    for index, (name, left, right) in enumerate(cases(args.group)):
        endpoints = {'A': left, 'B': right}
        cal = {side: execute(endpoint, f'{index}-cal-{side}') for side, endpoint in endpoints.items()}
        if cal['A']['PayloadHash'] != cal['B']['PayloadHash']: raise RuntimeError('Matched payload differs')
        count = max(row['iterations'] for row in cal.values())
        if not 128 <= count <= 100_000_128: raise RuntimeError('Invalid iteration count')
        counts = args.work / f'{index}-counts.json'
        counts.write_text(json.dumps({endpoint[1]: count for endpoint in endpoints.values()}))
        runs = {}
        for block in range(8):
            for side in ('AB' if block % 2 == 0 else 'BA'):
                key = side + str(block)
                runs[key] = execute(endpoints[side], f'{index}-{key}', counts)
            if block in (2, 6):
                for repeat in (1, 2):
                    key = f'control{block}-{repeat}'
                    runs[key] = execute(right, f'{index}-{key}', counts)
        for row in runs.values():
            if row['iterations'] != count or row['PayloadHash'] != cal['A']['PayloadHash']:
                raise RuntimeError('Iteration/payload drift')
            med(row, 'ns'); med(row, 'threadBytes'); med(row, 'totalBytes')
        data[name] = {'count': count, 'runs': runs, 'left': left, 'right': right}
        print('Diagnostic case complete: ' + name, flush=True)
    (args.work / 'data.json').write_text(json.dumps({'data': data, 'environments': environments, 'schedule': schedule, 'observations': observations}))
    summary = [f'# Read-limit component/copy diagnosis: {args.group}',
        'Normal rows: A dev 6666ada, B original PR 56113519. GuardDiagnostic rows: both use B, left=noop/right=accepted guard. No production optimization or check removal.',
        'One selected fixture/process, two-second shared-callsite warmup, eight alternating process pairs, two same-binary right/right pairs. Identical calibrated counts, managed harness IL and verified runtime hashes.',
        'Within-host intervals resample eight paired log ratios and are exploratory/unadjusted. Component differences are not an additive decomposition of complete deserialization costs or mathematical lower bounds.',
        '| Case | right/left [95%] | median paired delta ns | left ns range | right ns range | left/right thread B/op | right/right controls |',
        '|---|---:|---:|---:|---:|---:|---:|']
    for name, item in data.items():
        runs = item['runs']
        a = [med(runs['A' + str(i)], 'ns') for i in range(8)]
        b = [med(runs['B' + str(i)], 'ns') for i in range(8)]
        point, low, high = interval([y / x for x, y in zip(a, b)])
        delta = statistics.median(y - x for x, y in zip(a, b))
        allocation = '/'.join(f'{statistics.median(med(runs[side + str(i)], "threadBytes") for i in range(8)):.3f}' for side in ('A', 'B'))
        controls = '/'.join(f'{med(runs[f"control{i}-2"], "ns") / med(runs[f"control{i}-1"], "ns"):.4f}' for i in (2, 6))
        summary.append(f'| {name} | {point:.4f} [{low:.4f},{high:.4f}] | {delta:.3f} | {min(a):.3f}–{max(a):.3f} | {min(b):.3f}–{max(b):.3f} | {allocation} | {controls} |')
    summary += ['', '## All process medians and allocations',
                '| Case/side | iterations/sample | process0..7 ns | thread B/op range | total B/op range | GC0,1,2 totals |',
                '|---|---:|---|---:|---:|---:|']
    for name, item in data.items():
        for side in ('A', 'B'):
            rows = [item['runs'][side + str(i)] for i in range(8)]
            times = ','.join(f'{med(row, "ns"):.3f}' for row in rows)
            ranges = []
            for field in ('threadBytes', 'totalBytes'):
                values = [sample[field] for row in rows for sample in row['samples']]
                ranges.append(f'{min(values):.3f}–{max(values):.3f}')
            gc = ','.join(str(sum(sample['gen' + str(g)] for row in rows for sample in row['samples'])) for g in range(3))
            summary.append(f'| {name}/{side} | {item["count"]} | {times} | ' + ' | '.join(ranges) + ' | ' + gc + ' |')
    summary += ['', '## Same-binary right/right absolute controls', '| Case | control2-1/2-2/6-1/6-2 ns |', '|---|---|']
    for name, item in data.items():
        times = [med(item['runs'][f'control{i}-{repeat}'], 'ns') for i in (2, 6) for repeat in (1, 2)]
        summary.append('| ' + name + ' | ' + ','.join(f'{value:.3f}' for value in times) + ' |')
    summary += ['', '## Verified configurations and process order', json.dumps({'environments': environments, 'schedule': schedule})]
    text = '\n'.join(summary)
    print(text, flush=True)
    for label, observation in observations.items():
        print('OBSERVATION ' + json.dumps({'label': label, **observation}), flush=True)
    (args.work / 'summary.md').write_text(text)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output: output.write(text + '\n')


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print('Cost diagnosis failed: ' + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), flush=True)
        raise SystemExit(1)
