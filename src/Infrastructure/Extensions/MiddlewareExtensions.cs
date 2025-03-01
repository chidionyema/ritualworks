using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using haworks.Extensions;
using haworks.Services;
using System.Threading.Tasks;

public static class MiddlewareExtensions
{
    public static void ConfigureMiddlewarePipeline(this WebApplication app)
    {
        // Keep the rest of your pipeline.
        app.UseExceptionHandler("/error")
           .UseHsts()
           .UseHttpsRedirection()
           .UseSecurityHeaders(app.Configuration)
           .UseStaticFiles()
           .UseRouting()
           .UseCors("Allow3001")
           .UseAuthentication()
           .UseAuthorization()
           .UseRateLimiter()
           .UseMiddleware<ExceptionHandlingMiddleware>();
        
        // Only enable Swagger if NOT "Test" environment:
        if (!app.Environment.IsEnvironment("Test"))
        {
            app.UseSwagger();
            app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"));
        }

        app.MapControllers();
    }

    public static async Task InitializeVaultServiceAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var vault = scope.ServiceProvider.GetRequiredService<IVaultService>();
        await vault.InitializeAsync();
    }
}
