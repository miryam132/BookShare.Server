namespace BookShare.Application.Interfaces;

public interface IDatabaseHealthCheck
{
    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}
