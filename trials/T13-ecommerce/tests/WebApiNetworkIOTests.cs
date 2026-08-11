using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using WebApi;

namespace WebApi.Tests
{
    public class WebApiNetworkIOTests
    {
        // Helper to create a mock HttpMessageHandler that returns a fixed response.
        private static HttpClient CreateMockClient(HttpStatusCode statusCode, string content)
        {
            var handler = new MockHttpMessageHandler(statusCode, content);
            return new HttpClient(handler);
        }

        [Fact]
        public void HttpGet_ValidUrl_ReturnsResponseBody()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "{\"result\":\"ok\"}");
            // Inject the mock client into the static NetworkIO class via reflection.
            SetHttpClient(mockClient);

            // Act
            var result = NetworkIO.HttpGet("http://example.com/api");

            // Assert
            Assert.Equal("{\"result\":\"ok\"}", result);
        }

        [Fact]
        public void HttpGet_EmptyUrl_ThrowsException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<UriFormatException>(() => NetworkIO.HttpGet(""));
        }

        [Fact]
        public void HttpGet_NullUrl_ThrowsArgumentNullException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => NetworkIO.HttpGet(null));
        }

        [Fact]
        public void HttpGet_ServerError_ThrowsHttpRequestException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.InternalServerError, "error");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpGet("http://example.com/error"));
        }

        [Fact]
        public void HttpPost_ValidUrlAndBody_ReturnsResponseBody()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "{\"id\":123}");
            SetHttpClient(mockClient);

            // Act
            var result = NetworkIO.HttpPost("http://example.com/api", "{\"name\":\"test\"}");

            // Assert
            Assert.Equal("{\"id\":123}", result);
        }

        [Fact]
        public void HttpPost_EmptyBody_ReturnsResponseBody()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "{\"id\":1}");
            SetHttpClient(mockClient);

            // Act
            var result = NetworkIO.HttpPost("http://example.com/api", "");

            // Assert
            Assert.Equal("{\"id\":1}", result);
        }

        [Fact]
        public void HttpPost_NullBody_ThrowsArgumentNullException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => NetworkIO.HttpPost("http://example.com/api", null));
        }

        [Fact]
        public void HttpPost_ServerError_ThrowsHttpRequestException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.BadRequest, "bad request");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpPost("http://example.com/api", "{}"));
        }

        [Fact]
        public void HttpPut_ValidUrlAndBody_ReturnsResponseBody()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "{\"updated\":true}");
            SetHttpClient(mockClient);

            // Act
            var result = NetworkIO.HttpPut("http://example.com/api/1", "{\"name\":\"new\"}");

            // Assert
            Assert.Equal("{\"updated\":true}", result);
        }

        [Fact]
        public void HttpPut_EmptyBody_ReturnsResponseBody()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "{\"updated\":true}");
            SetHttpClient(mockClient);

            // Act
            var result = NetworkIO.HttpPut("http://example.com/api/1", "");

            // Assert
            Assert.Equal("{\"updated\":true}", result);
        }

        [Fact]
        public void HttpPut_NullBody_ThrowsArgumentNullException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => NetworkIO.HttpPut("http://example.com/api/1", null));
        }

        [Fact]
        public void HttpPut_ServerError_ThrowsHttpRequestException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.NotFound, "not found");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpPut("http://example.com/api/1", "{}"));
        }

        [Fact]
        public void HttpDelete_ValidUrl_ReturnsResponseBody()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "{\"deleted\":true}");
            SetHttpClient(mockClient);

            // Act
            var result = NetworkIO.HttpDelete("http://example.com/api/1");

            // Assert
            Assert.Equal("{\"deleted\":true}", result);
        }

        [Fact]
        public void HttpDelete_EmptyUrl_ThrowsUriFormatException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<UriFormatException>(() => NetworkIO.HttpDelete(""));
        }

        [Fact]
        public void HttpDelete_NullUrl_ThrowsArgumentNullException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.OK, "");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => NetworkIO.HttpDelete(null));
        }

        [Fact]
        public void HttpDelete_ServerError_ThrowsHttpRequestException()
        {
            // Arrange
            var mockClient = CreateMockClient(HttpStatusCode.InternalServerError, "error");
            SetHttpClient(mockClient);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpDelete("http://example.com/api/1"));
        }

        // Helper to set the private static HttpClient field in NetworkIO.
        private static void SetHttpClient(HttpClient client)
        {
            var field = typeof(NetworkIO).GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field == null)
            {
                throw new InvalidOperationException("Could not find _client field.");
            }
            field.SetValue(null, client);
        }

        // Mock HttpMessageHandler that returns a fixed response.
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _statusCode;
            private readonly string _content;

            public MockHttpMessageHandler(HttpStatusCode statusCode, string content)
            {
                _statusCode = statusCode;
                _content = content;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(_statusCode)
                {
                    Content = new StringContent(_content)
                };
                return Task.FromResult(response);
            }
        }
    }
}