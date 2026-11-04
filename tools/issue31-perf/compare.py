import json, math, os, random, statistics, subprocess, sys
from pathlib import Path
root = Path(sys.argv[1])
cpu = min(os.sched_getaffinity(0))
print('PINNED_CPU', cpu, flush=True)
def run(label, group):
    output = subprocess.check_output(['taskset', '-c', str(cpu), str(root / label / 'DeserializeOnlyAot'), '--performance'], text=True)
    data = json.loads(next(line[8:] for line in output.splitlines() if line.startswith('MEASURE ')))
    assert data['sink'] == 42 and data['iterations'] == 5000000
    assert len(data['samples']) == 15 and all(x == 48 for x in data['allocations'])
    print('PROCESS ' + json.dumps({'group':group, 'binary':label, **data}), flush=True)
    return data['median']
noise = []
for i in range(4):
    a = run('baseline1', 'AA-'+str(i))
    b = run('baseline1', 'AA-'+str(i))
    noise.append(b/a)
ratios = []
by_build = {1: [], 2: []}
for block in range(12):
    build = block % 2 + 1
    a, b = 'baseline'+str(build), 'candidate'+str(build)
    order = [a,b,b,a] if (block // 2) % 2 == 0 else [b,a,a,b]
    values = {a:[], b:[]}
    for label in order: values[label].append(run(label, block))
    ratio = statistics.geometric_mean(values[b]) / statistics.geometric_mean(values[a])
    ratios.append(ratio); by_build[build].append(ratio)
    print('PAIR ' + json.dumps({'block':block, 'build':build, 'candidate_over_baseline':ratio}), flush=True)
rng = random.Random(31)
# Processes, not inner timed samples, are paired units. This interval only
# describes this one hosted runner; it is not a cross-machine confidence claim.
bootstrap = sorted(statistics.geometric_mean(rng.choices(ratios,k=len(ratios))) for _ in range(10000))
print('SUMMARY ' + json.dumps({'same_binary_ratios':noise, 'same_binary_geomean':statistics.geometric_mean(noise), 'paired_ratios':ratios, 'paired_geomean':statistics.geometric_mean(ratios), 'paired_bootstrap_95_percent':[bootstrap[250],bootstrap[9749]], 'by_build':{k:statistics.geometric_mean(v) for k,v in by_build.items()}, 'scope':'one fixed-CPU hosted x64 runner; not a general no-regression acceptance'}))
