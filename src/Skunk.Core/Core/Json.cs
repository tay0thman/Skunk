using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skunk.Core
{
    /// <summary>JSON used for document strings and object user text (fields, enums as names).</summary>
    public static class Json
    {
        public static readonly JsonSerializerOptions Options = Create();

        static JsonSerializerOptions Create()
        {
            var o = new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = false,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            };
            o.Converters.Add(new JsonStringEnumConverter());
            return o;
        }

        /// <summary>Serialize with the runtime type (a SpotData passed as ElementData keeps all its fields).</summary>
        public static string Write<T>(T value) =>
            value == null ? "null" : JsonSerializer.Serialize(value, value.GetType(), Options);

        public static T Read<T>(string text) where T : class
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            try
            {
                return JsonSerializer.Deserialize<T>(text, Options);
            }
            catch (Exception ex)
            {
                Log.Debug("JSON read failed for " + typeof(T).Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>Deep copy through JSON.</summary>
        public static T Clone<T>(T value) where T : class => value == null ? null : Read<T>(Write(value));
    }
}
