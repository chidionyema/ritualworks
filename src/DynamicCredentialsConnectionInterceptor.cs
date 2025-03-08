using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Haworks.Services;

public class DynamicCredentialsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly VaultService _vault;

    public DynamicCredentialsConnectionInterceptor(VaultService vault)
    {
        _vault = vault;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        // Update the connection string with fresh credentials
        connection.ConnectionString = await _vault.GetDatabaseConnectionString();
        // Return the result from the base implementation
        return await base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }

    public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData)
    {
        // If the connection fails because of invalid credentials, clear the pool.
        if (eventData.Exception.Message.Contains("invalid password", StringComparison.InvariantCultureIgnoreCase))
        {
            NpgsqlConnection.ClearPool((NpgsqlConnection)connection);
        }
        base.ConnectionFailed(connection, eventData);
    }
}
