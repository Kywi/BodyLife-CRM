using BodyLife.Crm.Application.Commands;

namespace BodyLife.Crm.Modules.NonWorkingDays;

public sealed class CorrectNonWorkingDayPreparationResult
{
    private CorrectNonWorkingDayPreparationResult(
        CorrectNonWorkingDayPreparation? preparation,
        IReadOnlyList<CommandError> errors)
    {
        Preparation = preparation;
        Errors = errors;
    }

    public CorrectNonWorkingDayPreparation? Preparation { get; }

    public IReadOnlyList<CommandError> Errors { get; }

    public bool IsPrepared => Preparation is not null;

    internal static CorrectNonWorkingDayPreparationResult Prepared(
        CorrectNonWorkingDayPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        return new CorrectNonWorkingDayPreparationResult(preparation, []);
    }

    internal static CorrectNonWorkingDayPreparationResult Rejected(
        CommandErrorCode code,
        string message,
        string? field)
    {
        return new CorrectNonWorkingDayPreparationResult(
            preparation: null,
            Array.AsReadOnly([new CommandError(code, message, field)]));
    }
}
