using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;

namespace SDLC.Agents.Tests;

[TestFixture]
public class SweAfClientTests
{
    [Test]
    public void Constructor_NullHttp_ThrowsArgumentNullException()
    {
        var act = () => new SweAfClient(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public async Task SubmitAsync_Success_ReturnsRunId()
    {
        var json = """{"runId":"run-abc-123"}""";
        var http = new HttpClient(new FakeHttpHandler(jsonResponse: json));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var task = new SweAfTask { Spec = "Test spec", Architecture = "Test arch" };
        var runId = await client.SubmitAsync(task);

        runId.Should().Be("run-abc-123");
    }

    [Test]
    public async Task SubmitAsync_404_ThrowsHttpRequestException()
    {
        var http = new HttpClient(new FakeHttpHandler(statusCode: System.Net.HttpStatusCode.NotFound));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var task = new SweAfTask { Spec = "Test", Architecture = "Arch" };
        var act = () => client.SubmitAsync(task);
        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public async Task SubmitAsync_EmptyBody_ThrowsInvalidOperationException()
    {
        var http = new HttpClient(new FakeHttpHandler(statusCode: System.Net.HttpStatusCode.OK));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var task = new SweAfTask { Spec = "Test", Architecture = "Arch" };
        var act = () => client.SubmitAsync(task);
        act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task SubmitAsync_PostsCorrectJson()
    {
        var json = """{"runId":"run-posted"}""";
        string? capturedBody = null;
        var http = new HttpClient(new CapturingHttpHandler(jsonResponse: json, onSend: (req) => capturedBody = req.Content?.ReadAsStringAsync().Result));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var task = new SweAfTask { Spec = "My spec text", Architecture = "Arch description" };
        await client.SubmitAsync(task);

        capturedBody.Should().NotBeNull();
        capturedBody.Should().Contain("\"spec\"");
        capturedBody.Should().Contain("My spec text");
        capturedBody.Should().Contain("Arch description");
    }

    [Test]
    public async Task PollAsync_YieldsStatuses_ThenBreaksOnTerminal()
    {
        // Enum values: Running=0, Succeeded=1
        var statuses = new[]
        {
            """{"state":0,"isTerminal":false}""",
            """{"state":1,"isTerminal":true,"logs":"all done"}""",
        };
        var http = new HttpClient(new SequentialFakeHttpHandler(statuses, delayMs: 10));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var results = new List<SweAfStatus>();
        await foreach (var s in client.PollAsync("r1"))
            results.Add(s);

        results.Should().HaveCount(2);
        results[0].State.Should().Be(SweAfState.Running);
        results[1].State.Should().Be(SweAfState.Succeeded);
        results[1].Logs.Should().Be("all done");
    }

    [Test]
    public async Task PollAsync_YieldsSingleItem_WhenAlreadyTerminal()
    {
        var statuses = new[]
        {
            """{"state":1,"isTerminal":true,"logs":"done"}""",
        };
        var http = new HttpClient(new SequentialFakeHttpHandler(statuses, delayMs: 10));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var results = new List<SweAfStatus>();
        await foreach (var s in client.PollAsync("r2"))
            results.Add(s);

        results.Should().HaveCount(1);
        results[0].State.Should().Be(SweAfState.Succeeded);
        results[0].IsTerminal.Should().BeTrue();
    }

    [Test]
    public async Task PollAsync_404_ThrowsOnFirstError()
    {
        var http = new HttpClient(new FakeHttpHandler(statusCode: System.Net.HttpStatusCode.NotFound));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var act = async () =>
        {
            await foreach (var _ in client.PollAsync("r-fail"))
            { }
        };
        act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public async Task PollAsync_EmptyBody_Throws()
    {
        var http = new HttpClient(new FakeHttpHandler(statusCode: System.Net.HttpStatusCode.OK));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var act = async () =>
        {
            await foreach (var _ in client.PollAsync("r-fail"))
            { }
        };
        act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task PollAsync_Cancellation_StopsPolling()
    {
        var alwaysRunning = """{"state":0,"isTerminal":false}""";
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var http = new HttpClient(new SequentialFakeHttpHandler(new[] { alwaysRunning, alwaysRunning }, delayMs: 10));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var results = new List<SweAfStatus>();
        await foreach (var s in client.PollAsync("r3", cts.Token))
            results.Add(s);

        results.Should().BeEmpty();
    }

    [Test]
    public async Task PollAsync_YieldsMultipleNonTerminal()
    {
        var statuses = new[]
        {
            """{"state":0,"isTerminal":false}""",
            """{"state":0,"isTerminal":false}""",
            """{"state":1,"isTerminal":true,"logs":"ok"}""",
        };
        var http = new HttpClient(new SequentialFakeHttpHandler(statuses, delayMs: 10));
        http.BaseAddress = new Uri("http://test.local/");
        var client = new SweAfClient(http);

        var results = new List<SweAfStatus>();
        await foreach (var s in client.PollAsync("r4"))
            results.Add(s);

        results.Should().HaveCount(3);
        results[0].State.Should().Be(SweAfState.Running);
        results[2].IsTerminal.Should().BeTrue();
        results[2].Logs.Should().Be("ok");
    }

    private class FakeHttpHandler : HttpMessageHandler
    {
        private readonly string? jsonResponse;
        private readonly System.Net.HttpStatusCode statusCode;

        public FakeHttpHandler(string? jsonResponse = null, System.Net.HttpStatusCode statusCode = System.Net.HttpStatusCode.OK)
        {
            this.jsonResponse = jsonResponse;
            this.statusCode = statusCode;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var resp = new HttpResponseMessage(statusCode);
            if (jsonResponse != null)
                resp.Content = new StringContent(jsonResponse);
            return Task.FromResult(resp);
        }
    }

    private class CapturingHttpHandler : HttpMessageHandler
    {
        private readonly string jsonResponse;
        private readonly Action<HttpRequestMessage>? onSend;

        public CapturingHttpHandler(string jsonResponse, Action<HttpRequestMessage>? onSend = null)
        {
            this.jsonResponse = jsonResponse;
            this.onSend = onSend;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend?.Invoke(request);
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Content = new StringContent(jsonResponse);
            return Task.FromResult(resp);
        }
    }

    private class SequentialFakeHttpHandler : HttpMessageHandler
    {
        private readonly string[] _responses;
        private readonly int _delayMs;
        private int _index;

        public SequentialFakeHttpHandler(string[] responses, int delayMs = 10)
        {
            _responses = responses;
            _delayMs = delayMs;
            _index = 0;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_index >= _responses.Length)
                return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);

            await Task.Delay(_delayMs, cancellationToken);
            var json = _responses[_index++];
            var resp = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            resp.Content = new StringContent(json);
            return resp;
        }
    }
}
