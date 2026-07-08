namespace BodyLife.Crm.Application.Commands;

public interface IBodyLifeCommandHandler<in TCommand>
    where TCommand : IBodyLifeCommand
{
    Task<CommandResult> ExecuteAsync(TCommand command, CancellationToken cancellationToken);
}
