namespace BodyLife.Crm.Application.Queries;

public sealed record QueryPermissionResult(
    string ActionKey,
    string RequiredPolicy,
    bool IsAllowed,
    string? DeniedReasonCode,
    string? DeniedReason)
{
    public static QueryPermissionResult Allowed(string actionKey, string requiredPolicy)
    {
        return new QueryPermissionResult(
            NormalizeRequiredValue(actionKey, nameof(actionKey)),
            NormalizeRequiredValue(requiredPolicy, nameof(requiredPolicy)),
            IsAllowed: true,
            DeniedReasonCode: null,
            DeniedReason: null);
    }

    public static QueryPermissionResult Denied(
        string actionKey,
        string requiredPolicy,
        string deniedReasonCode,
        string deniedReason)
    {
        return new QueryPermissionResult(
            NormalizeRequiredValue(actionKey, nameof(actionKey)),
            NormalizeRequiredValue(requiredPolicy, nameof(requiredPolicy)),
            IsAllowed: false,
            NormalizeRequiredValue(deniedReasonCode, nameof(deniedReasonCode)),
            NormalizeRequiredValue(deniedReason, nameof(deniedReason)));
    }

    private static string NormalizeRequiredValue(string value, string parameterName)
    {
        var trimmed = value.Trim();

        return string.IsNullOrWhiteSpace(trimmed)
            ? throw new ArgumentException("Value cannot be blank.", parameterName)
            : trimmed;
    }
}
