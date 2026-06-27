using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZendeskApi.Client.Exceptions;

namespace ZendeskApi.Client.Converters
{
    /// <summary>
    /// Zendesk returns errors in two shapes:
    ///   { "error": "SomeCode", "description": "..." }          (error is a string)
    ///   { "error": { "title": "Forbidden", "message": "..." } } (error is an object)
    /// This converter reads both into <see cref="ErrorResponse"/> so that deserialization
    /// never throws when the API returns the object form.
    /// </summary>
    public class ErrorResponseConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ErrorResponse);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            var token = JToken.Load(reader);

            if (token.Type != JTokenType.Object)
            {
                return null;
            }

            var root = (JObject)token;
            var result = new ErrorResponse();

            var errorToken = root["error"];

            if (errorToken is JObject errorObject)
            {
                // Object form: pull title/message out of the nested object.
                result.Error = errorObject.Value<string>("title");
                result.Description = errorObject.Value<string>("message")
                                     ?? root.Value<string>("description");
                result.Details = errorObject["details"] as JObject
                                 ?? root["details"] as JObject;
            }
            else
            {
                // String form (or absent): keep the original behaviour.
                result.Error = errorToken?.Type == JTokenType.String
                    ? errorToken.Value<string>()
                    : errorToken?.ToString();
                result.Description = root.Value<string>("description");
                result.Details = root["details"] as JObject;
            }

            return result;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotSupportedException($"{nameof(ErrorResponseConverter)} is only used for deserialization.");
        }

        public override bool CanWrite => false;
    }
}
