using System.Text.Json;

namespace RainHarvest.Contracts;

/// <summary>JSON settings for MQTT payloads, shared by the backend and the simulator.</summary>
public static class MqttJson
{
    /// <summary>camelCase; missing required fields and nulls in non-nullable fields are errors.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };
}
