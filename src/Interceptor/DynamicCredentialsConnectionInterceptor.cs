using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using haworks.Contracts;

public class DynamicCredentialsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public DynamicCredentialsConnectionInterceptor(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default)
    {
        // Retrieve the connection string asynchronously
        var connectionString = await _connectionStringProvider.GetConnectionStringAsync();
        connection.ConnectionString = connectionString;

        // Call the base method to continue the interception process
        return await base.ConnectionOpeningAsync(connection, eventData, result, cancellationToken);
    }
}
