using ComdisAI.Data;

namespace ComdisAI.Repositories;

public sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    private readonly Dictionary<Type, object> _repositories = [];

    public IGenericRepository<T> Repository<T>() where T : class
    {
        if (!_repositories.TryGetValue(typeof(T), out var repo))
        {
            repo = new GenericRepository<T>(context);
            _repositories[typeof(T)] = repo;
        }
        return (IGenericRepository<T>)repo;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    public ValueTask DisposeAsync() => context.DisposeAsync();
}
