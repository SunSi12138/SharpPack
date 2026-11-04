using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// Inspect metadata outside the AOT consumer so verification adds no linker roots.
using var stream = File.OpenRead(args[0]);
using var pe = new PEReader(stream);
var reader = pe.GetMetadataReader();
foreach (var handle in reader.TypeDefinitions)
{
    var type = reader.GetTypeDefinition(handle);
    foreach (var methodHandle in type.GetMethods())
    {
        var name = reader.GetString(reader.GetMethodDefinition(methodHandle).Name);
        if (name.EndsWith(".CreateFormatter", StringComparison.Ordinal))
            Console.WriteLine($"FACTORY_METADATA {reader.GetString(type.Name)}: {name}");
    }
}
