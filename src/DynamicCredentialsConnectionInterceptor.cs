using RitualWorks.Contracts;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

public class DynamicCredentialsConnectionInterceptor : DbConnectionInterceptor
{
    private readonly IConnectionStringProvider _connectionStringProvider;

    public DynamicCredentialsConnectionInterceptor(IConnectionStringProvider connectionStringProvider)
    {
        _connectionStringProvider = connectionStringProvider;
    }

    public override InterceptionResult ConnectionOpening(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        connection.ConnectionString = _connectionStringProvider.GetConnectionString();
        return base.ConnectionOpening(connection, eventData, result);
    }
}
