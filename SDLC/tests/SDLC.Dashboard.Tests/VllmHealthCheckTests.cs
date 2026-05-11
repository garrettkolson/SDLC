using System.Net;
using FluentAssertions;
using NUnit.Framework;
using SDLC.Contracts;
using SDLC.Dashboard.Services;

namespace SDLC.Dashboard.Tests;

[TestFixture]
public class VllmHealthCheckTests
{
    [Test]
    public async Task CheckAsync_AllEndpointsUp_DeduplicatesByBaseUrl()
    {
        var routing = CreateDefaultRouting();
        var handler = new TestHandler(HttpStatusCode.OK);
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var check = new VllmHealthCheck(routing, http);

        var (healthy, message) = await check.CheckAsync();

        healthy.Should().BeTrue();
        handler.Requests.Should().Be(2); // 2 unique baseUrls: Local27B and LocalMoE
    }

    [Test]
    public async Task CheckAsync_OneEndpointDown_ReturnsFailures()
    {
        var routing = CreateDefaultRouting();
        routing.StageEndpoints[SdlcStage.Learn] = new ModelEndpoint("broken", "http://broken:8000/v1", MaxTokens: 4096);

        var handler = new TestHandler();
        handler.ForUrl("http://broken:8000/v1/v1/models", HttpStatusCode.BadGateway);
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        var check = new VllmHealthCheck(routing, http);

        var (healthy, message) = await check.CheckAsync();

        healthy.Should().BeFalse();
        message.Should().Contain("broken");
        message.Should().Contain("BadGateway");
    }

    private static ModelRoutingConfig CreateDefaultRouting()
    {
        return new ModelRoutingConfig
        {
            StageEndpoints = new Dictionary<SdlcStage, ModelEndpoint>
            {
                [SdlcStage.Research]   = ModelEndpoint.Local27B,
                [SdlcStage.Requirements] = ModelEndpoint.Local27B,
                [SdlcStage.Design]     = ModelEndpoint.Local27B,
                [SdlcStage.Build]      = ModelEndpoint.Local27B,
                [SdlcStage.Learn]      = ModelEndpoint.LocalMoE,
            }
        };
    }

    private class TestHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, HttpStatusCode> _responses = new();
        private readonly HttpStatusCode _defaultStatus;

        public TestHandler(HttpStatusCode defaultStatus = HttpStatusCode.OK)
        {
            _defaultStatus = defaultStatus;
        }

        public int Requests { get; private set; }

        public void ForUrl(string url, HttpStatusCode status)
        {
            _responses[url] = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var url = request.RequestUri!.ToString();
            var status = _responses.GetValueOrDefault(url, _defaultStatus);
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
