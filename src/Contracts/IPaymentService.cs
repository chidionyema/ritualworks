using haworks.Dto;
using haworks.Db;
using Stripe;
using System.Threading.Tasks;
using haworks.Services;

namespace haworks.Contracts
{
    public interface IPaymentService
    {
        Task<CreatePaymentIntentResponse> CreatePaymentIntentAsync(CreatePaymentIntentRequest request);
        Task UpdatePaymentStatusAsync(string sessionId);
    }
}