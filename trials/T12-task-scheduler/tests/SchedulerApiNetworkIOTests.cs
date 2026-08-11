using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using SchedulerApi;

namespace SchedulerApi.Tests
{
    public class SchedulerApiNetworkIOTests
    {
        [Fact]
        public void HttpGet_ValidUrl_ReturnsResponse()
        {
            // Arrange
            var url = "https://example.com";

            // Act
            var result = NetworkIO.HttpGet(url);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void HttpPost_ValidUrlAndBody_ReturnsResponse()
        {
            // Arrange
            var url = "https://example.com";
            var body = "{\"key\":\"value\"}";

            // Act
            var result = NetworkIO.HttpPost(url, body);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void HttpPut_ValidUrlAndBody_ReturnsResponse()
        {
            // Arrange
            var url = "https://example.com";
            var body = "{\"key\":\"value\"}";

            // Act
            var result = NetworkIO.HttpPut(url, body);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void HttpDelete_ValidUrl_ReturnsResponse()
        {
            // Arrange
            var url = "https://example.com";

            // Act
            var result = NetworkIO.HttpDelete(url);

            // Assert
            Assert.NotNull(result);
        }

        [Fact]
        public void HttpGet_EmptyUrl_ThrowsException()
        {
            // Arrange
            var url = "";

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpGet(url));
        }

        [Fact]
        public void HttpPost_EmptyUrl_ThrowsException()
        {
            // Arrange
            var url = "";
            var body = "{}";

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpPost(url, body));
        }

        [Fact]
        public void HttpPut_EmptyUrl_ThrowsException()
        {
            // Arrange
            var url = "";
            var body = "{}";

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpPut(url, body));
        }

        [Fact]
        public void HttpDelete_EmptyUrl_ThrowsException()
        {
            // Arrange
            var url = "";

            // Act & Assert
            Assert.Throws<HttpRequestException>(() => NetworkIO.HttpDelete(url));
        }

        [Fact]
        public void HttpPost_NullBody_ThrowsException()
        {
            // Arrange
            var url = "https://example.com";
            string body = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => NetworkIO.HttpPost(url, body));
        }

        [Fact]
        public void HttpPut_NullBody_ThrowsException()
        {
            // Arrange
            var url = "https://example.com";
            string body = null;

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => NetworkIO.HttpPut(url, body));
        }
    }
}