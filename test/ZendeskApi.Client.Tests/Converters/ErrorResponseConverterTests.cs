using Newtonsoft.Json;
using Xunit;
using ZendeskApi.Client.Exceptions;

namespace ZendeskApi.Client.Tests.Converters
{
    public class ErrorResponseConverterTests
    {
        [Fact]
        public void Should_Deserialize_When_Error_Is_A_String()
        {
            var json = "{\"error\":\"RecordNotFound\",\"description\":\"Not found\"}";

            var error = JsonConvert.DeserializeObject<ErrorResponse>(json);

            Assert.Equal("RecordNotFound", error.Error);
            Assert.Equal("Not found", error.Description);
        }

        [Fact]
        public void Should_Deserialize_When_Error_Is_An_Object()
        {
            var json = "{\"error\":{\"title\":\"Forbidden\",\"message\":\"You do not have access to this page.\"}}";

            var error = JsonConvert.DeserializeObject<ErrorResponse>(json);

            Assert.Equal("Forbidden", error.Error);
            Assert.Equal("You do not have access to this page.", error.Description);
        }

        [Fact]
        public void Should_Deserialize_Details_When_Error_Is_A_String()
        {
            var json = "{\"error\":\"RecordInvalid\",\"description\":\"Validation failed\",\"details\":{\"base\":[{\"description\":\"oops\"}]}}";

            var error = JsonConvert.DeserializeObject<ErrorResponse>(json);

            Assert.Equal("RecordInvalid", error.Error);
            Assert.NotNull(error.Details);
            Assert.NotNull(error.Details["base"]);
        }
    }
}
