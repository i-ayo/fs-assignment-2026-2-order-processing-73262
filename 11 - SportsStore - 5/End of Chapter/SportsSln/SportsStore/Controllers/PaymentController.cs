using Microsoft.AspNetCore.Mvc;
using SportsStore.Models;
using Stripe.Checkout;

namespace SportsStore.Controllers {

    public class PaymentController : Controller {
        private readonly IOrderRepository repository;
        private readonly Cart cart;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(IOrderRepository repoService, Cart cartService,
                ILogger<PaymentController> logger) {
            repository = repoService;
            cart = cartService;
            _logger = logger;
        }

        /// <summary>
        /// Stripe redirects here after a SUCCESSFUL payment.
        /// We verify with Stripe, update the order, clear the cart, and show confirmation.
        /// </summary>
        public async Task<IActionResult> Success(int orderId, string session_id) {
            _logger.LogInformation(
                "Payment Success callback received. OrderId: {OrderId}, SessionId: {SessionId}",
                orderId, session_id);

            try {
                var service = new SessionService();
                var session = await service.GetAsync(session_id);

                if (session.PaymentStatus == "paid") {
                    // Confirm with Stripe — payment actually went through
                    repository.UpdatePaymentStatus(orderId, "Succeeded", session.PaymentIntentId);
                    cart.Clear();

                    _logger.LogInformation(
                        "Payment SUCCEEDED for Order {OrderId}. " +
                        "PaymentIntentId: {PaymentIntentId}, " +
                        "AmountTotal: {AmountTotal}, " +
                        "CustomerEmail: {CustomerEmail}, " +
                        "StripeSessionId: {SessionId}",
                        orderId,
                        session.PaymentIntentId,
                        session.AmountTotal,
                        session.CustomerDetails?.Email ?? "not provided",
                        session_id);

                    return RedirectToPage("/Completed", new { orderId });
                } else {
                    // Session exists but payment not confirmed — unexpected state
                    repository.UpdatePaymentStatus(orderId, "Failed", null);

                    _logger.LogWarning(
                        "Payment NOT confirmed for Order {OrderId}. " +
                        "StripeSessionId: {SessionId}, PaymentStatus: {PaymentStatus}",
                        orderId, session_id, session.PaymentStatus);

                    return View("Failed", orderId);
                }
            } catch (Exception ex) {
                _logger.LogError(ex,
                    "Exception while verifying Stripe payment for Order {OrderId}, " +
                    "SessionId: {SessionId}",
                    orderId, session_id);

                repository.UpdatePaymentStatus(orderId, "Failed", null);
                return View("Failed", orderId);
            }
        }

        /// <summary>
        /// Stripe redirects here when the user clicks 'Back' or cancels on Stripe's page.
        /// This also covers declined cards (user cancels after seeing decline error).
        /// </summary>
        public IActionResult Cancel(int orderId) {
            repository.UpdatePaymentStatus(orderId, "Cancelled", null);

            _logger.LogWarning(
                "Payment CANCELLED for Order {OrderId}. " +
                "Cart still intact — user can retry.",
                orderId);

            return View("Cancel", orderId);
        }
    }
}
