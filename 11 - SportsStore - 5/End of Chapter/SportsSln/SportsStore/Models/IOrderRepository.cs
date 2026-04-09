namespace SportsStore.Models {

    public interface IOrderRepository {

        IQueryable<Order> Orders { get; }
        void SaveOrder(Order order);
        void UpdatePaymentStatus(int orderId, string status, string? paymentIntentId);
    }
}
