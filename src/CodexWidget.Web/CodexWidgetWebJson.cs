using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexWidget.Web;

public static class CodexWidgetWebJson
{
    public static IServiceCollection AddCodexWidgetWebJson(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.ConfigureHttpJsonOptions(ConfigureHttpJsonOptions);
        return services;
    }

    public static void ConfigureHttpJsonOptions(Microsoft.AspNetCore.Http.Json.JsonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ConfigureSerializerOptions(options.SerializerOptions);
    }

    public static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ConfigureSerializerOptions(options);
        return options;
    }

    public static void ConfigureSerializerOptions(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;

        for (var index = options.Converters.Count - 1; index >= 0; index--)
        {
            if (options.Converters[index] is JsonStringEnumConverter)
            {
                options.Converters.RemoveAt(index);
            }
        }

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }
}
