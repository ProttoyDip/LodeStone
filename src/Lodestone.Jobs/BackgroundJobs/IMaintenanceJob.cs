namespace Lodestone.Jobs.BackgroundJobs;

/// <summary>
/// A recurring supporting sweep. The shared shape lets the scheduler register any of them
/// generically without a cast, which Hangfire's expression serialization cannot represent.
/// </summary>
public interface IMaintenanceJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
}
