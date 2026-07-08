namespace BodyLife.Crm.Application.Queries;

public interface IBodyLifeQueryHandler<in TQuery, TResult>
    where TQuery : IBodyLifeQuery<TResult>
{
    Task<TResult> ExecuteAsync(TQuery query, CancellationToken cancellationToken);
}
