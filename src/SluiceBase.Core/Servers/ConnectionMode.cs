using System.Text.Json.Serialization;

namespace SluiceBase.Core.Servers;

// How to reach a MongoDB deployment. Standard = a single host:port (or the default
// scheme). Srv = a mongodb+srv DNS seedlist name (Atlas / managed clusters); the port
// is not used in this mode.
//
// The [JsonConverter] pins string serialization on the type itself, so the enum
// round-trips as "Standard"/"Srv" regardless of the JsonSerializerOptions in play
// (the API's global converter, or a consumer deserializing a response with defaults).
[JsonConverter(typeof(JsonStringEnumConverter<ConnectionMode>))]
public enum ConnectionMode
{
    Standard,
    Srv
}
