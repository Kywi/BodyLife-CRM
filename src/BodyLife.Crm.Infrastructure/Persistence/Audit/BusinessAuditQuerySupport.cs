using System.Text.Json;
using BodyLife.Crm.Application.Commands;
using BodyLife.Crm.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace BodyLife.Crm.Infrastructure.Persistence.Audit;

internal static class BusinessAuditQuerySupport
{
    internal const string ClientEntityType = "client";

    private static readonly JsonSerializerOptions AuditJsonOptions = new(
        JsonSerializerDefaults.Web);

    internal static IQueryable<BusinessAuditEntryRecord> WhereLinkedToClient(
        IQueryable<BusinessAuditEntryRecord> entries,
        Guid clientId)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var scalarClientReference = JsonSerializer.Serialize(
            new ScalarClientReference(clientId),
            AuditJsonOptions);
        var affectedClientReference = JsonSerializer.Serialize(
            new AffectedClientReference([clientId]),
            AuditJsonOptions);
        return entries.Where(entry =>
            (entry.EntityType == ClientEntityType && entry.EntityId == clientId)
            || EF.Functions.JsonContains(
                entry.RelatedEntityRefsJson,
                scalarClientReference)
            || EF.Functions.JsonContains(
                entry.RelatedEntityRefsJson,
                affectedClientReference));
    }

    internal static AccountKind MapAccountKind(string accountType)
    {
        return accountType switch
        {
            "owner" => AccountKind.Owner,
            "named_admin" => AccountKind.NamedAdmin,
            "shared_reception_admin" => AccountKind.SharedReceptionAdmin,
            _ => throw new InvalidOperationException(
                $"Unsupported audit actor account type '{accountType}'."),
        };
    }

    internal static ActorRole MapActorRole(string role)
    {
        return role switch
        {
            "owner" => ActorRole.Owner,
            "admin" => ActorRole.Admin,
            _ => throw new InvalidOperationException(
                $"Unsupported audit actor role '{role}'."),
        };
    }

    internal static EntryOrigin MapEntryOrigin(string entryOrigin)
    {
        return entryOrigin switch
        {
            "normal" => EntryOrigin.Normal,
            "manual_backfill" => EntryOrigin.ManualBackfill,
            "paper_fallback" => EntryOrigin.PaperFallback,
            "future_import" => EntryOrigin.FutureImport,
            _ => throw new InvalidOperationException(
                $"Unsupported audit entry origin '{entryOrigin}'."),
        };
    }

    private sealed record ScalarClientReference(Guid ClientId);

    private sealed record AffectedClientReference(IReadOnlyList<Guid> AffectedClientIds);
}
