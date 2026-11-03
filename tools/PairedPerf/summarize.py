import pathlib,sys,statistics
p=pathlib.Path(sys.argv[1]); runs={}
for label in ('A1','B1','B2','A2'):
    rows={}
    for line in (p/f'perf-{label}.txt').read_text().splitlines():
        if line.startswith('RESULT,'):
            _,name,ns,allocated,low,high,n=line.split(',')
            rows[name]=(float(ns),float(allocated),float(low),float(high))
    runs[label]=rows
print('Same-runner SDK 10.0.102 ABBA microbenchmark; median of two run medians. Not BDN, no statistical regression claim.\n')
print('Baseline 8efea5afc79eaefc5023806a2588c4ad2f09a9c6; candidate cd8359d9777898eb7334985001eef0d2d0287c9e.\n')
print('| Case | Old ns/op | New ns/op | Ratio | Old B/op | New B/op |')
print('|---|---:|---:|---:|---:|---:|')
for name in runs['A1']:
    a=statistics.mean(runs[k][name][0] for k in ('A1','A2')); b=statistics.mean(runs[k][name][0] for k in ('B1','B2'))
    aa=statistics.mean(runs[k][name][1] for k in ('A1','A2'));bb=statistics.mean(runs[k][name][1] for k in ('B1','B2'))
    print(f'| {name} | {a:.2f} | {b:.2f} | {b/a:.3f} | {aa:.2f} | {bb:.2f} |')
print('\nCandidate-only BitArray writer uses reused output buffer. Unsafe .NET 10 baseline intentionally not executed; not equivalent work and not a speed comparison.\n')
for name in runs['B1']:
    if name.startswith('BitArray'):
        ns=statistics.mean(runs[k][name][0] for k in ('B1','B2')); alloc=statistics.mean(runs[k][name][1] for k in ('B1','B2'))
        print(f'- {name}: {ns:.2f} ns/op; {alloc:.2f} B/op')
