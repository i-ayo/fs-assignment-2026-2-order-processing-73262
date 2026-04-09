using Microsoft.EntityFrameworkCore;

namespace SportsStore.Models {

    public class EFOrderRepository : IOrderRepository {
        private StoreDbContext context;

        public EFOrderRepository(StoreDbContext ctx) {
            context = ctx;
        }

        public IQueryable<Order> Orders => context.Orders
                            .Include(o => o.Lines)
                            .ThenInclude(l => l.Product);

        public void SaveOrder(Order order) {
            context.AttachRange(order.Lines.Select(l => l.Product));
            if (order.OrderID == 0) {
                context.Orders.Add(order);
            }
            context.SaveChanges();
        }

        public void UpdatePaymentStatus(int orderId, string status, string? paymentIntentId) {
            var order = context.Orders.Find(orderId);
            if (order != null) {
                order.PaymentStatus = status;
                order.PaymentIntentId = paymentIntentId;
                context.SaveChanges();
            }
        }
    }
}
