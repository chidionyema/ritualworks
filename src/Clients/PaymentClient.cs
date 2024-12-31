
using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using haworks.Controllers;
using haworks.Dto;

public interface IPaymentClient
{
    Task<CreatePaymentIntentResponse> CreatePaymentIntentAsync(CreatePaymentIntentRequest request);
    Task<PaymentIntentStatusResponse?> GetPaymentIntentAsync(Guid orderId);
}

public class PaymentClient : IPaymentClient
{
    private readonly HttpClient _httpClient;

    public PaymentClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CreatePaymentIntentResponse> CreatePaymentIntentAsync(CreatePaymentIntentRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/payment/create-intent", request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"Payment API error: {response.StatusCode} - {errorContent}");
        }

        var result = await response.Content.ReadFromJsonAsync<CreatePaymentIntentResponse>();
        if (result == null)
        {
            throw new Exception("Invalid response from Payment API");
        }

        return result;
    }

    public async Task<PaymentIntentStatusResponse?> GetPaymentIntentAsync(Guid orderId)
    {
        var response = await _httpClient.GetAsync($"/api/payment/{orderId}/status");

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<PaymentIntentStatusResponse>();
    }
}

public class PaymentIntentStatusResponse
{
    public bool IsComplete { get; set; }
    public string Status { get; set; }
    public decimal Amount { get; set; }
    public decimal Tax { get; set; }
}
public class CreatePaymentIntentResponse
{
    public string ClientSecret { get; set; }
}
