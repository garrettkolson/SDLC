using System.Net;
using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Agents;
using SDLC.Contracts;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class DefaultKernelWithUsageTests
{
    private IResilientHttpClientFactory _resilientFactory = null!;
    private SdlcStage _stage = SdlcStage.Research;

    [SetUp]
    public void SetUp()
    {
        _resilientFactory = Substitute.For<IResilientHttpClientFactory>();
    }

    [Test]
    public async Task CompleteAsyncWithUsage_ReturnsContentAndUsage()
    {
        var json = "{\"choices\":[{\"message\":{\"content\":\"Hello world\"}}],\"usage\":{\"prompt_tokens\":100,\"completion_tokens\":50}}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test-model", "http://test.local/v1", MaxTokens: 1024);
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var (content, usage) = await kernel.CompleteAsyncWithUsage("system", "user");

        content.Should().Be("Hello world");
        usage.PromptTokens.Should().Be(100);
        usage.CompletionTokens.Should().Be(50);
        usage.TotalTokens.Should().Be(150);
    }

    [Test]
    public async Task CompleteAsyncWithUsage_HandlesMissingUsageField()
    {
        var json = "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var kernel = new DefaultKernel(new ModelEndpoint("test", "http://test.local/v1"), _resilientFactory, _stage);

        var (content, usage) = await kernel.CompleteAsyncWithUsage("system", "user");

        content.Should().Be("OK");
        usage.Should().Be(TokenUsage.Zero);
    }

    [Test]
    public async Task CompleteAsyncWithUsage_HandlesPartialUsage()
    {
        var json = "{\"choices\":[{\"message\":{\"content\":\"OK\"}}],\"usage\":{\"prompt_tokens\":200}}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var kernel = new DefaultKernel(new ModelEndpoint("test", "http://test.local/v1"), _resilientFactory, _stage);

        var (content, usage) = await kernel.CompleteAsyncWithUsage("system", "user");

        usage.PromptTokens.Should().Be(200);
        usage.CompletionTokens.Should().Be(0);
    }

    [Test]
    public async Task CompleteAsyncWithUsage_UsesEndpointMaxTokens()
    {
        var json = "{\"choices\":[{\"message\":{\"content\":\"OK\"}}]}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test", "http://test.local/v1", MaxTokens: 2048);
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var (content, usage) = await kernel.CompleteAsyncWithUsage("system", "user");
        content.Should().Be("OK");
    }

    [Test]
    public async Task CompleteAsyncWithUsage_Throws_WhenMissingChoices()
    {
        var json = "{\"data\":[]}";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };
        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var kernel = new DefaultKernel(new ModelEndpoint("test", "http://test.local/v1"), _resilientFactory, _stage);

        var act = async () => await kernel.CompleteAsyncWithUsage("system", "user");

        await act.Should().ThrowAsync<KernelException>()
            .WithMessage("vLLM response missing choices/message/content");
    }

    private HttpClient CreateClient(HttpResponseMessage responseMessage)
    {
        var handler = new TestResponseMessageHandler(responseMessage);
        return new HttpClient(handler) { BaseAddress = new Uri("http://test.local/v1") };
    }

    private class TestResponseMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response);
    }
}
