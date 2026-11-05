"""Summarize selected JIT listings; never publish raw instruction dumps."""
import argparse
from collections import Counter
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def blocks(text):
    result = []
    parts = re.split(r'^; Assembly listing for method ', text, flags=re.M)[1:]
    for part in parts:
        lines = part.splitlines()
        end = re.search(r'^; Total bytes of code (\d+)', part, re.M)
        if not end:
            raise RuntimeError('Incomplete selected JIT listing')
        header = lines[0]
        tier = re.search(r'\(([^()]*)\)\s*$', header)
        body = part[:end.start()].splitlines()[1:]
        instructions, calls, symbols = [], [], []
        for line in body:
            parts = re.split(r';|//', line, maxsplit=1)
            clean = parts[0].strip()
            comment = parts[1].strip() if len(parts) == 2 else ''
            if any(name in comment for name in ('SharpPack.', 'Program', 'CORINFO_HELP')):
                symbols.append(re.sub(r'0x[0-9A-Fa-f]+', '<address>', comment))
            match = re.match(r'^([a-z][a-z0-9.]*)(?:\s+(.*))?$', clean)
            if match is None or clean.endswith(':'):
                continue
            opcode, operand = match.groups()
            operand = operand or ''
            instructions.append(opcode)
            if opcode in ('call', 'bl', 'blr', 'tail.jmp') or (opcode in ('jmp', 'b') and (':' in operand or 'CORINFO' in operand)):
                # Method and helper identities are useful; code addresses are not.
                target = re.sub(r'0x[0-9A-Fa-f]+', '<address>', operand)
                if any(name in comment for name in ('SharpPack.', 'Program', 'CORINFO_HELP')):
                    target += ' symbol=' + re.sub(r'0x[0-9A-Fa-f]+', '<address>', comment)
                target = re.sub(r'\b[0-9A-Fa-f]{12,16}\b', '<address>', target)
                calls.append(target)
        result.append({'method': header, 'tier': tier.group(1) if tier else 'unknown',
                       'codeBytes': int(end.group(1)), 'instructionCount': len(instructions),
                       'opcodes': dict(sorted(Counter(instructions).items())),
                       'calls': dict(sorted(Counter(calls).items())),
                       'symbolicReferences': dict(sorted(Counter(symbols).items()))})
    return result


def selected(row, assembly):
    if assembly == 'Perf':
        return ((row['method'].startswith('Program') and 'g__RunBatch' in row['method']) or
                (row['method'].startswith('Fixtures') and any(name in row['method'] for name in ('b__', 'CopyBlock', 'Consume'))))
    return any(x in row['method'] for x in (
        'SharpPackReaderOptionalState:Init', 'SharpPackReaderOptionalState:Reset',
        'SharpPackReader:.ctor', 'SharpPackReader:ReadValue', 'SharpPackReader:EnterDepth',
        'SharpPackReaderOptionalState:AddObjectReference',
        'SharpPackSerializer:Deserialize', 'SharpPackSerializer:EnsurePayloadWithinLimit',
        'SharpPackReader:DangerousReadUnmanagedArray', 'SharpPackReader:ReadUnmanagedArray',
        'SharpPackReader:TryReadCollectionHeader', 'SharpPackReader:ThrowIfCollectionLimitExceeded',
        'ArrayFormatter', 'UnmanagedArrayFormatter'))


def allows_empty(assembly, case):
    # Pure allocation/copy controls deliberately execute no reader method.
    return assembly == 'SharpPack.Core' and case.startswith(('CopyAlloc.', 'CopyReuse.'))


def main():
    parser = argparse.ArgumentParser()
    for name in ('a', 'b', 'work'): parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    args.work.mkdir(parents=True, exist_ok=True)
    cpu = min(os.sched_getaffinity(0))
    if cpu >= 63: raise RuntimeError('Unsupported affinity')
    summaries = []
    for tier in ('1',):
        for case in ('CopyAlloc.raw.65536', 'CopyReuse.raw.normal.65536'):
            for side in ('a', 'b'):
                root = getattr(args, side)
                for assembly in ('SharpPack.Core', 'Perf'):
                    label = '-'.join((tier, case, side, assembly))
                    dump = args.work / (label + '.asm')
                    env = dict(os.environ, DOTNET_TieredCompilation=tier, DOTNET_TieredPGO=tier,
                               DOTNET_JitDisasm='*', DOTNET_JitDisasmAssemblies=assembly,
                               DOTNET_JitStdOutFile=str(dump))
                    command = ['taskset', '-c', str(cpu), 'dotnet', str(root / 'Perf.dll'),
                               '--group', 'copy', '--case', case, '--warmup', '8000', '--calibrate', 'yes']
                    result = subprocess.run(command, cwd=root, env=env, stdout=subprocess.PIPE,
                                            stderr=subprocess.STDOUT, timeout=90)
                    raw = result.stdout.decode(errors='replace')
                    (args.work / (label + '.log')).write_text(raw)
                    if result.returncode: raise RuntimeError('JIT probe failed: ' + label)
                    # JIT listings use a separate local file to avoid stdout interleaving.
                    # No raw lines are echoed on error.
                    environments = [json.loads(line[4:]) for line in raw.splitlines() if line.startswith('ENV ')]
                    if len(environments) != 1: raise RuntimeError('Missing JIT process metadata: ' + label)
                    observed = environments[0]
                    hashes = {key: hashlib.sha256((root / file).read_bytes()).hexdigest().upper()
                              for key, file in [('harness', 'Perf.dll'), ('core', 'SharpPack.Core.dll'), ('streaming', 'SharpPack.Streaming.dll')]}
                    if any(observed[key] != value for key, value in hashes.items()):
                        raise RuntimeError('JIT binary identity mismatch')
                    if (observed['tieredCompilation'] != tier or observed['tieredPGO'] != tier or
                        observed['selectedCase'] != case or observed['affinity'] != 1 << cpu):
                        raise RuntimeError('JIT process settings mismatch')
                    if not dump.is_file(): raise RuntimeError('Missing JIT output file: ' + label)
                    all_rows = blocks(dump.read_text())
                    rows = [row for row in all_rows if selected(row, assembly)]
                    if not all_rows: raise RuntimeError('JIT emitted no listings')
                    if len(rows) > 500 or (not rows and not allows_empty(assembly, case)):
                        raise RuntimeError('Unexpected selected JIT listing count')
                    if assembly == 'Perf' and not any('g__RunBatch' in row['method'] and row['tier'] == 'Tier1' for row in rows):
                        raise RuntimeError('Missing Tier1 batch listing: ' + label)
                    summaries.append({'label': label, 'environment': observed,
                                      'selectedCount': len(rows), 'allListingCount': len(all_rows), 'methods': rows})
                    print('JIT_ENV ' + json.dumps({key: value for key, value in summaries[-1].items() if key != 'methods'}), flush=True)
                    for row in rows:
                        print('JIT_METHOD ' + json.dumps({'label': label, **row}), flush=True)
    text = json.dumps(summaries, indent=2)
    (args.work / 'summary.json').write_text(text)
    if os.environ.get('GITHUB_STEP_SUMMARY'):
        with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as output:
            output.write('# Allocation/copy JIT diagnosis\n\n')
            output.write('Separate probe processes; no timing claims from disassembly. Raw listings stay in temporary runner storage. Structured method/tier/code-size/opcode/call summaries are in the step output.\n\n')
            for entry in summaries:
                output.write('- ' + entry['label'] + ': ' + str(entry['selectedCount']) + ' selected listings.\n')


if __name__ == '__main__':
    try: main()
    except Exception as error:
        print('JIT diagnosis failed: ' + (str(error) if isinstance(error, RuntimeError) else type(error).__name__), flush=True)
        raise SystemExit(1)
