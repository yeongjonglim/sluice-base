using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SluiceBase.Api.Extensions;

public static class OpenApiOptionsExtensions
{
    public static OpenApiOptions AddStringEnumSchemaTransformer(this OpenApiOptions options)
    {
        options.AddSchemaTransformer((schema, context, ct) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (!type.IsEnum)
            {
                return Task.CompletedTask;
            }

            schema.Type = JsonSchemaType.String;
            schema.Format = null;
            schema.Enum =
            [
                .. type
                    .GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(field =>
                    {
                        var customName = field
                            .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()
                            ?.Name;

                        return JsonValue.Create(customName ?? field.Name);
                    })
            ];

            return Task.CompletedTask;
        });

        return options;
    }
}