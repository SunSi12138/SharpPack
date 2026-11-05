"""Verify the exact reset-only transformation; never edits a checkout."""
import argparse
from pathlib import Path
import subprocess


def transform(before):
    start = before.index('    public void Reset()\n', before.index('public sealed class'))
    boundary = before.index('        SerializerContext = null;', start)
    end = before.index('    void IDisposable.Dispose()', boundary)
    tables = before[start + len('    public void Reset()\n    {\n'):boundary]
    stores = before[boundary:end]
    replacement = ('    [MethodImpl(MethodImplOptions.AggressiveInlining)]\n'
                   '    public void Reset()\n    {\n'
                   '        if (sequentialReferences is not null || sparseReferences is not null)\n'
                   '        {\n            ResetReferences();\n        }\n\n' + stores +
                   '    [MethodImpl(MethodImplOptions.NoInlining)]\n'
                   '    void ResetReferences()\n    {\n' + tables.rstrip() + '\n    }\n\n')
    return before[:start] + replacement + before[end:]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--pr', type=Path, required=True)
    parser.add_argument('--candidate', type=Path, required=True)
    args = parser.parse_args()
    path = 'src/SharpPack.Core/SharpPackReaderOptionalState.cs'
    before = (args.pr / path).read_text()
    after = (args.candidate / path).read_text()
    if after != transform(before): raise RuntimeError('Unexpected production transformation')
    changed = subprocess.check_output(['git', '-C', str(args.candidate), 'diff', '--name-only',
        '56113519bb9771fe33ec39daed59181024918228', 'HEAD', '--', 'src', 'Directory.Build.props', 'global.json'], text=True).splitlines()
    if changed != [path]: raise RuntimeError('Unexpected additional production changes')
    print('Exact Reset helper transformation verified; original table blocks, reset stores and Init preserved.')


if __name__ == '__main__': main()
