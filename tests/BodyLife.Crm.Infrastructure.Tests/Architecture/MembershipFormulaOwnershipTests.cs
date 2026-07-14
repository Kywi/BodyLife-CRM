using System.Reflection;
using System.Reflection.Emit;
using BodyLife.Crm.Infrastructure;
using BodyLife.Crm.Modules.Memberships;

namespace BodyLife.Crm.Infrastructure.Tests.Architecture;

public sealed class MembershipFormulaOwnershipTests
{
    [Fact]
    public void ProductionModulesOutsideMembershipsUseOnlyApprovedMembershipContracts()
    {
        Assembly[] productionAssemblies =
        [
            typeof(MembershipsModule).Assembly,
            typeof(ServiceCollectionExtensions).Assembly,
            typeof(global::Program).Assembly,
        ];

        var violations = MembershipDependencyInspector.FindForbiddenReferences(
            productionAssemblies
                .Distinct()
                .SelectMany(assembly => assembly.GetTypes()));

        Assert.True(
            violations.Count == 0,
            "Production code outside Memberships references Memberships-owned formula "
            + "implementations or an unreviewed contract:"
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void OwnershipInspectorDetectsFormulaCallsOutsideMemberships()
    {
        var violations = MembershipDependencyInspector.FindForbiddenReferences(
            [typeof(OutsideMembershipsFormulaFixture)]);

        Assert.Contains(
            $"{typeof(OutsideMembershipsFormulaFixture).FullName} -> "
            + typeof(MembershipDateRules).FullName,
            violations);
    }

    private static class OutsideMembershipsFormulaFixture
    {
        public static DateOnly CalculateBaseEndDate(DateOnly startDate)
        {
            return MembershipDateRules.CalculateBaseEndDate(startDate, durationDays: 30);
        }
    }
}

internal static class MembershipDependencyInspector
{
    private const string MembershipsNamespace = "BodyLife.Crm.Modules.Memberships";
    private const string MembershipsInfrastructureNamespace
        = "BodyLife.Crm.Infrastructure.Persistence.Memberships";
    private static readonly BindingFlags DeclaredMembers = BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static
        | BindingFlags.DeclaredOnly;
    private static readonly Assembly MembershipsAssembly = typeof(MembershipsModule).Assembly;
    private static readonly Assembly InfrastructureAssembly
        = typeof(ServiceCollectionExtensions).Assembly;
    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(obj: null)!)
        .ToDictionary(opCode => unchecked((ushort)opCode.Value));
    private static readonly HashSet<Type> ApprovedMembershipContracts =
    [
        typeof(MembershipsModule),
        typeof(MembershipActionKeys),
        typeof(MembershipWarningCodes),
        typeof(MembershipWarningSeverity),
        typeof(MembershipWarning),
        typeof(MembershipExtensionDay),
        typeof(IssuedMembershipSnapshot),
        typeof(GetMembershipStateQuery),
        typeof(GetMembershipStateResult),
        typeof(GetMembershipStateStatus),
        typeof(MembershipStateReadModel),
        typeof(GetClientMembershipStatesQuery),
        typeof(GetClientMembershipStatesResult),
        typeof(GetClientMembershipStatesStatus),
        typeof(ClientMembershipStatesReadModel),
        typeof(ClientMembershipStateTimelineItem),
        typeof(ActiveMembershipCandidateSelection),
        typeof(ActiveMembershipCandidateStatus),
        typeof(IssuedMembershipLifecycleStatus),
        typeof(PreviewIssueMembershipQuery),
        typeof(PreviewIssueMembershipResult),
        typeof(PreviewIssueMembershipStatus),
        typeof(MembershipIssuePreview),
        typeof(MembershipIssueNegativeContext),
        typeof(MembershipNegativeHandlingDecision),
        typeof(MembershipNegativeHandlingOption),
        typeof(CreateMembershipOpeningStateCommand),
        typeof(IssueMembershipCommand),
        typeof(MembershipVisitEligibility),
        typeof(MembershipVisitAcknowledgement),
    ];

    public static IReadOnlyList<string> FindForbiddenReferences(IEnumerable<Type> sourceTypes)
    {
        ArgumentNullException.ThrowIfNull(sourceTypes);

        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var sourceType in sourceTypes.Where(type => !IsMembershipsOwner(type)))
        {
            foreach (var referencedType in GetReferencedTypes(sourceType))
            {
                var normalizedType = Normalize(referencedType);

                if (IsMembershipsType(normalizedType)
                    && !ApprovedMembershipContracts.Contains(normalizedType))
                {
                    violations.Add(
                        $"{sourceType.FullName ?? sourceType.Name} -> "
                        + (normalizedType.FullName ?? normalizedType.Name));
                }
            }
        }

        return violations.ToArray();
    }

    private static IReadOnlyCollection<Type> GetReferencedTypes(Type sourceType)
    {
        var referencedTypes = new HashSet<Type>();

        AddTypeShape(sourceType.BaseType, referencedTypes);

        foreach (var interfaceType in sourceType.GetInterfaces())
        {
            AddTypeShape(interfaceType, referencedTypes);
        }

        foreach (var genericArgument in sourceType.GetGenericArguments())
        {
            AddTypeShape(genericArgument, referencedTypes);
        }

        foreach (var field in sourceType.GetFields(DeclaredMembers))
        {
            AddTypeShape(field.FieldType, referencedTypes);
        }

        foreach (var property in sourceType.GetProperties(DeclaredMembers))
        {
            AddTypeShape(property.PropertyType, referencedTypes);

            foreach (var indexParameter in property.GetIndexParameters())
            {
                AddTypeShape(indexParameter.ParameterType, referencedTypes);
            }
        }

        foreach (var eventInfo in sourceType.GetEvents(DeclaredMembers))
        {
            AddTypeShape(eventInfo.EventHandlerType, referencedTypes);
        }

        var methods = sourceType.GetMethods(DeclaredMembers)
            .Cast<MethodBase>()
            .Concat(sourceType.GetConstructors(DeclaredMembers));

        if (sourceType.TypeInitializer is { } typeInitializer)
        {
            methods = methods.Append(typeInitializer);
        }

        foreach (var method in methods.Distinct())
        {
            AddMethodShape(method, referencedTypes);
            AddMethodBodyReferences(sourceType, method, referencedTypes);
        }

        return referencedTypes;
    }

    private static void AddMethodShape(MethodBase method, ISet<Type> referencedTypes)
    {
        AddTypeShape(method.DeclaringType, referencedTypes);

        if (method is MethodInfo methodInfo)
        {
            AddTypeShape(methodInfo.ReturnType, referencedTypes);
        }

        foreach (var parameter in method.GetParameters())
        {
            AddTypeShape(parameter.ParameterType, referencedTypes);
        }

        if (!method.IsGenericMethod)
        {
            return;
        }

        foreach (var genericArgument in method.GetGenericArguments())
        {
            AddTypeShape(genericArgument, referencedTypes);
        }
    }

    private static void AddMethodBodyReferences(
        Type sourceType,
        MethodBase method,
        ISet<Type> referencedTypes)
    {
        var methodBody = method.GetMethodBody();
        var instructions = methodBody?.GetILAsByteArray();

        if (methodBody is null || instructions is null)
        {
            return;
        }

        foreach (var local in methodBody.LocalVariables)
        {
            AddTypeShape(local.LocalType, referencedTypes);
        }

        foreach (var clause in methodBody.ExceptionHandlingClauses)
        {
            if (clause.Flags == ExceptionHandlingClauseOptions.Clause)
            {
                AddTypeShape(clause.CatchType, referencedTypes);
            }
        }

        using var stream = new MemoryStream(instructions, writable: false);
        using var reader = new BinaryReader(stream);

        while (stream.Position < stream.Length)
        {
            var firstByte = reader.ReadByte();
            var operationValue = firstByte == 0xfe
                ? (ushort)(0xfe00 | reader.ReadByte())
                : firstByte;

            if (!OpCodesByValue.TryGetValue(operationValue, out var operation))
            {
                throw new InvalidOperationException(
                    $"Unknown IL operation 0x{operationValue:x4} in "
                    + $"{sourceType.FullName}.{method.Name}.");
            }

            ReadOperand(sourceType, method, operation.OperandType, reader, referencedTypes);
        }
    }

    private static void ReadOperand(
        Type sourceType,
        MethodBase method,
        OperandType operandType,
        BinaryReader reader,
        ISet<Type> referencedTypes)
    {
        switch (operandType)
        {
            case OperandType.InlineNone:
                return;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                Skip(reader, byteCount: 1);
                return;
            case OperandType.InlineVar:
                Skip(reader, byteCount: 2);
                return;
            case OperandType.InlineBrTarget:
            case OperandType.InlineI:
            case OperandType.InlineSig:
            case OperandType.InlineString:
            case OperandType.ShortInlineR:
                Skip(reader, byteCount: 4);
                return;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                Skip(reader, byteCount: 8);
                return;
            case OperandType.InlineSwitch:
                var targetCount = reader.ReadInt32();
                Skip(reader, checked(targetCount * sizeof(int)));
                return;
            case OperandType.InlineField:
            case OperandType.InlineMethod:
            case OperandType.InlineTok:
            case OperandType.InlineType:
                ResolveMemberTypes(
                    sourceType,
                    method,
                    reader.ReadInt32(),
                    referencedTypes);
                return;
            default:
                throw new InvalidOperationException(
                    $"Unsupported IL operand type '{operandType}' in "
                    + $"{sourceType.FullName}.{method.Name}.");
        }
    }

    private static void ResolveMemberTypes(
        Type sourceType,
        MethodBase sourceMethod,
        int metadataToken,
        ISet<Type> referencedTypes)
    {
        var typeArguments = sourceType.IsGenericType
            ? sourceType.GetGenericArguments()
            : Type.EmptyTypes;
        var methodArguments = sourceMethod.IsGenericMethod
            ? sourceMethod.GetGenericArguments()
            : Type.EmptyTypes;
        var member = sourceMethod.Module.ResolveMember(
            metadataToken,
            typeArguments,
            methodArguments);

        switch (member)
        {
            case Type memberType:
                AddTypeShape(memberType, referencedTypes);
                break;
            case FieldInfo field:
                AddTypeShape(field.DeclaringType, referencedTypes);
                AddTypeShape(field.FieldType, referencedTypes);
                break;
            case MethodBase method:
                AddMethodShape(method, referencedTypes);
                break;
        }
    }

    private static void AddTypeShape(Type? type, ISet<Type> referencedTypes)
    {
        if (type is null || !referencedTypes.Add(type))
        {
            return;
        }

        if (type.HasElementType)
        {
            AddTypeShape(type.GetElementType(), referencedTypes);
        }

        if (type.IsGenericType)
        {
            AddTypeShape(type.GetGenericTypeDefinition(), referencedTypes);

            foreach (var genericArgument in type.GetGenericArguments())
            {
                AddTypeShape(genericArgument, referencedTypes);
            }
        }

        if (type.IsGenericParameter)
        {
            foreach (var constraint in type.GetGenericParameterConstraints())
            {
                AddTypeShape(constraint, referencedTypes);
            }
        }
    }

    private static void Skip(BinaryReader reader, int byteCount)
    {
        reader.BaseStream.Seek(byteCount, SeekOrigin.Current);
    }

    private static Type Normalize(Type type)
    {
        while (type.HasElementType)
        {
            type = type.GetElementType()!;
        }

        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? type.GetGenericTypeDefinition()
            : type;
    }

    private static bool IsMembershipsType(Type type)
    {
        return type.Assembly == MembershipsAssembly
            && IsInNamespace(type.Namespace, MembershipsNamespace);
    }

    private static bool IsMembershipsOwner(Type type)
    {
        return type.Assembly == MembershipsAssembly
            && IsInNamespace(type.Namespace, MembershipsNamespace)
            || type.Assembly == InfrastructureAssembly
            && IsInNamespace(type.Namespace, MembershipsInfrastructureNamespace);
    }

    private static bool IsInNamespace(string? candidate, string ownerNamespace)
    {
        return string.Equals(candidate, ownerNamespace, StringComparison.Ordinal)
            || candidate?.StartsWith(ownerNamespace + ".", StringComparison.Ordinal) == true;
    }
}
