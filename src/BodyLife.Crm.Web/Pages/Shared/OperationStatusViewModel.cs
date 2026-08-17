namespace BodyLife.Crm.Web.Pages.Shared;

public sealed record OperationStatusViewModel(
    string? Message,
    string Tone = "success",
    string? Context = null,
    bool IsUpdateFragment = false)
{
    public bool IsVisible => !string.IsNullOrWhiteSpace(Message);

    public string NormalizedTone => Tone is "info" or "warning" or "error" ? Tone : "success";

    public bool AutoDismiss => IsVisible && (NormalizedTone is "success" or "info");

    public static OperationStatusViewModel Empty { get; } = new((string?)null);
}
