using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace PigeonWatch.WebApi.ModelBinding;

public class InvariantCultureFormValueProviderFactory : IValueProviderFactory
{
    public Task CreateValueProviderAsync(ValueProviderFactoryContext context)
    {
        if (!context.ActionContext.HttpContext.Request.HasFormContentType)
            return Task.CompletedTask;

        return AddValueProviderAsync(context, context.ActionContext.HttpContext.RequestAborted);
    }

    private static async Task AddValueProviderAsync(ValueProviderFactoryContext context, CancellationToken cancellationToken = default)
    {
        HttpRequest request = context.ActionContext.HttpContext.Request;
        IFormCollection form;

        try
        {
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException exception)
        {
            throw new ValueProviderException($"Failed to read the request form. {exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new ValueProviderException($"Failed to read the request form. {exception.Message}", exception);
        }

        context.ValueProviders.Add(new FormValueProvider(BindingSource.Form, form, CultureInfo.InvariantCulture));
    }
}
