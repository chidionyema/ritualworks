using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Xunit;

namespace Haworks.Tests
{
    /// <summary>
    /// Demonstrates extensive logging of:
    /// 1) The register call's request & response (and set-cookies),
    /// 2) The shared CookieContainer contents after registration,
    /// 3) The protected endpoint request & response,
    /// 4) The CookieContainer again afterward.
    /// </summary>
    [Collection("Integration Tests")]
    public class CookieDebugTest
    {
        private readonly IntegrationTestFixture _fixture;
        private readonly HttpClient _client;

        public CookieDebugTest(IntegrationTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.CreateClientWithCookies(); // uses the shared CookieContainer

            // Ensure BaseAddress is set to match your test server
              _client.BaseAddress = _fixture.Factory.Server.BaseAddress;
        }

        [Fact]
        public async Task Register_And_ShowCookies_WithExtraLogging()
        {
            // -----------------------
            // 1) REGISTER THE USER
            // -----------------------
            var registerDto = new
            {
                Username = "testuser_cookie_debug",
                Email = "cookie_debug@example.com",
                Password = "Test1234!"
            };

            // LOG the outgoing request data:
            Console.WriteLine("[DEBUG] Sending POST /api/authentication/register");
            Console.WriteLine("        Request body (JSON):");
            Console.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(registerDto, Newtonsoft.Json.Formatting.Indented));

            // Make the request:
            var regResponse = await _client.PostAsJsonAsync("/api/authentication/register", registerDto);

            // LOG the response status/headers/body:
            Console.WriteLine($"[DEBUG] /register response status code: {regResponse.StatusCode}");
            Console.WriteLine("[DEBUG] /register response headers:");
            foreach (var header in regResponse.Headers)
            {
                Console.WriteLine($"    {header.Key}: {string.Join(",", header.Value)}");
            }
            foreach (var header in regResponse.Content.Headers)
            {
                Console.WriteLine($"    {header.Key}: {string.Join(",", header.Value)}");
            }

            var regBody = await regResponse.Content.ReadAsStringAsync();
            Console.WriteLine("[DEBUG] /register response body:");
            Console.WriteLine(regBody);

            // Will throw if not 2xx
            regResponse.EnsureSuccessStatusCode();

            // -----------------------
            // 2) DUMP COOKIES NOW
            // -----------------------
            Console.WriteLine("[DEBUG] Cookies *after* registration:");
            DumpAllCookies();

            // -----------------------
            // 3) CALL PROTECTED ENDPOINT
            // -----------------------
            Console.WriteLine("\n[DEBUG] Sending GET /api/v1/content/getcontent (requires 'jwt' cookie)...");
            var protectedResp = await _client.GetAsync("/api/v1/content/getcontent");

            Console.WriteLine($"[DEBUG] Protected endpoint status: {protectedResp.StatusCode}");
            Console.WriteLine("[DEBUG] Protected endpoint response headers:");
            foreach (var header in protectedResp.Headers)
            {
                Console.WriteLine($"    {header.Key}: {string.Join(",", header.Value)}");
            }
            foreach (var header in protectedResp.Content.Headers)
            {
                Console.WriteLine($"    {header.Key}: {string.Join(",", header.Value)}");
            }

            var protectedBody = await protectedResp.Content.ReadAsStringAsync();
            Console.WriteLine("[DEBUG] Protected endpoint response body:");
            Console.WriteLine(protectedBody);

            // If you expect a successful 200 from the protected endpoint:
            protectedResp.EnsureSuccessStatusCode();

            // -----------------------
            // 4) DUMP COOKIES AGAIN
            // -----------------------
            Console.WriteLine("[DEBUG] Cookies *after* protected endpoint call:");
            DumpAllCookies();
        }

        /// <summary>
        /// Utility method to read all cookies for the current HttpClient.BaseAddress
        /// from the shared CookieContainer, then print them to console output.
        /// </summary>
        private void DumpAllCookies()
        {
            var baseUri = _client.BaseAddress!;
            CookieCollection cookies = _fixture.CookieContainer.GetCookies(baseUri);

            Console.WriteLine($"[DEBUG] Found {cookies.Count} cookies for {baseUri}:");
            foreach (Cookie cookie in cookies)
            {
                Console.WriteLine(
                    $"    • {cookie.Name}={cookie.Value}\n" +
                    $"      Domain={cookie.Domain}, Path={cookie.Path}, " +
                    $"Secure={cookie.Secure}, HttpOnly={cookie.HttpOnly}");
            }
        }
    }
}
