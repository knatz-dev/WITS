using Dapper;
using WITS.Data.Contracts;

namespace WITS.Services;

public interface ITicketCollaboratorService
{
    Task<bool> AddAsync(long ticketId, long userId, CancellationToken cancellationToken = default);
    Task<bool> RemoveAsync(long ticketId, long userId, CancellationToken cancellationToken = default);
}

public class TicketCollaboratorService : ITicketCollaboratorService
{
    private readonly IDbConnectionFactory _connectionFactory;

    public TicketCollaboratorService(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> AddAsync(long ticketId, long userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "INSERT OR IGNORE INTO TicketCollaborators (TicketId, UserId) VALUES (@TicketId, @UserId);",
            new { TicketId = ticketId, UserId = userId },
            cancellationToken: cancellationToken));
        return true;
    }

    public async Task<bool> RemoveAsync(long ticketId, long userId, CancellationToken cancellationToken = default)
    {
        using var connection = await _connectionFactory.CreateConnectionAsync(cancellationToken);
        var rows = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TicketCollaborators WHERE TicketId = @TicketId AND UserId = @UserId;",
            new { TicketId = ticketId, UserId = userId },
            cancellationToken: cancellationToken));
        return rows > 0;
    }
}