# Collectible AssemblyLoadContext sample

This sample loads a plugin into a collectible `AssemblyLoadContext`, passes an
explicit `SharpPackSerializerContext` into plugin-owned generic serialization,
then releases every plugin reference and verifies that the load context is
collected.

Run it from the repository root:

```shell
dotnet run --project sandbox/CollectibleAlcSample -c Release
```

The host deliberately shares `SharpPack.Core` with the default load context.
Loading another copy inside the plugin context would give
`SharpPackSerializerContext` a different runtime type identity.
