using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace PigeonWatch.IntegrationTests;

public sealed class EndpointCatalog(IServiceProvider services)
{
    public IReadOnlyList<RoutedEndpoint> GetEndpoints()
    {
        EndpointDataSource dataSource = services.GetRequiredService<EndpointDataSource>();
        List<RoutedEndpoint> endpoints = [];

        foreach (RouteEndpoint endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            string pattern = (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            bool requiresAuthorization = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0
                && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null;
            IReadOnlyList<string> methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["ANY"];

            foreach (string method in methods)
                endpoints.Add(new RoutedEndpoint(method.ToUpperInvariant(), pattern, requiresAuthorization));
        }

        return endpoints.OrderBy(endpoint => endpoint.Key, StringComparer.Ordinal).ToList();
    }
}
