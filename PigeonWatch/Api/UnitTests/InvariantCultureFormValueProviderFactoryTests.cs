using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using PigeonWatch.WebApi.ModelBinding;

namespace PigeonWatch.UnitTests;

public class InvariantCultureFormValueProviderFactoryTests
{
    private readonly InvariantCultureFormValueProviderFactory factory = new();

    [Fact]
    public async Task Form_values_are_read_with_the_invariant_culture_while_the_thread_culture_is_polish()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");

        try
        {
            ValueProviderFactoryContext context = Context("application/x-www-form-urlencoded", "latitude=52.2297");

            await factory.CreateValueProviderAsync(context);

            IValueProvider provider = Assert.Single(context.ValueProviders);
            ValueProviderResult latitude = provider.GetValue("latitude");
            Assert.Equal(CultureInfo.InvariantCulture, latitude.Culture);
            Assert.Equal(52.2297, double.Parse(latitude.FirstValue!, latitude.Culture));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task Request_without_form_content_adds_no_value_provider()
    {
        ValueProviderFactoryContext context = Context("application/json", "{}");

        await factory.CreateValueProviderAsync(context);

        Assert.Empty(context.ValueProviders);
    }

    [Fact]
    public async Task Malformed_multipart_body_is_reported_as_a_value_provider_exception_keeping_the_cause()
    {
        ValueProviderFactoryContext context = Context("multipart/form-data; boundary=unit-boundary", "not multipart");

        ValueProviderException exception = await Assert.ThrowsAsync<ValueProviderException>(() => factory.CreateValueProviderAsync(context));

        Assert.NotNull(exception.InnerException);
    }

    private static ValueProviderFactoryContext Context(string contentType, string body)
    {
        DefaultHttpContext httpContext = new();
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        httpContext.Request.ContentType = contentType;
        httpContext.Request.ContentLength = bytes.Length;
        httpContext.Request.Body = new MemoryStream(bytes);
        ActionContext actionContext = new(httpContext, new RouteData(), new ActionDescriptor());

        return new ValueProviderFactoryContext(actionContext);
    }
}
