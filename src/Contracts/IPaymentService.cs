using System.Collections.Generic;
using System.Threading.Tasks;
using RitualWorks.Controllers;

namespace RitualWorks.Contracts
{
    public interface IPaymentService
    {
        Task<string> CreateCheckoutSessionAsync(string userId, List<CheckoutItem> items);
    }
}
