namespace BodyLife.Crm.Application.Commands;

public sealed record CommandError(
    CommandErrorCode Code,
    string Message,
    string? Field = null);
