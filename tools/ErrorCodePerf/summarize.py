"""Summarize all predeclared samples without discarding outliers."""
import pathlib, sys, statistics
p = pathlib.Path(sys.argv[1])
labels = ('A1', 'B1', 'B2', 'A2', 'B3', 'A3', 'A4', 'B4')
runs = {}
for label in labels:
    rows = {}
    for line in (p / f'perf-{label}.txt').read_text().splitlines():
        if line.startswith('RESULT,'):
            _, name, ns, allocated, low, high, count = line.split(',')
            rows[name] = tuple(map(float, (ns, allocated, low, high)))
    assert len(rows) == 15, (label, len(rows))
    runs[label] = rows
assert all(set(rows) == set(runs['A1']) for rows in runs.values())
print('Pinned dev 6666ada vs error-code candidate 02d7604. Same runner, SDK, harness and process settings. Order: ABBA BAAB. Four independent run medians per revision; no exclusions.\n')
print('Warmed microbenchmark, not BenchmarkDotNet or a statistical regression gate. Hosted-runner noise and runtime layout can affect small timing differences. ScalarRead includes identical boxing on both sides.\n')
print('| Case | A ns/op (range) | B ns/op (range) | B/A | A B/op | B B/op |')
print('|---|---:|---:|---:|---:|---:|')
for name in runs['A1']:
    a = [runs['A'+str(i)][name][0] for i in range(1,5)]
    b = [runs['B'+str(i)][name][0] for i in range(1,5)]
    aa = [runs['A'+str(i)][name][1] for i in range(1,5)]
    ba = [runs['B'+str(i)][name][1] for i in range(1,5)]
    print(f'| {name} | {statistics.median(a):.2f} ({min(a):.2f}–{max(a):.2f}) | {statistics.median(b):.2f} ({min(b):.2f}–{max(b):.2f}) | {statistics.median(b)/statistics.median(a):.3f} | {statistics.median(aa):.2f} | {statistics.median(ba):.2f} |')
