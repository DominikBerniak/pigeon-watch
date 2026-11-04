using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;

namespace PigeonWatch.IntegrationTests;

public sealed class RequestBodySizeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is null)
                    context.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestMaxRequestBodySizeFeature());

                return nextMiddleware(context);
            });

            next(app);
        };
    }
}
