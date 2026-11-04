"""Compare final JIT bodies; emit summaries, never raw disassembly."""
import pathlib, re, sys, hashlib, difflib
root = pathlib.Path(sys.argv[1])
needles = ('SharpPack.Formatters.StringFormatter:Serialize[', 'SharpPack.SharpPackWriter`1[System.__Canon]:WriteUtf8(System.String)', 'SharpPack.SharpPackSerializer:Serialize[System.__Canon,System.__Canon]')

def bodies(path, tier):
    found = {}
    for block in path.read_text().split('; Assembly listing for method ')[1:]:
        header, _, rest = block.partition('\n')
        if tier not in header or not any(n in header for n in needles):
            continue
        code = [line.strip() for line in rest.splitlines() if line.strip() and not line.startswith(';')]
        size = re.search(r'; Total bytes of code (\d+)', rest)
        if size:
            found[header] = (code, int(size.group(1)))
    assert any('StringFormatter:Serialize[' in k for k in found), (str(path), list(found))
    if tier == '(Tier1)':
        assert len(found) == 3, (str(path), list(found))
    return found

for mode, tier in [('tiered', '(Tier1)'), ('fullopts', '(FullOpts)')]:
    a, b = [bodies(root / f'jit-{mode}-{side}.asm', tier) for side in ('baseline', 'candidate')]
    assert set(a) == set(b)
    print(f'## {mode}: final completed UTF8 hot-path bodies ({len(a)} methods; fully inlined methods have no separate body)')
    for name in a:
        ac, az = a[name]; bc, bz = b[name]
        same = ac == bc
        digest = lambda x: hashlib.sha256('\n'.join(x).encode()).hexdigest()[:16]
        print(f'- {name}: identical={same}; bytes A/B={az}/{bz}; normalized instructions A/B={len(ac)}/{len(bc)}; SHA256 A/B={digest(ac)}/{digest(bc)}')
        if not same:
            edits = [(tag, i2-i1, j2-j1) for tag,i1,i2,j1,j2 in difflib.SequenceMatcher(a=ac,b=bc).get_opcodes() if tag != 'equal']
            print(f'  Instruction-list edit groups (kind, A lines, B lines): {edits}')
    print()
