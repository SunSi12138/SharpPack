using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace SharpPack.Generator;

internal static class DiagnosticDescriptors
{
    const string Category = "GenerateSharpPack";

    public static readonly DiagnosticDescriptor MustBePartial = new(
        id: "SHARPPACK001",
        title: "SharpPackable object must be partial",
        messageFormat: "The SharpPackable object '{0}' must be partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AbstractMustUnion = new(
        id: "SHARPPACK003",
        title: "abstract/interface type of SharpPackable object must annotate with Union",
        messageFormat: "abstract/interface type of SharpPackable object '{0}' must annotate with Union",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleCtorWithoutAttribute = new(
        id: "SHARPPACK004",
        title: "Require [SharpPackConstructor] when exists multiple constructors",
        messageFormat: "The SharpPackable object '{0}' must annotate with [SharpPackConstructor] when exists multiple constructors",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MultipleCtorAttribute = new(
        id: "SHARPPACK005",
        title: "[SharpPackConstructor] exists in multiple constructors",
        messageFormat: "Mupltiple [SharpPackConstructor] exists in '{0}' but allows only single ctor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ConstructorHasNoMatchedParameter = new(
        id: "SHARPPACK006",
        title: "SharpPackObject's constructor has no matched parameter",
        messageFormat: "The SharpPackable object '{0}' constructor's parameter '{1}' must match a serialized member name(case-insensitive)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OnMethodHasParameter = new(
        id: "SHARPPACK007",
        title: "SharpPackObject's On*** methods must has no parameter",
        messageFormat: "The SharpPackable object '{0}''s '{1}' method must has no parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OnMethodInUnamannagedType = new(
        id: "SHARPPACK008",
        title: "SharpPackObject's On*** methods can't annotate in unamnaged struct",
        messageFormat: "The SharpPackable object '{0}' is unmanaged struct that can't annotate On***Attribute however '{1}' method annotaed",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OverrideMemberCantAddAnnotation = new(
        id: "SHARPPACK009",
        title: "Override member can't annotate Ignore/Include attribute",
        messageFormat: "The SharpPackable object '{0}' override member '{1}' can't annotate {2} attribute",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SealedTypeCantBeUnion = new(
        id: "SHARPPACK010",
        title: "Sealed type can't be union",
        messageFormat: "The SharpPackable object '{0}' is sealed type so can't be Union",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor ConcreteTypeCantBeUnion = new(
        id: "SHARPPACK011",
        title: "Concrete type can't be union",
        messageFormat: "The SharpPackable object '{0}' can be Union, only allow abstract or interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor UnionTagDuplicate = new(
        id: "SHARPPACK012",
        title: "Union tag is duplicate",
        messageFormat: "The SharpPackable object '{0}' union tag value is duplicate",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor UnionMemberTypeNotImplementBaseType = new(
        id: "SHARPPACK013",
        title: "Union member not implement union interface",
        messageFormat: "The SharpPackable object '{0}' union member '{1}' not implement union interface",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);


    public static readonly DiagnosticDescriptor UnionMemberTypeNotDerivedBaseType = new(
        id: "SHARPPACK014",
        title: "Union member not dervided union base type",
        messageFormat: "The SharpPackable object '{0}' union member '{1}' not derived union type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnionMemberNotAllowStruct = new(
        id: "SHARPPACK015",
        title: "Union member can't be struct",
        messageFormat: "The SharpPackable object '{0}' union member '{1}' can't be member, not allows struct",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnionMemberMustBeSharpPackable = new(
        id: "SHARPPACK016",
        title: "Union member must be SharpPackable",
        messageFormat: "The SharpPackable object '{0}' union member '{1}' must be SharpPackable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MembersCountOver250 = new(
        id: "SHARPPACK017",
        title: "Members count limit",
        messageFormat: "The SharpPackable object '{0}' member count is '{1}', however limit size is 249",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberCantSerializeType = new(
        id: "SHARPPACK018",
        title: "Member can't serialize type",
        messageFormat: "The SharpPackable object '{0}' member '{1}' type is '{2}' that can't serialize",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberIsNotSharpPackable = new(
        id: "SHARPPACK019",
        title: "Member is not SharpPackable object",
        messageFormat: "The SharpPackable object '{0}' member '{1}' type '{2}' is not SharpPackable. Annotate [SharpPackable] to '{2}' or if external type that can serialize, annotate `[SharpPackAllowSerialize]` to member",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor TypeIsRefStruct = new(
        id: "SHARPPACK020",
        title: "Type is ref struct",
        messageFormat: "The SharpPackable object '{0}' is ref struct, it can not serialize",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MemberIsRefStruct = new(
        id: "SHARPPACK021",
        title: "Member is ref struct",
        messageFormat: "The SharpPackable object '{0}' member '{1}' type '{2}' is ref struct, it can not serialize",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionGenerateIsAbstract = new(
        id: "SHARPPACK022",
        title: "Collection type not allows interface/abstract",
        messageFormat: "The SharpPackable object '{0}' is GenerateType.Collection but interface/abstract, only allows concrete type",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionGenerateNotImplementedInterface = new(
        id: "SHARPPACK023",
        title: "Collection type must implement collection interface",
        messageFormat: "The SharpPackable object '{0}' is GenerateType.Collection but not implemented collection interface(ICollection<T>/ISet<T>/IDictionary<TKey,TValue>)",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CollectionGenerateNoParameterlessConstructor = new(
        id: "SHARPPACK024",
        title: "Collection type must require parameterless constructor",
        messageFormat: "The SharpPackable object '{0}' is GenerateType.Collection but not exists parameterless constructor",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AllMembersMustAnnotateOrder = new(
        id: "SHARPPACK025",
        title: "All members must annotate SharpPackOrder when SerializeLayout.Explicit",
        messageFormat: "The SharpPackable object '{0}' member '{1}' is not annotated SharpPackOrder",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor AllMembersMustBeContinuousNumber = new(
        id: "SHARPPACK026",
        title: "All SharpPackOrder members must be continuous number from zero",
        messageFormat: "The SharpPackable object '{0}' member '{1}' is not continuous number from zero",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeScriptMustBeSharpPackable = new(
        id: "SHARPPACK027",
        title: "GenerateTypeScript must be SharpPackable",
        messageFormat: "Type '{0}' is annotated GenerateTypeScript but not annotated SharpPackable",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeScriptOnlyAllowsGenerateTypeObject = new(
        id: "SHARPPACK028",
        title: "GenerateTypeScript must be SharpPackable(GenerateType.Object)",
        messageFormat: "Type '{0}' is annotated GenerateTypeScript, its SharpPackable only allows GenerateType.Object",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeScriptDoesNotAllowGenerics = new(
        id: "SHARPPACK029",
        title: "GenerateTypeScript type does not allow generics",
        messageFormat: "Type '{0}' is annotated GenerateTypeScript that does not allow generics parameter",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeScriptDoesNotAllowLongEnum = new(
        id: "SHARPPACK030",
        title: "GenerateTypeScript type does not allow 64bit enum",
        messageFormat: "GenerateTypeScript type '{0}' has not support 64bit(long/ulong) enum type '{1}', 64bit enum is not supported in typescript generation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeScriptNotSupportedType = new(
        id: "SHARPPACK031",
        title: "not allow GenerateTypeScript type",
        messageFormat: "GenerateTypeScript type '{0}' member '{1}' type '{2}' is not supported type in typescript generation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeScriptNotSupportedCustomFormatter = new(
        id: "SHARPPACK032",
        title: "not allow GenerateTypeScript type",
        messageFormat: "GenerateTypeScript type '{0}' member '{1}' is annnotated [SharpPackCustomFormatter] that not supported in typescript generation",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CircularReferenceOnlyAllowsParameterlessConstructor = new(
        id: "SHARPPACK033",
        title: "CircularReference SharpPack Object must require parameterless constructor",
        messageFormat: "The SharpPackable object '{0}' is GenerateType.CircularReference but not exists parameterless constructor.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnamangedStructWithLayoutAutoField = new(
        id: "SHARPPACK034",
        title: "Before .NET 7 unmanaged struct must annotate LayoutKind.Auto or Explicit",
        messageFormat: "The unmanaged struct '{0}' has LayoutKind.Auto field('{1}'). Before .NET 7, if field contains Auto then automatically promote to LayoutKind.Auto but .NET 7 is Sequential so breaking binary compatibility when runtime upgraded. To safety, you have to annotate [StructLayout(LayoutKind.Auto)] or LayoutKind.Explicit to type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnamangedStructSharpPackCtor = new(
        id: "SHARPPACK035",
        title: "Unamanged strcut does not allow [SharpPackConstructor]",
        messageFormat: "The unamanged struct '{0}' can not annotate with [SharpPackConstructor] because don't call any constructors",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InheritTypeCanNotIncludeParentPrivateMember = new(
        id: "SHARPPACK036",
        title: "Inherit type can not include private member",
        messageFormat: "Type '{0}' can not include parent type's private member '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ReadOnlyFieldMustBeConstructorMember = new(
        id: "SHARPPACK037",
        title: "Readonly field must be constructor member",
        messageFormat: "Type '{0}' readonly field '{1}' must be constructor member",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateOrderDoesNotAllow = new(
        id: "SHARPPACK038",
        title: "All members order must be unique",
        messageFormat: "The SharpPackable object '{0}' member '{1}' is duplicated order between '{2}'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor GenerateTypeCannotSpeciyToUnionBaseType = new(
        id: "SHARPPACK039",
        title: "GenerateType cannot be specified for the Union base type itself",
        messageFormat: "The SharpPackable object '{0}' cannot specify '{1}'. Because it is Union base type.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor SuppressDefaultInitializationMustBeSettable = new(
        id: "SHARPPACK040",
        title: "Readonly member cannot specify [SuppressDefaultInitialization]",
        messageFormat: "The SharpPackable object '{0}' member '{1}' has [SuppressDefaultInitialization], it cannot be readonly, init-only and required.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor VersionTolerantOnUnmanagedStruct = new(
        id: "SHARPPACK041",
        title: "Invalid usage of VersionTolerant on unmanaged struct",
        messageFormat: "The unmanaged struct '{0}' cannot be used for VersionTolerant serialization.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NestedContainingTypesMustBePartial = new(
        id: "SHARPPACK042",
        title: "Nested SharpPackable object's containing type(s) must be partial",
        messageFormat: "The SharpPackable object '{0}' containing type(s) must be partial",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
