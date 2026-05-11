using System.Diagnostics.CodeAnalysis;
using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Infrastructure;
using SDLC.Notifications;

namespace SDLC.Notifications.Tests;

[TestFixture, SingleThreaded]
public class SlackNotificationServiceTests
{
    [NotNull]
    private FakeHttpHandler _httpHandler = null!;

    [NotNull]
    private IHttpClientFactory _httpClientFactory = null!;

    private readonly DashboardUrlBuilder _urlBuilder = new("http://localhost:1234");

    [SetUp]
    public void SetUp()
    {
        _httpHandler = new FakeHttpHandler();
        var httpClient = new HttpClient(_httpHandler) { BaseAddress = new Uri("https://hooks.slack.com/services/test") };
        _httpClientFactory = new FakeHttpClientFactory(httpClient);
    }

    [TearDown]
    public void TearDown()
    {
        _httpHandler.Dispose();
    }

    [Test]
    public async Task SendApprovalRequestAsync_PostsToWebhook()
    {
        var service = new SlackNotificationService(_httpClientFactory!, _urlBuilder);
        var gate = new StageGate
        {
            RunId = Guid.NewGuid(),
            Stage = SdlcStage.Requirements,
            Status = GateStatus.Pending,
            Notes = "Review the spec"
        };

        await service.SendApprovalRequestAsync(gate);

        _httpHandler.ReceivedCount.Should().Be(1);
    }

    [Test]
    public async Task SendApprovalRequestAsync_PostsJsonContentType()
    {
        var service = new SlackNotificationService(_httpClientFactory, _urlBuilder);
        var gate = new StageGate { RunId = Guid.NewGuid(), Stage = SdlcStage.Design, Status = GateStatus.Pending };

        await service.SendApprovalRequestAsync(gate);

        _httpHandler.ReceivedRequest()?.Content?.Headers?.ContentType?.MediaType.Should().Be("application/json");
    }

    [Test]
    public async Task SendApprovalRequestAsync_IncludesGateIdInPayload()
    {
        var service = new SlackNotificationService(_httpClientFactory, _urlBuilder);
        var expectedGateId = Guid.NewGuid();
        var gate = new StageGate { GateId = expectedGateId, RunId = Guid.NewGuid(), Stage = SdlcStage.Research, Status = GateStatus.Pending };

        await service.SendApprovalRequestAsync(gate);

        var payload = _httpHandler.ReceivedRequest()?.Content?.ReadAsStringAsync().Result;
        payload.Should().Contain(expectedGateId.ToString());
    }

    [Test]
    public async Task SendApprovalRequestAsync_IncludesStageInPayload()
    {
        var service = new SlackNotificationService(_httpClientFactory, _urlBuilder);
        var stage = SdlcStage.Build;
        var gate = new StageGate { RunId = Guid.NewGuid(), Stage = stage, Status = GateStatus.Pending };

        await service.SendApprovalRequestAsync(gate);

        var payload = _httpHandler.ReceivedRequest()?.Content?.ReadAsStringAsync().Result;
        payload.Should().Contain($"{stage}*");
    }

    // We removed notes from the payloads for now
    // [Test]
    // public async Task SendApprovalRequestAsync_IncludesNotesInPayload()
    // {
    //     var service = new SlackNotificationService(_httpClientFactory, _urlBuilder);
    //     var notes = "Needs more detail on error handling";
    //     var gate = new StageGate { RunId = Guid.NewGuid(), Stage = SdlcStage.Requirements, Status = GateStatus.Pending, Notes = notes };
    //
    //     await service.SendApprovalRequestAsync(gate);
    //
    //     var payload = _httpHandler.ReceivedRequest()?.Content?.ReadAsStringAsync().Result;
    //     payload.Should().Contain(notes);
    // }

    [Test]
    public async Task SendApprovalRequestAsync_ReturnsSuccessfully_WhenServerReturns200()
    {
        var service = new SlackNotificationService(_httpClientFactory, _urlBuilder);
        var gate = new StageGate { RunId = Guid.NewGuid(), Stage = SdlcStage.Research, Status = GateStatus.Pending };

        var act = () => service.SendApprovalRequestAsync(gate);

        await act.Should().NotThrowAsync();
    }

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public FakeHttpClientFactory(HttpClient httpClient) => _httpClient = httpClient;

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private class FakeHttpHandler : HttpMessageHandler
    {
        private HttpRequestMessage? _request;
        private readonly TaskCompletionSource<bool> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int ReceivedCount { get; private set; }

        public HttpRequestMessage? ReceivedRequest() => _request;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _request = request;
            ReceivedCount++;
            _received.SetResult(true);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }

        public Task WaitForReceivedAsync() => _received.Task;
    }
}
