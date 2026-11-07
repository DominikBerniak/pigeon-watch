using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PigeonWatch.BusinessObjects;
using PigeonWatch.WebApi.Mappers;
using PigeonWatch.WebApi.ModelBinding;
using PigeonWatch.WebApi.ViewModelCreators;

namespace PigeonWatch.DependencyInjection;

public static class WebApiServiceCollectionExtensions
{
    public static IServiceCollection AddWebApi(this IServiceCollection services)
    {
        services.AddScoped<IRegisterRequestMapper, RegisterRequestMapper>();
        services.AddScoped<IRegisteredAccountViewModelCreator, RegisteredAccountViewModelCreator>();
        services.AddScoped<IUpdatedProfileViewModelCreator, UpdatedProfileViewModelCreator>();
        services.AddScoped<IPasswordChangedViewModelCreator, PasswordChangedViewModelCreator>();
        services.AddScoped<IClientConfigurationViewModelCreator, ClientConfigurationViewModelCreator>();
        services.AddScoped<IGeneralConfigurationViewModelCreator, GeneralConfigurationViewModelCreator>();
        services.AddScoped<IUiResourcesViewModelCreator, UiResourcesViewModelCreator>();
        services.AddScoped<ICreateSightingRequestMapper, CreateSightingRequestMapper>();
        services.AddScoped<INearbySightingsRequestMapper, NearbySightingsRequestMapper>();
        services.AddScoped<ISightingListRequestMapper, SightingListRequestMapper>();
        services.AddScoped<ISightingViewModelCreator, SightingViewModelCreator>();
        services.AddScoped<ISightingCommentViewModelCreator, SightingCommentViewModelCreator>();

        services.PostConfigure<MvcOptions>(options =>
        {
            for (int index = 0; index < options.ValueProviderFactories.Count; index++)
            {
                if (options.ValueProviderFactories[index] is FormValueProviderFactory)
                    options.ValueProviderFactories[index] = new InvariantCultureFormValueProviderFactory();
            }
        });

        services.PostConfigure<ApiBehaviorOptions>(options =>
        {
            Func<ActionContext, IActionResult> defaultFactory = options.InvalidModelStateResponseFactory;
            options.InvalidModelStateResponseFactory = context =>
            {
                bool bodyTooLarge = context.HttpContext.Request.HasFormContentType
                    && context.HttpContext.Request.ContentLength > SightingRules.PhotoRequestMaxBytes
                    && context.ModelState.TryGetValue(string.Empty, out ModelStateEntry? formEntry)
                    && formEntry.Errors.Count > 0;

                if (!bodyTooLarge)
                    return defaultFactory(context);

                string category = (context.ActionDescriptor as ControllerActionDescriptor)?.ControllerTypeInfo.FullName ?? "PigeonWatch.WebApi";
                string route = (context.HttpContext.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
                string formError = context.ModelState[string.Empty]?.Errors[0].ErrorMessage ?? "unknown";
                context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(category)
                    .LogWarning("Request rejected with {ErrorCode} on {Route}: {FormError}", SightingErrorCodes.PhotoTooLarge, route, formError);

                ModelStateDictionary modelState = new();
                modelState.AddModelError(SightingErrorCodes.PhotoTooLarge, $"The request must not exceed {SightingRules.PhotoRequestMaxBytes} bytes and the photo {SightingRules.PhotoMaxBytes} bytes.");
                ValidationProblemDetails problem = context.HttpContext.RequestServices
                    .GetRequiredService<ProblemDetailsFactory>()
                    .CreateValidationProblemDetails(context.HttpContext, modelState, StatusCodes.Status400BadRequest);

                return new BadRequestObjectResult(problem)
                {
                    ContentTypes = { "application/problem+json", "application/problem+xml" }
                };
            };
        });

        return services;
    }
}
