using Microsoft.Extensions.AI;
using Subconscious.Engine.Tools;
using System.ComponentModel;

namespace Subconscious.Tools.Desktop;

/// <summary>
/// Web operations tool module. Provides web-related capabilities like HTTP requests.
/// Port of Python's <c>desktop_tools/web.py</c>.
/// </summary>
public sealed class WebToolModule : IToolModule
{
    public string Slug => "web";

    public IReadOnlyList<AIFunction> CreateTools(EngineContext context)
    {
        return
        [
            AIFunctionFactory.Create(
                FetchUrl,
                "fetch_url",
                "Fetch content from a URL. Returns the response body."),

            AIFunctionFactory.Create(
                DownloadFile,
                "download_file",
                "Download a file from a URL to a specified path."),

            AIFunctionFactory.Create(
                GetUrlInfo,
                "get_url_info",
                "Get metadata about a URL including status code and headers.")
        ];
    }

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static async Task<string> FetchUrl(
        [Description("The URL to fetch.")] string url,
        EngineContext context)
    {
        try
        {
            var response = await HttpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return content.Length > 100_000
                ? $"Content length: {content.Length} chars (truncated)\n\n{content[..100_000]}"
                : content;
        }
        catch (HttpRequestException ex)
        {
            return $"Error fetching '{url}': {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return $"Error: Request to '{url}' timed out";
        }
    }

    private static async Task<string> DownloadFile(
        [Description("The URL of the file to download.")] string url,
        [Description("The local path to save the file.")] string path,
        EngineContext context)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsByteArrayAsync();
            File.WriteAllBytes(path, content);

            return $"Successfully downloaded {content.Length} bytes to '{path}'";
        }
        catch (HttpRequestException ex)
        {
            return $"Error downloading '{url}': {ex.Message}";
        }
        catch (IOException ex)
        {
            return $"Error saving to '{path}': {ex.Message}";
        }
    }

    private static async Task<string> GetUrlInfo(
        [Description("The URL to check.")] string url,
        EngineContext context)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Head, url);
            var response = await HttpClient.SendAsync(request);

            return $"""
                URL: {url}
                Status Code: {(int)response.StatusCode} {response.StatusCode}
                Content-Type: {response.Content.Headers.ContentType?.ToString() ?? "Unknown"}
                Content-Length: {response.Content.Headers.ContentLength ?? 0} bytes
                """;
        }
        catch (HttpRequestException ex)
        {
            return $"Error checking '{url}': {ex.Message}";
        }
    }
}
