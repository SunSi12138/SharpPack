#!/usr/bin/env python3
"""Apply an isolated CI-only root-preservation candidate, not production edits."""
import sys
from pathlib import Path

variant = sys.argv[1]
assert variant in ('baseline', 'no-annotations', 'interface-roots', 'constructor-roots')
if variant == 'baseline':
    raise SystemExit(0)

core = Path('src/SharpPack.Core')
annotation = '        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)]\n'
for name, expected in [('SharpPackSerializer.Deserialize.cs', 11), ('SharpPackSerializer.Context.cs', 6)]:
    path = core / name
    source = path.read_text()
    assert source.count(annotation) == expected, name
    replacement = annotation.replace('NonPublicMethods', 'NonPublicConstructors') if variant == 'constructor-roots' else ''
    path.write_text(source.replace(annotation, replacement))

if variant == 'interface-roots':
    path = core / 'FormatterFactory.cs'
    source = path.read_text()
    marker = '    internal static SharpPackFormatter<T> Create(\n'
    assert source.count(marker) == 1
    roots = '''    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(ISharpPackFormatterFactory<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(ISharpPackContextFormatterFactory<>))]
'''
    path.write_text(source.replace(marker, roots + marker))

if variant == 'constructor-roots':
    path = Path('src/SharpPack.Generator/SharpPackGenerator.Emitter.cs')
    source = path.read_text()
    marker = '        StaticConstructor();\n'
    assert source.count(marker) == 3
    root = '''        if (!global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
        {
            global::System.GC.KeepAlive(typeof({{TypeName}}).GetMethods(global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly));
        }
'''
    path.write_text(source.replace(marker, marker + root))
    # Same-assembly external union targets are not covered by the ordinary
    # emitter. Characterize their required static-constructor anchor too.
    path = Path('src/SharpPack.Generator/SharpPackGenerator.ExternalUnionFactory.cs')
    source = path.read_text()
    marker = '''        sb.AppendLine("{");
        sb.AppendLine(
            $"    static global::SharpPack.SharpPackFormatter<{targetType}>");'''
    assert source.count(marker) == 1
    rooted = '''        sb.AppendLine("{");
        sb.AppendLine($"    static {target.Name}()");
        sb.AppendLine("    {");
        sb.AppendLine("        if (!global::System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)");
        sb.AppendLine($"            global::System.GC.KeepAlive(typeof({targetType}).GetMethods(global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly));");
        sb.AppendLine("    }");
        sb.AppendLine(
            $"    static global::SharpPack.SharpPackFormatter<{targetType}>");'''
    path.write_text(source.replace(marker, rooted))
print(f'Applied CI-only candidate: {variant}')
