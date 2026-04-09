using Stripe.Checkout;
using SportsStore.Models;

namespace SportsStore.Services {

    public class StripePaymentService : IPaymentService {

        private readonly ILogger<StripePaymentService> _logger;

        public StripePaymentService(IConfiguration config,
                ILogger<StripePaymentService> logger) {
            _logger = logger;
            // Set the global Stripe secret key — read from user secrets, never hardcoded
            Stripe.StripeConfiguration.ApiKey = config["Stripe:SecretKey"];
        }

        public async Task<string> CreateCheckoutSessionAsync(
                int orderId,
                IEnumerable<CartLine> lines,
                string successUrl,
                string cancelUrl) {

            var lineItems = lines.Select(l => new SessionLineItemOptions {
                PriceData = new SessionLineItemPriceDataOptions {
                    Currency = "eur",
                    ProductData = new SessionLineItemPriceDataProductDataOptions {
                        Name = l.Product.Name ?? "Product",
                        Description = $"ProductID: {l.Product.ProductID}"
                    },
                    UnitAmount = (long)(l.Product.Price * 100) // Stripe uses cents
                },
                Quantity = l.Quantity
            }).ToList();

            var options = new SessionCreateOptions {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = lineItems,
                Mode = "payment",
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = new Dictionary<string, string> {
                    { "orderId", orderId.ToString() }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation(
                "Stripe Checkout Session created. SessionId: {SessionId}, " +
                "OrderId: {OrderId}, AmountTotal: {AmountTotal}, Currency: {Currency}",
                session.Id, orderId, session.AmountTotal, "eur");

            return session.Url;
        }
    }
}
