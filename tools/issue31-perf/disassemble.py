import difflib, hashlib, re, subprocess, sys
from pathlib import Path
root=Path(sys.argv[1]); methods={}
for label in ['baseline1','candidate1','baseline2','candidate2']:
    exe=root/label/'DeserializeOnlyAot'
    symbols=subprocess.check_output(['nm','-S','--defined-only',str(exe)+'.dbg'],text=True)
    symbol_names={}
    for line in symbols.splitlines():
        parts=line.split()
        if len(parts)>=4:
            symbol_names.setdefault(int(parts[0],16), ' '.join(parts[3:]))
    selected=[]
    for line in symbols.splitlines():
        p=line.split()
        if len(p)<4 or p[2] not in ('t','T'): continue
        name=' '.join(p[3:])
        if ('NestedModel' in name or 'NestedItem' in name or 'ReadNested' in name) and ('Deserialize' in name or 'ReadNested' in name):
            address,size=int(p[0],16),int(p[1],16)
            if not size or size>10000: continue
            raw=subprocess.check_output(['objdump','-d','--no-show-raw-insn',f'--start-address={address}',f'--stop-address={address+size}',str(exe)],text=True)
            # Resolve branch targets to instruction indexes and external calls
            # to debug-symbol names, rather than objdump's misleading PLT offsets.
            rows=[]
            for row in raw.splitlines():
                m=re.match(r'\s*([0-9a-f]+):\s+(.*)',row)
                if m: rows.append((int(m[1],16),m[2]))
            local={address:'L'+str(index) for index,(address,_) in enumerate(rows)}
            instructions=[]
            for _,value in rows:
                value=value.split('#')[0].strip()
                value=re.sub(r'0x[0-9a-f]+\(%rip\)', 'RIP(%rip)', value)
                target=re.match(r'((?:j\w+|call)\s+)([0-9a-f]+)(?: <[^>]*>)?$',value)
                if target:
                    address=int(target[2],16)
                    value=target[1]+local.get(address,symbol_names.get(address,'EXTERNAL'))
                instructions.append(value)
            methods[label,name]=instructions
            selected.append({'name':name,'bytes':size,'normalized_sha256':hashlib.sha256('\n'.join(instructions).encode()).hexdigest()})
    print('NATIVE_METHODS',label,selected,flush=True)
for a,b in [('baseline1','baseline2'),('candidate1','candidate2'),('baseline1','candidate1')]:
    names=sorted({name for label,name in methods if label==a} & {name for label,name in methods if label==b})
    for name in names:
        left,right=methods[a,name],methods[b,name]
        diff=list(difflib.unified_diff(left,right,fromfile=a,tofile=b,lineterm=''))
        print('METHOD_DIFF',a,b,name,'changed',bool(diff))
        print('\n'.join(diff[:180]))
