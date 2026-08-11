// FHIRGateway — Network I/O portal caps
// Auto-bound to Dafny stub: network-io
// DO NOT invent new structure. This file only inlays function behind pre-cut portals.

using System.Net.Http;
using System.Threading.Tasks;

namespace FHIRGateway
{
    public static partial class NetworkIO
    {
        private static readonly HttpClient _client = new();

        // Portal: HttpGet(url) returns (response: string)
        public static string HttpGet(string url)
        {
            return _client.GetStringAsync(url).GetAwaiter().GetResult();
        }

        // Portal: HttpPost(url, body) returns (response: string)
        public static string HttpPost(string url, string body)
        {
            var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var response = _client.PostAsync(url, content).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        // Portal: HttpPut(url, body) returns (response: string)
        public static string HttpPut(string url, string body)
        {
            var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var response = _client.PutAsync(url, content).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }

        // Portal: HttpDelete(url) returns (response: string)
        public static string HttpDelete(string url)
        {
            var response = _client.DeleteAsync(url).GetAwaiter().GetResult();
            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
    }
}