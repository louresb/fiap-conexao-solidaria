using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace ConexaoSolidaria.Infrastructure.OpenApi;

public static class OpenApiServiceCollectionExtensions
{
    public static IServiceCollection AddConexaoSolidariaOpenApi(
        this IServiceCollection services,
        string title,
        string description)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer(async (document, context, _) =>
            {
                document.Info.Title = title;
                document.Info.Description = description;
                document.Info.Version = "v1";

                var schemeProvider = context.ApplicationServices.GetService<IAuthenticationSchemeProvider>();
                if (schemeProvider is null)
                {
                    return;
                }

                var authenticationSchemes = await schemeProvider.GetAllSchemesAsync();

                if (authenticationSchemes.All(scheme => scheme.Name != "Bearer"))
                {
                    return;
                }

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                        In = ParameterLocation.Header,
                        Description = "JWT emitido pelo realm conexao-solidaria."
                    }
                };
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var requiresAuthorization = context.Description.ActionDescriptor.EndpointMetadata
                    .OfType<IAuthorizeData>()
                    .Any();

                if (!requiresAuthorization || context.Document is null)
                {
                    return Task.CompletedTask;
                }

                operation.Security ??= [];
                operation.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = []
                });

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
