using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SchedulerApi.Tests
{
    public class SchedulerApiNetworkIOTests
    {
        [Fact]
        public void HttpGet_ValidUrl_ReturnsResponseBody()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"ok\"}")
                };
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                var result = SchedulerApi.NetworkIO.HttpGet("http://example.com/jobs");
                Assert.Equal("{\"status\":\"ok\"}", result);
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        [Fact]
        public void HttpGet_ServerError_ThrowsHttpRequestException()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                Assert.Throws<HttpRequestException>(() => SchedulerApi.NetworkIO.HttpGet("http://example.com/jobs"));
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        [Fact]
        public void HttpPost_ValidUrlAndBody_ReturnsResponseBody()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("application/json", request.Content.Headers.ContentType.MediaType);
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Equal("{\"name\":\"job1\"}", body);
                return new HttpResponseMessage(HttpStatusCode.Accepted)
                {
                    Content = new StringContent("{\"id\":\"123\"}")
                };
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                var result = SchedulerApi.NetworkIO.HttpPost("http://example.com/jobs", "{\"name\":\"job1\"}");
                Assert.Equal("{\"id\":\"123\"}", result);
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        [Fact]
        public void HttpPost_ServerError_ReturnsErrorBody()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("{\"error\":\"invalid\"}")
                };
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                var result = SchedulerApi.NetworkIO.HttpPost("http://example.com/jobs", "{\"name\":\"job1\"}");
                Assert.Equal("{\"error\":\"invalid\"}", result);
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        [Fact]
        public void HttpPut_ValidUrlAndBody_ReturnsResponseBody()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                Assert.Equal(HttpMethod.Put, request.Method);
                Assert.Equal("application/json", request.Content.Headers.ContentType.MediaType);
                var body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Assert.Equal("{\"status\":\"running\"}", body);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"status\":\"running\"}")
                };
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                var result = SchedulerApi.NetworkIO.HttpPut("http://example.com/jobs/123", "{\"status\":\"running\"}");
                Assert.Equal("{\"status\":\"running\"}", result);
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        [Fact]
        public void HttpDelete_ValidUrl_ReturnsResponseBody()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"deleted\":true}")
                };
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                var result = SchedulerApi.NetworkIO.HttpDelete("http://example.com/jobs/123");
                Assert.Equal("{\"deleted\":true}", result);
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        [Fact]
        public void HttpDelete_ServerError_ReturnsErrorBody()
        {
            using var handler = new MockHttpMessageHandler((request) =>
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("{\"error\":\"not found\"}")
                };
            });
            using var client = new HttpClient(handler);
            var originalClient = GetPrivateClient();
            SetPrivateClient(client);
            try
            {
                var result = SchedulerApi.NetworkIO.HttpDelete("http://example.com/jobs/123");
                Assert.Equal("{\"error\":\"not found\"}", result);
            }
            finally
            {
                SetPrivateClient(originalClient);
            }
        }

        private static HttpClient GetPrivateClient()
        {
            var field = typeof(SchedulerApi.NetworkIO).GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (HttpClient)field.GetValue(null);
        }

        private static void SetPrivateClient(HttpClient client)
        {
            var field = typeof(SchedulerApi.NetworkIO).GetField("_client", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            field.SetValue(null, client);
        }
    }

    public class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public MockHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}