using haworks.Services;
using Microsoft.Extensions.DependencyInjection;
using haworks.Contracts;
using haworks.Webhooks;

public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddAutoMapper(typeof(MappingProfile))
            .AddMemoryCache()
            .AddScoped<PaymentSessionStrategy>()
            .AddScoped<SubscriptionSessionStrategy>()
            .AddScoped<IPaymentProcessingService, PaymentProcessingService>()
            .AddScoped<ISubscriptionProcessingService, SubscriptionProcessingService>()
            .AddScoped<ITelemetryService, ConsoleTelemetryService>()
            .AddScoped<IFileValidator, FileValidator>()
            .AddScoped<IContentStorageService, ContentStorageService>()
            .AddScoped<IChunkedUploadService, ChunkedUploadService>()
            .AddScoped<ICurrentUserService, CurrentUserService>();

        return services;
    }
}
