using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using haworks.Db;
using Microsoft.EntityFrameworkCore.Storage;

namespace haworks.Contracts
{
    public interface IPaymentRepository
    {
        Task<Payment?> GetPaymentByIdAsync(Guid paymentId);
        Task<Payment?> GetPaymentByOrderIdAsync(Guid orderId);
        Task<Payment> GetPaymentByStripeSessionIdAsync(string sessionId);
        Task CreatePaymentAsync(Payment payment);
        Task UpdatePaymentAsync(Payment payment);
        Task<IDbContextTransaction> BeginTransactionAsync();
    }
}
