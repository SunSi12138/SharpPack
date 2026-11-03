import pathlib,sys,statistics
p=pathlib.Path(sys.argv[1]); runs={}
for label in ('A1','B1','C1','C2','B2','A2'):
    rows={}
    for line in (p/f'perf-{label}.txt').read_text().splitlines():
        if line.startswith('RESULT,'):
            _,name,ns,allocated,low,high,n=line.split(',')
            rows[name]=(float(ns),float(allocated),float(low),float(high))
    runs[label]=rows
print('Same-runner SDK 10.0.102 ABCCBA microbenchmark; average of two run medians. Not BDN, no statistical regression claim.\n')
print('A dev8efea5af; B PR30 56113519; C independent inline experiment974e7186.\n')
print('| Case | A ns/op | B ns/op | C ns/op | B/A | C/A | B B/op | C B/op |')
print('|---|---:|---:|---:|---:|---:|---:|---:|')
for name in runs['A1']:
    a,b,c=[statistics.mean(runs[k+suffix][name][0] for suffix in ('1','2')) for k in ('A','B','C')]
    print(f'| {name} | {a:.2f} | {b:.2f} | {c:.2f} | {b/a:.3f} | {c/a:.3f} | {runs["B1"][name][1]:.2f} | {runs["C1"][name][1]:.2f} |')
print('\nSafe BitArray only; unsafe old baseline not executed. Reused output buffer.\n')
for name in runs['B1']:
    if name.startswith('BitArray'):
        for k in ('B','C'):
            ns=statistics.mean(runs[k+s][name][0] for s in ('1','2'))
            print(f'- {name} {k}: {ns:.2f} ns/op; {runs[k+"1"][name][1]:.2f} B/op')
