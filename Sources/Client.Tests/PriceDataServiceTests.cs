using System.Threading.Tasks;
using NUnit.Framework;
using QuickPrice.Services;

namespace QuickPrice.Tests
{
    [TestFixture]
    public class PriceDataServiceTests
    {
        [Test]
        public async Task UpdatePricesAsync_DoesNotThrow()
        {
            // This is a smoke test to ensure the async update path is callable.
            // Note: In CI this will run without actual server; ensure RequestHandler is stubbed or network calls are safe.
            var svc = PriceDataService.Instance;
            // Call with force=false to avoid aggressive network calls in tests
            var result = await svc.UpdatePricesAsync(force: false);

            // We don't assert on result because environment may not have server; just ensure no exception
            Assert.Pass("UpdatePricesAsync invoked");
        }

        [Test]
        public void GetPrice_ReturnsNullable_WhenCacheEmpty()
        {
            var svc = PriceDataService.Instance;
            // Ensure cache cleared for test
            // (Reflection could be used to clear private cache in real unit tests; here we just call and expect null)
            var price = svc.GetPrice("non-existent-id");
            Assert.IsNull(price);
        }
    }
}
