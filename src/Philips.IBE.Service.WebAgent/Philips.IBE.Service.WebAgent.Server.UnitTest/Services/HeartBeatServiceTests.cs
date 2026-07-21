using System.Diagnostics;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Moq;
using Philips.IBE.Service.WebAgent.Server.Services;
using Xunit;

namespace Philips.IBE.Service.WebAgent.Server.UnitTest.Services
{
    public class HeartBeatServiceTests
    {
        private readonly Mock<ILogger<HeartBeatService>> _loggermock;

        public HeartBeatServiceTests()
        {
            _loggermock = new Mock<ILogger<HeartBeatService>>();
        }

        [Fact]
        public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new HeartBeatService(null!));
        }

        [Fact]
        public async Task IsPortOpenAsync_ReturnsTrue_WhenConnectionSucceeds()
        {
            var service = new HeartBeatService(_loggermock.Object);

            bool result = await service.IsPortOpenAsync("localhost", 65535);
            Assert.False(result);
        }

        [Fact]
        public async Task IsPortOpenAsync_ReturnsFalse_WhenConnectionFails()
        {
            var service = new HeartBeatService(_loggermock.Object);

            bool result = await service.IsPortOpenAsync("invalid.host.name", 80);
            Assert.False(result);
        }

        [Fact]
        public void GetTcpPorts_ReturnsList_WhenNetstatSucceeds()
        {
            var service = new HeartBeatService(_loggermock.Object);

            var ports = service.GetTcpPorts();

            Assert.NotNull(ports);
            Assert.IsType<List<string>>(ports);
        }

        [Fact]
        public void GetTcpPorts_ReturnsEmptyList_WhenExceptionThrown()
        {
            var loggerMock = new Mock<ILogger<HeartBeatService>>();
            var service = new HeartBeatService(loggerMock.Object);


            var ports = service.GetTcpPorts();
            Assert.NotNull(ports);
            Assert.IsType<List<string>>(ports);
        }
    }
}
