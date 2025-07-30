using Scalar.AspNetCore;

namespace MandiocaCozidinha.Services.Api.Configurations
{
    public static class OpenApiConfiguration
    {
        public static void AddOpenConfig(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddOpenApi();
        }

        public static IApplicationBuilder UseOpenApiWithScalarConfig(this WebApplication app)
        {
            app.MapOpenApi("/openapi/{documentName}.json");
            app.MapScalarApiReference(options =>
            {
                options
                    .WithOpenApiRoutePattern("/openapi/{documentName}.json")
                    .WithTitle("Custom API")
                    .WithSidebar(true);
            });

            return app;
        }
    }
}