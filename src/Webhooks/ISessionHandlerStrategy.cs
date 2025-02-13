using System.Threading.Tasks;
using Stripe.Checkout;

namespace haworks.Webhooks
{
    public interface ISessionHandlerStrategy
    {
        Task<bool> HandleSession(Session session);
    }
}
