using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Haworks.Services; // Ensure this matches the namespace where VaultService is defined

namespace Haworks.Tests
{
    public class FakeVaultService : VaultService
    {
        public FakeVaultService() 
            : base(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    { "Vault:Address", "http://127.0.0.1:8200" },
                    // Supply file paths that exist by creating temporary files with dummy content.
                    { "Vault:RoleIdPath", CreateTempFile("dummy-role-id") },
                    { "Vault:SecretIdPath", CreateTempFile("dummy-secret-id") }
                })
                .Build())
        {
        }

        private static string CreateTempFile(string content)
        {
            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, content);
            return tempFile;
        }

        public override Task<string> GetDatabaseConnectionString()
        {
            // Return the connection string your integration tests expect.
            return Task.FromResult("User ID=myuser;Password=mypassword;Host=localhost;Port=5433;Database=test_db;SSL Mode=Disable;");
        }
    }
}
