using Microsoft.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SharpPack.Generator;

public class ReferenceSymbols
{
    public Compilation Compilation { get; }

    public INamedTypeSymbol SharpPackableAttribute { get; }
    public INamedTypeSymbol SharpPackUnionAttribute { get; }
    public INamedTypeSymbol SharpPackUnionFormatterAttribute { get; }
    public INamedTypeSymbol SharpPackConstructorAttribute { get; }
    public INamedTypeSymbol SharpPackAllowSerializeAttribute { get; }
    public INamedTypeSymbol SharpPackOrderAttribute { get; }
    public INamedTypeSymbol? SharpPackCustomFormatterAttribute { get; }
    public INamedTypeSymbol? SharpPackCustomFormatter2Attribute { get; }
    public INamedTypeSymbol SharpPackIgnoreAttribute { get; }
    public INamedTypeSymbol SharpPackIncludeAttribute { get; }
    public INamedTypeSymbol SharpPackOnSerializingAttribute { get; }
    public INamedTypeSymbol SharpPackOnSerializedAttribute { get; }
    public INamedTypeSymbol SharpPackOnDeserializingAttribute { get; }
    public INamedTypeSymbol SharpPackOnDeserializedAttribute { get; }
    public INamedTypeSymbol SkipOverwriteDefaultAttribute { get; }
    public INamedTypeSymbol GenerateTypeScriptAttribute { get; }
    public INamedTypeSymbol ISharpPackable { get; }
    public INamedTypeSymbol ISharpPackUnmanagedRawCopyDisabled { get; }

    public WellKnownTypes KnownTypes { get; }

    public ReferenceSymbols(Compilation compilation)
    {
        Compilation = compilation;

        // SharpPack
        SharpPackableAttribute = GetTypeByMetadataName(SharpPackGenerator.SharpPackableAttributeFullName);
        SharpPackUnionAttribute = GetTypeByMetadataName("SharpPack.SharpPackUnionAttribute");
        SharpPackUnionFormatterAttribute = GetTypeByMetadataName("SharpPack.SharpPackUnionFormatterAttribute");
        SharpPackConstructorAttribute = GetTypeByMetadataName("SharpPack.SharpPackConstructorAttribute");
        SharpPackAllowSerializeAttribute = GetTypeByMetadataName("SharpPack.SharpPackAllowSerializeAttribute");
        SharpPackOrderAttribute = GetTypeByMetadataName("SharpPack.SharpPackOrderAttribute");
        SharpPackCustomFormatterAttribute = compilation.GetTypeByMetadataName("SharpPack.SharpPackCustomFormatterAttribute`1")?.ConstructUnboundGenericType();
        SharpPackCustomFormatter2Attribute = compilation.GetTypeByMetadataName("SharpPack.SharpPackCustomFormatterAttribute`2")?.ConstructUnboundGenericType();
        SharpPackIgnoreAttribute = GetTypeByMetadataName("SharpPack.SharpPackIgnoreAttribute");
        SharpPackIncludeAttribute = GetTypeByMetadataName("SharpPack.SharpPackIncludeAttribute");
        SharpPackOnSerializingAttribute = GetTypeByMetadataName("SharpPack.SharpPackOnSerializingAttribute");
        SharpPackOnSerializedAttribute = GetTypeByMetadataName("SharpPack.SharpPackOnSerializedAttribute");
        SharpPackOnDeserializingAttribute = GetTypeByMetadataName("SharpPack.SharpPackOnDeserializingAttribute");
        SharpPackOnDeserializedAttribute = GetTypeByMetadataName("SharpPack.SharpPackOnDeserializedAttribute");
        SkipOverwriteDefaultAttribute = GetTypeByMetadataName("SharpPack.SuppressDefaultInitializationAttribute");
        GenerateTypeScriptAttribute = GetTypeByMetadataName(SharpPackGenerator.GenerateTypeScriptAttributeFullName);
        ISharpPackable = GetTypeByMetadataName("SharpPack.ISharpPackable`1").ConstructUnboundGenericType();
        ISharpPackUnmanagedRawCopyDisabled = GetTypeByMetadataName(
            "SharpPack.ISharpPackUnmanagedRawCopyDisabled");
        KnownTypes = new WellKnownTypes(this);
    }

    INamedTypeSymbol GetTypeByMetadataName(string metadataName)
    {
        var symbol = Compilation.GetTypeByMetadataName(metadataName);
        if (symbol == null)
        {
            throw new InvalidOperationException($"Type {metadataName} is not found in compilation.");
        }
        return symbol;
    }

    // UnamnaagedType no need.
    public class WellKnownTypes
    {
        readonly ReferenceSymbols parent;

        public INamedTypeSymbol System_Collections_Generic_IEnumerable_T { get; }
        public INamedTypeSymbol System_Collections_Generic_ICollection_T { get; }
        public INamedTypeSymbol System_Collections_Generic_ISet_T { get; }
        public INamedTypeSymbol System_Collections_Generic_IDictionary_T { get; }
        public INamedTypeSymbol System_Collections_Generic_List_T { get; }

        public INamedTypeSymbol System_Guid { get; }
        public INamedTypeSymbol System_Version { get; }
        public INamedTypeSymbol System_Uri { get; }

        public INamedTypeSymbol System_Numerics_BigInteger { get; }
        public INamedTypeSymbol System_TimeZoneInfo { get; }
        public INamedTypeSymbol System_Collections_BitArray { get; }
        public INamedTypeSymbol System_Text_StringBuilder { get; }
        public INamedTypeSymbol System_Type { get; }
        public INamedTypeSymbol System_Globalization_CultureInfo { get; }
        public INamedTypeSymbol System_Lazy_T { get; }
        public INamedTypeSymbol System_Collections_Generic_KeyValuePair_T { get; }
        public INamedTypeSymbol System_Nullable_T { get; }

        public INamedTypeSymbol System_DateTime { get; }
        public INamedTypeSymbol System_DateTimeOffset { get; }
        public INamedTypeSymbol System_Runtime_InteropServices_StructLayout { get; }

        // netstandard2.0 source generator has there reference so use string instead...
        //public INamedTypeSymbol System_Memory_T { get; }
        //public INamedTypeSymbol System_ReadOnlyMemory_T { get; }
        //public INamedTypeSymbol System_Buffers_ReadOnlySequence_T { get; }
        //public INamedTypeSymbol System_Collections_Generic_PriorityQueue_T { get; }
        const string System_Memory_T = "global::System.Memory<>";
        const string System_ReadOnlyMemory_T = "global::System.ReadOnlyMemory<>";
        const string System_Buffers_ReadOnlySequence_T = "global::System.Buffers.ReadOnlySequence<>";
        const string System_Collections_Generic_PriorityQueue_T = "global::System.Collections.Generic.PriorityQueue<,>";

        readonly HashSet<ITypeSymbol> knownTypes;

        static readonly Dictionary<string, string> knownGenericTypes = new()
        {
            // ArrayFormatters
            { "System.ArraySegment<>", "global::SharpPack.Formatters.ArraySegmentFormatter<TREPLACE>" },
            { "System.Memory<>", "global::SharpPack.Formatters.MemoryFormatter<TREPLACE>" },
            { "System.ReadOnlyMemory<>", "global::SharpPack.Formatters.ReadOnlyMemoryFormatter<TREPLACE>" },
            { "System.Buffers.ReadOnlySequence<>", "global::SharpPack.Formatters.ReadOnlySequenceFormatter<TREPLACE>" },

            // CollectionFormatters
            { "System.Collections.Generic.List<>", "global::SharpPack.Formatters.ListFormatter<TREPLACE>" },
            { "System.Collections.Generic.Stack<>", "global::SharpPack.Formatters.StackFormatter<TREPLACE>" },
            { "System.Collections.Generic.Queue<>", "global::SharpPack.Formatters.QueueFormatter<TREPLACE>" },
            { "System.Collections.Generic.LinkedList<>", "global::SharpPack.Formatters.LinkedListFormatter<TREPLACE>" },
            { "System.Collections.Generic.HashSet<>", "global::SharpPack.Formatters.HashSetFormatter<TREPLACE>" },
            { "System.Collections.Generic.SortedSet<>", "global::SharpPack.Formatters.SortedSetFormatter<TREPLACE>" },
            { "System.Collections.Generic.PriorityQueue<,>", "global::SharpPack.Formatters.PriorityQueueFormatter<TREPLACE>" },
            { "System.Collections.ObjectModel.ObservableCollection<>", "global::SharpPack.Formatters.ObservableCollectionFormatter<TREPLACE>" },
            { "System.Collections.ObjectModel.Collection<>", "global::SharpPack.Formatters.CollectionFormatter<TREPLACE>" },
            { "System.Collections.Concurrent.ConcurrentQueue<>", "global::SharpPack.Formatters.ConcurrentQueueFormatter<TREPLACE>" },
            { "System.Collections.Concurrent.ConcurrentStack<>", "global::SharpPack.Formatters.ConcurrentStackFormatter<TREPLACE>" },
            { "System.Collections.Concurrent.ConcurrentBag<>", "global::SharpPack.Formatters.ConcurrentBagFormatter<TREPLACE>" },
            { "System.Collections.Generic.Dictionary<,>", "global::SharpPack.Formatters.DictionaryFormatter<TREPLACE>" },
            { "System.Collections.Generic.SortedDictionary<,>", "global::SharpPack.Formatters.SortedDictionaryFormatter<TREPLACE>" },
            { "System.Collections.Generic.SortedList<,>", "global::SharpPack.Formatters.SortedListFormatter<TREPLACE>" },
            { "System.Collections.Concurrent.ConcurrentDictionary<,>", "global::SharpPack.Formatters.ConcurrentDictionaryFormatter<TREPLACE>" },
            { "System.Collections.ObjectModel.ReadOnlyCollection<>", "global::SharpPack.Formatters.ReadOnlyCollectionFormatter<TREPLACE>" },
            { "System.Collections.ObjectModel.ReadOnlyObservableCollection<>", "global::SharpPack.Formatters.ReadOnlyObservableCollectionFormatter<TREPLACE>" },
            { "System.Collections.Concurrent.BlockingCollection<>", "global::SharpPack.Formatters.BlockingCollectionFormatter<TREPLACE>" },

            // ImmutableCollectionFormatters
            { "System.Collections.Immutable.ImmutableArray<>", "global::SharpPack.Formatters.ImmutableArrayFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableList<>", "global::SharpPack.Formatters.ImmutableListFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableQueue<>", "global::SharpPack.Formatters.ImmutableQueueFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableStack<>", "global::SharpPack.Formatters.ImmutableStackFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableDictionary<,>", "global::SharpPack.Formatters.ImmutableDictionaryFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableSortedDictionary<,>", "global::SharpPack.Formatters.ImmutableSortedDictionaryFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableSortedSet<>", "global::SharpPack.Formatters.ImmutableSortedSetFormatter<TREPLACE>" },
            { "System.Collections.Immutable.ImmutableHashSet<>", "global::SharpPack.Formatters.ImmutableHashSetFormatter<TREPLACE>" },
            { "System.Collections.Immutable.IImmutableList<>", "global::SharpPack.Formatters.InterfaceImmutableListFormatter<TREPLACE>" },
            { "System.Collections.Immutable.IImmutableQueue<>", "global::SharpPack.Formatters.InterfaceImmutableQueueFormatter<TREPLACE>" },
            { "System.Collections.Immutable.IImmutableStack<>", "global::SharpPack.Formatters.InterfaceImmutableStackFormatter<TREPLACE>" },
            { "System.Collections.Immutable.IImmutableDictionary<,>", "global::SharpPack.Formatters.InterfaceImmutableDictionaryFormatter<TREPLACE>" },
            { "System.Collections.Immutable.IImmutableSet<>", "global::SharpPack.Formatters.InterfaceImmutableSetFormatter<TREPLACE>" },

            // FrozenCollectionFormatters
            { "System.Collections.Frozen.FrozenDictionary<,>", "global::SharpPack.Formatters.FrozenDictionaryFormatter<TREPLACE>" },
            { "System.Collections.Frozen.FrozenSet<>", "global::SharpPack.Formatters.FrozenSetFormatter<TREPLACE>" },

            // InterfaceCollectionFormatters
            { "System.Collections.Generic.IEnumerable<>", "global::SharpPack.Formatters.InterfaceEnumerableFormatter<TREPLACE>" },
            { "System.Collections.Generic.ICollection<>", "global::SharpPack.Formatters.InterfaceCollectionFormatter<TREPLACE>" },
            { "System.Collections.Generic.IReadOnlyCollection<>", "global::SharpPack.Formatters.InterfaceReadOnlyCollectionFormatter<TREPLACE>" },
            { "System.Collections.Generic.IList<>", "global::SharpPack.Formatters.InterfaceListFormatter<TREPLACE>" },
            { "System.Collections.Generic.IReadOnlyList<>", "global::SharpPack.Formatters.InterfaceReadOnlyListFormatter<TREPLACE>" },
            { "System.Collections.Generic.IDictionary<,>", "global::SharpPack.Formatters.InterfaceDictionaryFormatter<TREPLACE>" },
            { "System.Collections.Generic.IReadOnlyDictionary<,>", "global::SharpPack.Formatters.InterfaceReadOnlyDictionaryFormatter<TREPLACE>" },
            { "System.Linq.ILookup<,>", "global::SharpPack.Formatters.InterfaceLookupFormatter<TREPLACE>" },
            { "System.Linq.IGrouping<,>", "global::SharpPack.Formatters.InterfaceGroupingFormatter<TREPLACE>" },
            { "System.Collections.Generic.ISet<>", "global::SharpPack.Formatters.InterfaceSetFormatter<TREPLACE>" },
            { "System.Collections.Generic.IReadOnlySet<>", "global::SharpPack.Formatters.InterfaceReadOnlySetFormatter<TREPLACE>" },

            { "System.Collections.Generic.KeyValuePair<,>", "global::SharpPack.Formatters.KeyValuePairFormatter<TREPLACE>" },
            { "System.Lazy<>", "global::SharpPack.Formatters.LazyFormatter<TREPLACE>" },

            // TupleFormatters
            { "System.Tuple<>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,,,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,,,,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,,,,,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,,,,,,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.Tuple<,,,,,,,>", "global::SharpPack.Formatters.TupleFormatter<TREPLACE>" },
            { "System.ValueTuple<>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,,,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,,,,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,,,,,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,,,,,,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
            { "System.ValueTuple<,,,,,,,>", "global::SharpPack.Formatters.ValueTupleFormatter<TREPLACE>" },
        };

        public WellKnownTypes(ReferenceSymbols parent)
        {
            this.parent = parent;
            System_Collections_Generic_IEnumerable_T = GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1").ConstructUnboundGenericType();
            System_Collections_Generic_ICollection_T = GetTypeByMetadataName("System.Collections.Generic.ICollection`1").ConstructUnboundGenericType();
            System_Collections_Generic_ISet_T = GetTypeByMetadataName("System.Collections.Generic.ISet`1").ConstructUnboundGenericType();
            System_Collections_Generic_IDictionary_T = GetTypeByMetadataName("System.Collections.Generic.IDictionary`2").ConstructUnboundGenericType();
            System_Collections_Generic_List_T = GetTypeByMetadataName("System.Collections.Generic.List`1").ConstructUnboundGenericType();
            System_Guid = GetTypeByMetadataName("System.Guid");
            System_Version = GetTypeByMetadataName("System.Version");
            System_Uri = GetTypeByMetadataName("System.Uri");
            System_Numerics_BigInteger = GetTypeByMetadataName("System.Numerics.BigInteger");
            System_TimeZoneInfo = GetTypeByMetadataName("System.TimeZoneInfo");
            System_Collections_BitArray = GetTypeByMetadataName("System.Collections.BitArray");
            System_Text_StringBuilder = GetTypeByMetadataName("System.Text.StringBuilder");
            System_Type = GetTypeByMetadataName("System.Type");
            System_Globalization_CultureInfo = GetTypeByMetadataName("System.Globalization.CultureInfo");
            System_Lazy_T = GetTypeByMetadataName("System.Lazy`1").ConstructUnboundGenericType();
            System_Collections_Generic_KeyValuePair_T = GetTypeByMetadataName("System.Collections.Generic.KeyValuePair`2").ConstructUnboundGenericType();
            System_Nullable_T = GetTypeByMetadataName("System.Nullable`1").ConstructUnboundGenericType();
            //System_Memory_T = GetTypeByMetadataName("System.Memory").ConstructUnboundGenericType();
            //System_ReadOnlyMemory_T = GetTypeByMetadataName("System.ReadOnlyMemory").ConstructUnboundGenericType();
            //System_Buffers_ReadOnlySequence_T = GetTypeByMetadataName("System.Buffers.ReadOnlySequence").ConstructUnboundGenericType();
            //System_Collections_Generic_PriorityQueue_T = GetTypeByMetadataName("System.Collections.Generic.PriorityQueue").ConstructUnboundGenericType();

            System_DateTime = GetTypeByMetadataName("System.DateTime");
            System_DateTimeOffset = GetTypeByMetadataName("System.DateTimeOffset");
            System_Runtime_InteropServices_StructLayout = GetTypeByMetadataName("System.Runtime.InteropServices.StructLayoutAttribute");

            knownTypes = new HashSet<ITypeSymbol>(new[]
            {
                System_Collections_Generic_IEnumerable_T,
                System_Collections_Generic_ICollection_T,
                System_Collections_Generic_ISet_T,
                System_Collections_Generic_IDictionary_T,
                System_Version,
                System_Uri,
                System_Numerics_BigInteger,
                System_TimeZoneInfo,
                System_Collections_BitArray,
                System_Text_StringBuilder,
                System_Type,
                System_Globalization_CultureInfo,
                System_Lazy_T,
                System_Collections_Generic_KeyValuePair_T,
                System_Nullable_T,
                //System_Memory_T,
                //System_ReadOnlyMemory_T,
                //System_Buffers_ReadOnlySequence_T,
                //System_Collections_Generic_PriorityQueue_T
            }, SymbolEqualityComparer.Default);
        }

        public bool Contains(ITypeSymbol symbol)
        {
            var constructedSymbol = symbol;
            if (symbol is INamedTypeSymbol nts && nts.IsGenericType)
            {
                symbol = nts.ConstructUnboundGenericType();
            }

            var contains1 = knownTypes.Contains(symbol);
            if (contains1) return true;

            var fullyQualifiedString = symbol.FullyQualifiedToString();
            if (fullyQualifiedString is System_Memory_T or System_ReadOnlyMemory_T or System_Buffers_ReadOnlySequence_T or System_Collections_Generic_PriorityQueue_T)
            {
                return true;
            }

            // tuple
            if (fullyQualifiedString.StartsWith("global::System.Tuple<") || fullyQualifiedString.StartsWith("global::System.ValueTuple<"))
            {
                return true;
            }

            // Most collections are basically serializable, wellknown
            var isIterable = constructedSymbol.AllInterfaces.Any(x => x.EqualsUnconstructedGenericType(System_Collections_Generic_IEnumerable_T));
            if (isIterable)
            {
                return true;
            }

            return false;
        }

        public string? GetNonDefaultFormatterName(ITypeSymbol? type)
        {
            if (type == null) return null;

            if (type.TypeKind == TypeKind.Enum)
            {
                return $"global::SharpPack.Formatters.UnmanagedFormatter<{type.FullyQualifiedToString()}>";
            }

            if (type.TypeKind == TypeKind.Array)
            {
                if (type is IArrayTypeSymbol array)
                {
                    if (array.IsSZArray)
                    {
                        return $"global::SharpPack.Formatters.ArrayFormatter<{array.ElementType.FullyQualifiedToString()}>";
                    }
                    else
                    {
                        if (array.Rank == 2)
                        {
                            return $"global::SharpPack.Formatters.TwoDimensionalArrayFormatter<{array.ElementType.FullyQualifiedToString()}>";
                        }
                        else if (array.Rank == 3)
                        {
                            return $"global::SharpPack.Formatters.ThreeDimensionalArrayFormatter<{array.ElementType.FullyQualifiedToString()}>";
                        }
                        else if (array.Rank == 4)
                        {
                            return $"global::SharpPack.Formatters.FourDimensionalArrayFormatter<{array.ElementType.FullyQualifiedToString()}>";
                        }
                    }
                }

                return null;
            }

            if (type is not INamedTypeSymbol named) return null;

            if (!named.IsGenericType) return null;

            var genericType = named.ConstructUnboundGenericType();
            var genericTypeString = genericType.ToDisplayString();
            var fullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            // var isOpenGenericType = type.TypeArguments.Any(x => x is ITypeParameterSymbol);

            // nullable
            if (genericTypeString == "T?")
            {
                var firstTypeArgument = named.TypeArguments[0];
                var f = "global::SharpPack.Formatters.NullableFormatter<" + firstTypeArgument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">";
                return f;
            }

            // known types
            if (knownGenericTypes.TryGetValue(genericTypeString, out var formatter))
            {
                var typeArgs = string.Join(", ", named.TypeArguments.Select(x => x.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                var f = formatter.Replace("TREPLACE", typeArgs);
                return f;
            }

            return null;
        }

        INamedTypeSymbol GetTypeByMetadataName(string metadataName) => parent.GetTypeByMetadataName(metadataName);
    }
}
