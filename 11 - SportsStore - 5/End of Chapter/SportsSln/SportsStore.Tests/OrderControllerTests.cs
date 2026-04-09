using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SportsStore.Controllers;
using SportsStore.Models;
using SportsStore.Services;
using Xunit;

namespace SportsStore.Tests {

    public class OrderControllerTests {

        [Fact]
        public async Task Cannot_Checkout_Empty_Cart() {
            // Arrange
            Mock<IOrderRepository> mock = new Mock<IOrderRepository>();
            Mock<IPaymentService> paymentMock = new Mock<IPaymentService>();
            Cart cart = new Cart();
            Order order = new Order();
            OrderController target = new OrderController(mock.Object, cart,
                paymentMock.Object, NullLogger<OrderController>.Instance);

            // Act
            ViewResult? result = await target.Checkout(order) as ViewResult;

            // Assert - order was NOT saved (cart was empty)
            mock.Verify(m => m.SaveOrder(It.IsAny<Order>()), Times.Never);
            Assert.True(string.IsNullOrEmpty(result?.ViewName));
            Assert.False(result?.ViewData.ModelState.IsValid);
        }

        [Fact]
        public async Task Cannot_Checkout_Invalid_ShippingDetails() {
            // Arrange
            Mock<IOrderRepository> mock = new Mock<IOrderRepository>();
            Mock<IPaymentService> paymentMock = new Mock<IPaymentService>();
            Cart cart = new Cart();
            cart.AddItem(new Product(), 1);
            OrderController target = new OrderController(mock.Object, cart,
                paymentMock.Object, NullLogger<OrderController>.Instance);
            target.ModelState.AddModelError("error", "error");

            // Act
            ViewResult? result = await target.Checkout(new Order()) as ViewResult;

            // Assert - order was NOT saved (model state invalid)
            mock.Verify(m => m.SaveOrder(It.IsAny<Order>()), Times.Never);
            Assert.True(string.IsNullOrEmpty(result?.ViewName));
            Assert.False(result?.ViewData.ModelState.IsValid);
        }

        [Fact]
        public async Task Can_Checkout_And_Submit_Order() {
            // Arrange
            Mock<IOrderRepository> mock = new Mock<IOrderRepository>();
            Mock<IPaymentService> paymentMock = new Mock<IPaymentService>();
            paymentMock
                .Setup(p => p.CreateCheckoutSessionAsync(
                    It.IsAny<int>(),
                    It.IsAny<IEnumerable<CartLine>>(),
                    It.IsAny<string>(),
                    It.IsAny<string>()))
                .ReturnsAsync("https://checkout.stripe.com/test-session");

            Cart cart = new Cart();
            cart.AddItem(new Product(), 1);
            OrderController target = new OrderController(mock.Object, cart,
                paymentMock.Object, NullLogger<OrderController>.Instance);
            // Give the controller a fake HTTP context so Request.Scheme works
            target.ControllerContext = new ControllerContext {
                HttpContext = new DefaultHttpContext()
            };

            // Act - valid checkout redirects to Stripe
            var result = await target.Checkout(new Order());

            // Assert - order was saved as Pending before Stripe redirect
            mock.Verify(m => m.SaveOrder(It.IsAny<Order>()), Times.Once);
            // Assert - redirected to Stripe URL
            Assert.IsType<RedirectResult>(result);
            Assert.Contains("stripe.com", ((RedirectResult)result).Url);
        }
    }
}
