using Microsoft.AspNetCore.Mvc;
using SportsStore.Models;
using SportsStore.Services;

namespace SportsStore.Controllers {

    public class OrderController : Controller {
        private readonly IOrderRepository repository;
        private readonly Cart cart;
        private readonly IPaymentService paymentService;
        private readonly ILogger<OrderController> _logger;

        public OrderController(IOrderRepository repoService, Cart cartService,
                IPaymentService paymentSvc, ILogger<OrderController> logger) {
            repository = repoService;
            cart = cartService;
            paymentService = paymentSvc;
            _logger = logger;
        }

        public ViewResult Checkout() {
            _logger.LogInformation(
                "Checkout page visited. Cart has {LineCount} line(s), total {CartTotal:C}",
                cart.Lines.Count(),
                cart.ComputeTotalValue());
            return View(new Order());
        }

        [HttpPost]
        public async Task<IActionResult> Checkout(Order order) {
            if (cart.Lines.Count() == 0) {
                _logger.LogWarning(
                    "Checkout attempted with empty cart by customer {Name}",
                    order.Name ?? "unknown");
                ModelState.AddModelError("", "Sorry, your cart is empty!");
            }

            if (ModelState.IsValid) {
                // 1. Save order to DB immediately with Pending status
                order.Lines = cart.Lines.ToArray();
                order.PaymentStatus = "Pending";
                repository.SaveOrder(order);

                var productIds = order.Lines.Select(l => l.Product.ProductID).ToArray();
                _logger.LogInformation(
                    "Order {OrderId} saved as Pending. Customer: {CustomerName}, " +
                    "City: {City}, Country: {Country}, Lines: {LineCount}, " +
                    "Total: {CartTotal:C}, ProductIds: {@ProductIds}",
                    order.OrderID, order.Name,
                    order.City, order.Country,
                    order.Lines.Count,
                    cart.ComputeTotalValue(),
                    productIds);

                // 2. Build Stripe return URLs
                var baseUrl = $"{Request.Scheme}://{Request.Host}";
                var successUrl = $"{baseUrl}/Payment/Success?orderId={order.OrderID}&session_id={{CHECKOUT_SESSION_ID}}";
                var cancelUrl  = $"{baseUrl}/Payment/Cancel?orderId={order.OrderID}";

                // 3. Create Stripe Checkout Session and redirect user there
                _logger.LogInformation(
                    "Creating Stripe Checkout Session for Order {OrderId}", order.OrderID);
                try {
                    var stripeUrl = await paymentService.CreateCheckoutSessionAsync(
                        order.OrderID, cart.Lines, successUrl, cancelUrl);

                    _logger.LogInformation(
                        "Redirecting Order {OrderId} to Stripe Checkout at {StripeUrl}",
                        order.OrderID, stripeUrl);

                    return Redirect(stripeUrl);
                } catch (Exception ex) {
                    _logger.LogError(ex,
                        "Stripe session creation FAILED for Order {OrderId}. " +
                        "Message: {ErrorMessage}",
                        order.OrderID, ex.Message);
                    repository.UpdatePaymentStatus(order.OrderID, "Failed", null);
                    ModelState.AddModelError("", $"Payment service unavailable: {ex.Message}");
                    return View(order);
                }
            } else {
                _logger.LogWarning(
                    "Checkout validation failed for {Name}. Errors: {Errors}",
                    order.Name ?? "unknown",
                    ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage));
                return View();
            }
        }
    }
}
