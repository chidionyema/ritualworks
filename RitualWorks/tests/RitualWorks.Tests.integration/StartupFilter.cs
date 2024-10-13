using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using System;

namespace RitualWorks.Tests
{
    // Custom StartupFilter to add TestAuthenticationMiddleware
    public class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return builder =>
            {
                builder.UseMiddleware<TestAuthenticationMiddleware>();
                builder.UseAuthentication();
                builder.UseAuthorization();
                next(builder);
            };
        }
    }
}
