using System.Net;
using FluentAssertions;
using NUnit.Framework;
using SDLC.Notifications;

namespace SDLC.Notifications.Tests;

[TestFixture, SingleThreaded]
public class ResilientSlackHandlerTests
{
    [Test]
    public void Handler_Registers_WithHttpClientFactory()
    {
        var handler = new ResilientSlackHandler();
        handler.Should().NotBeNull();
        handler.Should().BeAssignableTo<DelegatingHandler>();
    }

    [Test]
    public async Task Handler_Returns200_WhenInnerHandlerSucceeds()
    {
        var inner = new SucceedingHandler();
        using var handler = new HttpClient(new ResilientSlackHandler { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://hooks.slack.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/sdlc");

        using var response = await handler.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Handler_Retries_WhenInnerHandlerReturns502()
    {
        var inner = new FailingHandler(HttpStatusCode.BadGateway);
        using var handler = new HttpClient(new ResilientSlackHandler { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://hooks.slack.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/sdlc");

        await handler.SendAsync(request);

        inner.CallCount.Should().BeGreaterThanOrEqualTo(3);
    }

    [Test]
    public async Task Handler_Retries_WhenInnerHandlerReturns503()
    {
        var inner = new FailingHandler(HttpStatusCode.ServiceUnavailable);
        using var handler = new HttpClient(new ResilientSlackHandler { InnerHandler = inner })
        {
            BaseAddress = new Uri("https://hooks.slack.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhook/sdlc");

        await handler.SendAsync(request);

        inner.CallCount.Should().BeGreaterThanOrEqualTo(3);
    }

    private class SucceedingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private class FailingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private int _callCount;

        public FailingHandler(HttpStatusCode statusCode) => _statusCode = statusCode;
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            return Task.FromResult(new HttpResponseMessage(_statusCode));
        }
    }
}
