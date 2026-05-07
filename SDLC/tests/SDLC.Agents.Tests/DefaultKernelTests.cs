using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using SDLC.Contracts;
using System.Net;

namespace SDLC.Agents.Tests;

[TestFixture, SingleThreaded]
public class DefaultKernelTests
{
    private IResilientHttpClientFactory _resilientFactory = null!;
    private SdlcStage _stage = SdlcStage.Research;

    [SetUp]
    public void SetUp()
    {
        _resilientFactory = Substitute.For<IResilientHttpClientFactory>();
    }

    [Test]
    public async Task CompleteAsync_ReturnsContent_FromValidResponse()
    {
        var json = """{"choices":[{"message":{"content":"Hello world"}}]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test-model", "http://test.local/v1", MaxTokens: 1024);
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var result = await kernel.CompleteAsync("system", "user");

        result.Should().Be("Hello world");
    }

    [Test]
    public async Task CompleteAsync_Throws_WhenMissingChoices()
    {
        var json = """{"data":[]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test", "http://test.local/v1");
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var act = async () => await kernel.CompleteAsync("system", "user");

        await act.Should().ThrowAsync<KernelException>()
            .WithMessage("vLLM response missing choices/message/content");
    }

    [Test]
    public async Task CompleteAsync_Throws_WhenMalformedJson()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", System.Text.Encoding.UTF8, "application/json")
        };

        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test", "http://test.local/v1");
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var act = async () => await kernel.CompleteAsync("system", "user");

        await act.Should().ThrowAsync<KernelException>()
            .WithMessage("Malformed vLLM response");
    }

    [Test]
    public async Task CompleteAsync_UsesEndpointMaxTokens_WhenSet()
    {
        var json = """{"choices":[{"message":{"content":"OK"}}]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test", "http://test.local/v1", MaxTokens: 2048);
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        await kernel.CompleteAsync("system", "user");
    }

    [Test]
    public async Task CompleteAsync_Throws_ForTooManyRequests()
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            ReasonPhrase = "Rate limited"
        };

        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test", "http://test.local/v1");
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var act = async () => await kernel.CompleteAsync("system", "user");

        await act.Should().ThrowAsync<KernelException>()
            .WithMessage("*rate limited*");
    }

    [Test]
    public async Task CompleteAsync_ReturnsNullString_WhenContentIsEmpty()
    {
        var json = """{"choices":[{"message":{"content":""}}]}""";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        _resilientFactory.CreateForStage(_stage).Returns(CreateClient(response));

        var endpoint = new ModelEndpoint("test", "http://test.local/v1");
        var kernel = new DefaultKernel(endpoint, _resilientFactory, _stage);

        var result = await kernel.CompleteAsync("system", "user");

        result.Should().Be("");
    }

    private HttpClient CreateClient(HttpResponseMessage responseMessage)
    {
        var handler = new TestResponseMessageHandler(responseMessage);
        return new HttpClient(handler) { BaseAddress = new Uri("http://test.local/v1") };
    }

    private class TestResponseMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
