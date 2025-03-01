using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using haworks.Services;

namespace Haworks.Tests
{
    public class FakeVaultService : VaultService
    {
        public FakeVaultService()
            : base(
                  Options.Create(new VaultOptions
                  {
                      Address = "http://127.0.0.1:8200",
                      RoleIdPath = CreateTempFile("dummy-role-id"),
                      SecretIdPath = CreateTempFile("dummy-secret-id")
                  }),
                  Options.Create(new DatabaseOptions
                  {
                      Host = "localhost"
                  }),
                  new LoggerFactory().CreateLogger<VaultService>()
              )
        {
        }

        private static string CreateTempFile(string content)
        {
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, content);
            return tempFile;
        }

        // For tests we simply return a fixed, safe connection string.
        public override Task<string> GetDatabaseConnectionStringAsync(CancellationToken ct = default)
        {
            return Task.FromResult("User ID=myuser;Password=mypassword;Host=localhost;Port=5433;Database=test_db;SSL Mode=Disable;");
        }

        // Override GetDatabaseCredentialsAsync to always return a safe lease and credentials.
        public override Task<(string Username, SecureString Password)> GetDatabaseCredentialsAsync(CancellationToken ct = default)
        {
            // In this fake service, we simulate a valid lease of 3600 seconds.
            LeaseDuration = TimeSpan.FromSeconds(3600);
            LeaseExpiry = DateTime.UtcNow.Add(LeaseDuration);

            var securePwd = new SecureString();
            foreach (var c in "mypassword")
            {
                securePwd.AppendChar(c);
            }
            securePwd.MakeReadOnly();

            return Task.FromResult(("myuser", securePwd));
        }

        // Optionally, if your production code calls RefreshCredentials, you can override it as well.
        public override Task RefreshCredentials(CancellationToken ct = default)
        {
            // Simply update credentials with a safe lease.
            LeaseDuration = TimeSpan.FromSeconds(3600);
            LeaseExpiry = DateTime.UtcNow.Add(LeaseDuration);

            var securePwd = new SecureString();
            foreach (var c in "mypassword")
            {
                securePwd.AppendChar(c);
            }
            securePwd.MakeReadOnly();

            // If the base class stores the credentials in a field, ensure that it gets updated.
            // (Assuming _credentials is accessible via a protected method or property in your real code.)
            // For this fake, we assume that GetDatabaseCredentialsAsync will be used.

            return Task.CompletedTask;
        }
    }
}
