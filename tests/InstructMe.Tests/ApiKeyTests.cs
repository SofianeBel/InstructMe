using System.Net;
using System.Net.Http;
using System.Text;
using Anthropic.Models.Messages;
using InstructMe.Definitions;

namespace InstructMe.Tests;

[CollectionDefinition("API environment", DisableParallelization = true)]
public class ApiEnvironmentCollection : ICollectionFixture<ApiEnvironment>;

public sealed class ApiEnvironment : IDisposable
{
    public ApiEnvironment()
    {
        // Only change this isolated test process, using dummy credentials.
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "stale-placeholder");
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", "unrelated-placeholder");
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", "https://proxy.example.invalid");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null);
        Environment.SetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN", null);
        Environment.SetEnvironmentVariable("ANTHROPIC_BASE_URL", null);
    }
}

[Collection("API environment")]
public class ApiKeyTests
{
    [Theory]
    [InlineData("entered-placeholder")]
    [InlineData(" \tentered-placeholder\r\n")]
    public void Entered_key_takes_priority_over_an_existing_environment_key(string entered)
    {
        var settings = new AppSettings { AnthropicApiKey = entered };
        Assert.Equal("entered-placeholder", settings.ResolveApiKey());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    public void Empty_entered_key_falls_back_to_the_environment(string? entered)
    {
        var settings = new AppSettings { AnthropicApiKey = entered };
        Assert.Equal("stale-placeholder", settings.ResolveApiKey());
    }

    [Fact]
    public async Task Requests_use_the_selected_key_without_inherited_bearer_auth_or_proxy()
    {
        var settings = new AppSettings { AnthropicApiKey = "entered-placeholder" };
        using var http = new HttpClient(new InspectRequest());
        using var client = DefinitionService.CreateClient(settings.ResolveApiKey()!, http);
        await client.Messages.Create(new MessageCreateParams
        {
            Model = settings.Model,
            MaxTokens = 1,
            Messages = [new() { Role = Role.User, Content = "Hi" }],
        });
    }

    private sealed class InspectRequest : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("entered-placeholder", Assert.Single(request.Headers.GetValues("X-Api-Key")));
            Assert.Null(request.Headers.Authorization);
            Assert.Equal(new Uri("https://api.anthropic.com/v1/messages"), request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"id":"msg_test","type":"message","role":"assistant","model":"claude-haiku-4-5",
                     "content":[{"type":"text","text":"Hi"}],"stop_reason":"max_tokens","stop_sequence":null,
                     "usage":{"input_tokens":1,"output_tokens":1}}
                    """, Encoding.UTF8, "application/json"),
            });
        }
    }
}
