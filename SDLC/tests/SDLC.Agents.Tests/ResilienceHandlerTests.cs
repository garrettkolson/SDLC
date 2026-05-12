using FluentAssertions;
using NUnit.Framework;
using Polly;
using Polly.Extensions.Http;

namespace SDLC.Agents.Tests;

[TestFixture]
public class ResilienceHandlerTests
{
    [Test]
    public async Task SendAsync_Http502_RetriesThenReturnsLastResponse()
    {
        var handler = new FakeCountingHandler(
            responses: new[]
            {
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
            },
            maxCalls: 4);

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(1));

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
        var inner = new ResilienceHandler(retryPolicy, timeoutPolicy, handler);

        var client = new HttpClient(inner);
        client.Timeout = TimeSpan.FromSeconds(30);

        var response = await client.GetAsync("http://test.local/");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadGateway);
        handler.CallCount.Should().Be(4);
    }

    [Test]
    public async Task SendAsync_Http429_Retries()
    {
        var handler = new FakeCountingHandler(
            responses: new[]
            {
                new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests),
                new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests),
                new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests),
            },
            maxCalls: 4);

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(1));

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
        var inner = new ResilienceHandler(retryPolicy, timeoutPolicy, handler);

        var client = new HttpClient(inner);
        client.Timeout = TimeSpan.FromSeconds(30);

        var response = await client.GetAsync("http://test.local/");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.TooManyRequests);
        handler.CallCount.Should().Be(4);
    }

    [Test]
    public async Task SendAsync_200OnFirstTry_DoesNotRetry()
    {
        var handler = new FakeCountingHandler(
            responses: new[]
            {
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") },
            },
            maxCalls: 1);

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(3, i => TimeSpan.FromMilliseconds(1));

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
        var inner = new ResilienceHandler(retryPolicy, timeoutPolicy, handler);

        var client = new HttpClient(inner);
        client.Timeout = TimeSpan.FromSeconds(30);

        var response = await client.GetAsync("http://test.local/");
        response.IsSuccessStatusCode.Should().BeTrue();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        handler.CallCount.Should().Be(1);
    }

    [Test]
    public async Task SendAsync_200OnFifthTry_RetriesUntilSuccess()
    {
        var handler = new FakeCountingHandler(
            responses: new[]
            {
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
                new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway),
                new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("ok") },
            },
            maxCalls: 5);

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(4, i => TimeSpan.FromMilliseconds(1));

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
        var inner = new ResilienceHandler(retryPolicy, timeoutPolicy, handler);

        var client = new HttpClient(inner);
        client.Timeout = TimeSpan.FromSeconds(30);

        var response = await client.GetAsync("http://test.local/");
        response.IsSuccessStatusCode.Should().BeTrue();
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        handler.CallCount.Should().Be(5);
    }

    [Test]
    public async Task SendAsync_ExceedsTimeout_Throws()
    {
        var handler = new FakeDelayHandler(delay: TimeSpan.FromMilliseconds(200));

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(2, i => TimeSpan.FromMilliseconds(1));

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromMilliseconds(100));
        var inner = new ResilienceHandler(retryPolicy, timeoutPolicy, handler);

        var client = new HttpClient(inner);
        client.Timeout = TimeSpan.FromSeconds(30);

        Exception? caught = null;
        try { await client.GetAsync("http://test.local/"); }
        catch (Exception ex) { caught = ex; }
        caught.Should().NotBeNull();
        handler.CallCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task SendAsync_HttpException_RetriesUntilExhausted()
    {
        var handler = new FakeErrorHandler { Retries = true };

        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(2, i => TimeSpan.FromMilliseconds(1));

        var timeoutPolicy = Policy.TimeoutAsync<HttpResponseMessage>(TimeSpan.FromSeconds(30));
        var inner = new ResilienceHandler(retryPolicy, timeoutPolicy, handler);

        var client = new HttpClient(inner);
        client.Timeout = TimeSpan.FromSeconds(30);

        HttpRequestException? caught = null;
        try { await client.GetAsync("http://test.local/"); }
        catch (HttpRequestException ex) { caught = ex; }
        caught.Should().NotBeNull();
        handler.CallCount.Should().Be(3);
    }

    private class FakeCountingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage[] _responses;
        private readonly int _maxCalls;
        private int _callCount;

        public int CallCount => _callCount;

        public FakeCountingHandler(HttpResponseMessage[] responses, int maxCalls)
        {
            _responses = responses;
            _maxCalls = maxCalls;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            if (_callCount > _maxCalls)
                throw new InvalidOperationException("Fake handler exceeded max calls");
            return _responses[Math.Min(_callCount - 1, _responses.Length - 1)];
        }
    }

    private class FakeDelayHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;
        private int _callCount;

        public int CallCount => _callCount;

        public FakeDelayHandler(TimeSpan delay)
        {
            _delay = delay;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            await Task.Delay(_delay, cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway);
        }
    }

    private class FakeErrorHandler : HttpMessageHandler
    {
        private int _callCount;
        public bool Retries;

        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _callCount++;
            if (Retries)
                throw new HttpRequestException("Fake handler error");
            throw new InvalidOperationException("Fake handler error");
        }
    }
}
