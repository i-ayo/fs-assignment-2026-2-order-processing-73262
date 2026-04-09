namespace SportsStore.Services {

    public interface IPaymentService {

        /// <summary>
        /// Creates a Stripe Checkout Session for the given order and cart lines.
        /// Returns the hosted Stripe URL to redirect the user to.
        /// </summary>
        Task<string> CreateCheckoutSessionAsync(
            int orderId,
            IEnumerable<SportsStore.Models.CartLine> lines,
            string successUrl,
            string cancelUrl);
    }
}
