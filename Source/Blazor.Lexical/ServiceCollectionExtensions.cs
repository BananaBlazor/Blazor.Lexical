using Microsoft.Extensions.DependencyInjection;

namespace Blazor.Lexical;

/// <summary>
/// Registration helpers for Blazor.Lexical.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Blazor.Lexical services and app-wide defaults.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional callback to configure <see cref="LexicalOptions"/>.</param>
    public static IServiceCollection AddLexicalBlazor(
        this IServiceCollection services,
        Action<LexicalOptions>? configure = null)
    {
        var options = new LexicalOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services;
    }
}
