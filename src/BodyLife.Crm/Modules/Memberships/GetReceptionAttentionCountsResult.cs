namespace BodyLife.Crm.Modules.Memberships;

public enum GetReceptionAttentionCountsStatus { Success, PermissionDenied, ValidationFailed, RecalculationFailed, SourceInconsistent }

public sealed record GetReceptionAttentionCountsResult(
    GetReceptionAttentionCountsStatus Status, int? EndingSoonMembershipCount, int? NegativeClientCount, string? ErrorCode, string? ErrorMessage, string? ErrorField)
{
    public static GetReceptionAttentionCountsResult Success(int endingSoon, int negativeClients)
    {
        if (endingSoon < 0 || negativeClients < 0) throw new ArgumentOutOfRangeException();
        return new(GetReceptionAttentionCountsStatus.Success, endingSoon, negativeClients, null, null, null);
    }

    public static GetReceptionAttentionCountsResult Failure(GetReceptionAttentionCountsStatus status, string code, string message, string? field = null)
    {
        if (status == GetReceptionAttentionCountsStatus.Success || !Enum.IsDefined(status)
            || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Failure requires a non-success status and error details.");
        return new(status, null, null, code, message, field);
    }
}
