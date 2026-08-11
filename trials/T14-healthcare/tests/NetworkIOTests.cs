using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using WebApi;

namespace WebApi.Tests
{
    public class NetworkIOTests
    {
        [Fact]
        public void HttpGet_ValidUrl_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("response body")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpGet("http://localhost/test");

            // Assert
            Assert.Equal("response body", result);
        }

        [Fact]
        public void HttpGet_EmptyUrl_ThrowsException()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                throw new HttpRequestException("Invalid URL");
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpGet(""));
        }

        [Fact]
        public void HttpGet_NullUrl_ThrowsException()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                throw new HttpRequestException("Invalid URL");
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpGet(null));
        }

        [Fact]
        public void HttpGet_ServerError_ReturnsErrorResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("error")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpGet("http://localhost/error");

            // Assert
            Assert.Equal("error", result);
        }

        [Fact]
        public void HttpPost_ValidUrlAndBody_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.Created)
                {
                    Content = new StringContent("created")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpPost("http://localhost/create", "{\"name\":\"test\"}");

            // Assert
            Assert.Equal("created", result);
        }

        [Fact]
        public void HttpPost_EmptyBody_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpPost("http://localhost/create", "");

            // Assert
            Assert.Equal("ok", result);
        }

        [Fact]
        public void HttpPost_NullBody_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpPost("http://localhost/create", null);

            // Assert
            Assert.Equal("ok", result);
        }

        [Fact]
        public void HttpPost_InvalidUrl_ThrowsException()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                throw new HttpRequestException("Invalid URL");
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpPost("invalid-url", "body"));
        }

        [Fact]
        public void HttpPut_ValidUrlAndBody_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("updated")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpPut("http://localhost/update", "{\"name\":\"test\"}");

            // Assert
            Assert.Equal("updated", result);
        }

        [Fact]
        public void HttpPut_EmptyBody_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpPut("http://localhost/update", "");

            // Assert
            Assert.Equal("ok", result);
        }

        [Fact]
        public void HttpPut_NullBody_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpPut("http://localhost/update", null);

            // Assert
            Assert.Equal("ok", result);
        }

        [Fact]
        public void HttpPut_InvalidUrl_ThrowsException()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                throw new HttpRequestException("Invalid URL");
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpPut("invalid-url", "body"));
        }

        [Fact]
        public void HttpDelete_ValidUrl_ReturnsResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("deleted")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpDelete("http://localhost/delete");

            // Assert
            Assert.Equal("deleted", result);
        }

        [Fact]
        public void HttpDelete_EmptyUrl_ThrowsException()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                throw new HttpRequestException("Invalid URL");
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpDelete(""));
        }

        [Fact]
        public void HttpDelete_NullUrl_ThrowsException()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                throw new HttpRequestException("Invalid URL");
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpDelete(null));
        }

        [Fact]
        public void HttpDelete_ServerError_ReturnsErrorResponse()
        {
            // Arrange
            var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("error")
                };
            });
            var client = new HttpClient(handler);
            NetworkIO.SetClient(client);

            // Act
            var result = NetworkIO.HttpDelete("http://localhost/error");

            // Assert
            Assert.Equal("error", result);
        }
    }

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}