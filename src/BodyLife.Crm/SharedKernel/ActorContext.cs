namespace BodyLife.Crm.SharedKernel;

public sealed record ActorContext(
    AccountId AccountId,
    ActorRole Role,
    AccountKind AccountKind,
    SessionId SessionId,
    string? DeviceLabel);
