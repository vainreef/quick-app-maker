namespace Vainreef.EdgeStore.Cdp;

public class Waiter
{
    private readonly CdpClient _client;

    public Waiter(CdpClient client)
    {
        _client = client;
    }

    public async Task<bool> WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan? timeout = null, TimeSpan? interval = null, string description = "")
    {
        var actualTimeout = timeout ?? TimeSpan.FromSeconds(45);
        var actualInterval = interval ?? TimeSpan.FromMilliseconds(300);
        var deadline = DateTime.UtcNow.Add(actualTimeout);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await predicate())
                {
                    return true;
                }
            }
            catch { }

            await Task.Delay(actualInterval);
        }

        return false;
    }

    public async Task WaitForUrlAsync(string pattern, TimeSpan? timeout = null)
    {
        bool ok = await WaitUntilAsync(async () =>
        {
            var url = await _client.EvaluateAsync<string>("location.href");
            return url?.Contains(pattern, StringComparison.OrdinalIgnoreCase) == true;
        }, timeout, description: $"Wait for URL containing '{pattern}'");

        if (!ok)
        {
            throw new TimeoutException($"Timed out waiting for URL pattern: {pattern}");
        }
    }

    public async Task WaitForTextAsync(string text, TimeSpan? timeout = null)
    {
        bool ok = await WaitUntilAsync(async () =>
        {
            var body = await _client.EvaluateAsync<string>("document.body ? document.body.innerText : ''");
            return body?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
        }, timeout, description: $"Wait for text containing '{text}'");

        if (!ok)
        {
            throw new TimeoutException($"Timed out waiting for page text: {text}");
        }
    }

    public async Task NavigateAsync(string url, string label = "")
    {
        await _client.SendAsync("Page.navigate", new { url });
        string pathOnly = url.Split('?')[0];
        await WaitForUrlAsync(pathOnly);
        await Task.Delay(500); // Give JS microtasks a brief breath
    }

    public async Task ReloadAsync(bool ignoreCache = true)
    {
        await _client.SendAsync("Page.reload", new { ignoreCache });
        await Task.Delay(800);
    }
}
