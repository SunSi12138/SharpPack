using Microsoft.CodeAnalysis;

namespace SharpPack.Generator;

internal readonly record struct FormatterDependency(string Segment, ITypeSymbol Type);
internal sealed record UnsupportedFormatterShape(string? Member, string Reason);

public partial class TypeMeta
{
    // A sidecar uses the same member discovery, ordering and constructor
    // matching as an ordinary object. Unlike a partial type it can only call
    // members that are accessible from the consuming assembly.
    internal static (SerializationPlan? Plan, FormatterDependency[] Dependencies, UnsupportedFormatterShape? Error)
        PlanExternalObject(INamedTypeSymbol type, ReferenceSymbols references)
    {
        var compilation = references.Compilation;
        bool Accessible(ISymbol? symbol) => symbol is not null && compilation.IsSymbolAccessibleWithin(symbol, compilation.Assembly);

        if (type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || type.IsAbstract || type.IsStatic ||
            type.IsRefLikeType || type.IsAnonymousType || !type.CanBeReferencedByName || !Accessible(type))
        {
            return Failed(null, "the type is not an accessible, concrete class or struct");
        }

        bool IsSelected(IMethodSymbol constructor) => constructor.ContainsAttribute(references.SharpPackConstructorAttribute);
        static bool CanPassArguments(IMethodSymbol constructor) => constructor.Parameters.All(parameter =>
            parameter.RefKind is RefKind.None or RefKind.In);
        var model = new TypeMeta(type, references, constructor =>
            IsSelected(constructor) || Accessible(constructor) && CanPassArguments(constructor));

        if (model.IsUnion)
            return Failed(null, "union metadata requires an existing SharpPack union formatter");
        if (model.OnSerializing.Length + model.OnSerialized.Length + model.OnDeserializing.Length + model.OnDeserialized.Length != 0)
            return Failed(null, "SharpPack callbacks require an existing formatter");
        if (model.ctorInvalid is { } invalid)
            return Failed(null, Diagnostic.Create(invalid, Location.None, type.Name).GetMessage());

        var constructor = model.Constructor ?? type.InstanceConstructors.FirstOrDefault(candidate =>
            candidate.Parameters.IsEmpty && Accessible(candidate));
        if (constructor is null || !Accessible(constructor) || !CanPassArguments(constructor))
            return Failed(null, "no accessible SharpPack constructor can be called with by-value or in arguments");

        foreach (var parameter in constructor.Parameters)
        {
            var matching = model.Members.Where(member =>
                string.Equals(member.ConstructorParameterName, parameter.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length != 1 || !SymbolEqualityComparer.Default.Equals(matching[0].MemberType, parameter.Type))
                return Failed(null, $"constructor parameter '{parameter.Name}' must match exactly one serialized member of the same type");
        }

        var members = model.Members;
        if (members.Length > MaximumObjectMembers)
            return Failed(null, $"an ordinary SharpPack object can have at most {MaximumObjectMembers} members");

        var explicitOrder = members.Any(static member => member.HasExplicitOrder);
        if (explicitOrder && members.Where((member, index) => !member.HasExplicitOrder || member.Order != index).Any())
            return Failed(null, "explicit SharpPackOrder values must cover every member exactly once, starting at zero");

        if (!explicitOrder && members.GroupBy(static member => member.Symbol.ContainingType, SymbolEqualityComparer.Default)
                .Any(static group => !group.Key!.Locations.Any(static location => location.IsInSource) &&
                                     group.Any(static member => member.IsField) && group.Any(static member => member.IsProperty)))
        {
            return Failed(null, "metadata does not retain the relative declaration order of fields and properties; use explicit SharpPackOrder values");
        }

        foreach (var member in members)
        {
            if (member.Kind == MemberKind.CustomFormatter)
                return Failed(member.Name, "member-level formatter attributes require an existing object formatter");
            if (!Accessible(member.Symbol) || member.Symbol is IPropertySymbol property && !Accessible(property.GetMethod))
                return Failed(member.Name, "the serialized member is not readable from the consuming assembly");
            if (!member.IsConstructorParameter && (!member.IsSettable ||
                member.Symbol is IPropertySymbol writable && !Accessible(writable.SetMethod)))
            {
                return Failed(member.Name, "the member needs an accessible setter or a matching constructor parameter");
            }
            if (member.SuppressDefaultInitialization && (!member.IsAssignable || member.IsConstructorParameter))
                return Failed(member.Name, "SuppressDefaultInitialization requires a writable member outside the constructor");
        }

        var setsRequiredMembers = compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute");
        if (setsRequiredMembers is null || !constructor.ContainsAttribute(setsRequiredMembers))
        {
            var initialized = new HashSet<ISymbol>(
                members.Where(static member => !member.IsConstructorParameter).Select(static member => member.Symbol),
                SymbolEqualityComparer.Default);
            foreach (var required in type.GetAllMembers().Where(static member =>
                         member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                if (!initialized.Contains(required))
                    return Failed(required.Name, "the generated initializer cannot satisfy this required member");
            }
        }

        var shape = TypeShape.Create(type, model, references) with
        {
            GenerateType = GenerateType.Object,
            Layout = explicitOrder ? SerializeLayout.Explicit : SerializeLayout.Sequential,
            ConstructorParameters = new EquatableArray<ParameterShape>(constructor.Parameters.Select(static parameter =>
                new ParameterShape(parameter.Name, parameter.Type.FullyQualifiedToString(), parameter.RefKind == RefKind.In))),
        };
        return (SerializationPlanner.Plan(shape),
            members.Select(static member => new FormatterDependency("." + member.Name, member.MemberType)).ToArray(), null);

        static (SerializationPlan?, FormatterDependency[], UnsupportedFormatterShape)
            Failed(string? member, string reason) => (null, [], new(member, reason));
    }
}
