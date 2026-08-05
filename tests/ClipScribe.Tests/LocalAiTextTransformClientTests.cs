using System.Net;
using System.Net.Http;
using System.Text;
using ClipScribe.Core.Models;
using ClipScribe.Core.Services;

namespace ClipScribe.Tests;

public sealed class LocalAiTextTransformClientTests
{
    [Fact]
    public async Task TransformAsync_ReturnsTrimmedContent_AndUsesChatCompletionsEndpoint()
    {
        HttpRequestMessage? seenRequest = null;

        using var httpClient = new HttpClient(new DelegateHandler(req =>
        {
            seenRequest = req;
            const string payload = """
                {
                  "choices": [
                    { "message": { "content": "  transformed output  " } }
                  ]
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new LocalAiTextTransformClient(httpClient);

        var result = await sut.TransformAsync(
            new LocalAiSettings(Enabled: true, Endpoint: "http://localhost:11434", Model: "llama3.2:3b"),
            "Summarize",
            "A long input body");

        Assert.Equal("transformed output", result);
        Assert.NotNull(seenRequest);
        Assert.Equal(HttpMethod.Post, seenRequest!.Method);
        Assert.Equal("http://localhost:11434/v1/chat/completions", seenRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task TransformAsync_HonorsExistingV1Path()
    {
        HttpRequestMessage? seenRequest = null;

        using var httpClient = new HttpClient(new DelegateHandler(req =>
        {
            seenRequest = req;
            const string payload = """
                {
                  "choices": [
                    { "message": { "content": "done" } }
                  ]
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new LocalAiTextTransformClient(httpClient);

        _ = await sut.TransformAsync(
            new LocalAiSettings(Enabled: true, Endpoint: "http://localhost:11434/v1", Model: "llama3.2:3b"),
            "Fix grammar",
            "some text");

        Assert.NotNull(seenRequest);
        Assert.Equal("http://localhost:11434/v1/chat/completions", seenRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task TransformAsync_Throws_WhenDisabledOrUnconfigured()
    {
        var sut = new LocalAiTextTransformClient(new HttpClient(new DelegateHandler(_ =>
            throw new InvalidOperationException("No request should be made."))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.TransformAsync(LocalAiSettings.Default, "Summarize", "input"));
    }

    [Fact]
    public async Task TransformAsync_Throws_WhenResponseMissingContent()
    {
        using var httpClient = new HttpClient(new DelegateHandler(_ =>
        {
            const string payload = """
                {
                  "choices": []
                }
                """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
        }));

        var sut = new LocalAiTextTransformClient(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.TransformAsync(
                new LocalAiSettings(Enabled: true, Endpoint: "http://localhost:11434", Model: "llama3.2:3b"),
                "Summarize",
                "input"));
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
