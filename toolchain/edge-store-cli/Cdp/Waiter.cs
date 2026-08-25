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
        var started = DateTime.UtcNow;
        var deadline = DateTime.UtcNow.Add(actualTimeout);
        var nextProgress = started.AddSeconds(5);
        Exception? firstException = null;

        Ops.Wait(description);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await predicate())
                {
                    Ops.WaitOk(description);
                    return true;
                }
            }
            catch (Exception ex)
            {
                if (firstException == null)
                {
                    firstException = ex;
                    Ops.Publish("WAIT-ERR", $"{description}: {ex.Message}");
                }
            }

            if (DateTime.UtcNow >= nextProgress)
            {
                Ops.Publish("WAIT", $"{description}: {(DateTime.UtcNow - started).TotalSeconds:0}s/{actualTimeout.TotalSeconds:0}s");
                nextProgress = DateTime.UtcNow.AddSeconds(5);
            }

            await Task.Delay(actualInterval);
        }

        Ops.Publish("WAIT-TIMEOUT", $"{description} did not become true within {actualTimeout.TotalSeconds:0}s.");
        return false;
    }

    public async Task RequireAsync(Func<Task<bool>> predicate, TimeSpan timeout, string description)
    {
        if (!await WaitUntilAsync(predicate, timeout, description: description))
            throw new TimeoutException($"{description} did not become true within {timeout.TotalSeconds:0}s.");
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

    public async Task NavigateAsync(string url, string label = "", bool allowOverviewRedirect = false)
    {
        Ops.Nav(url + (label.Length > 0 ? "  (" + label + ")" : ""));
        await _client.SendAsync("Page.navigate", new { url });
        string pathOnly = url.Split('?')[0];
        await RequireAsync(async () =>
        {
            string current = await _client.EvaluateAsync<string>("location.href") ?? "";
            return current.Contains(pathOnly, StringComparison.OrdinalIgnoreCase) ||
                   (allowOverviewRedirect && current.Contains("/overview", StringComparison.OrdinalIgnoreCase));
        }, TimeSpan.FromSeconds(45), $"Navigate to {label} ({pathOnly})");
        await Task.Delay(500); // Give JS microtasks a brief breath
    }

    public async Task ReloadAsync(bool ignoreCache = true)
    {
        // F5 (Page.reload) leaves the Partner Center SPA as a blank shell. Use a
        // cold navigation to the current URL instead, which re-bootstraps the route
        // and forces a fresh server read (real persistence proof).
        string current = await _client.EvaluateAsync<string>("location.href") ?? "";
        Ops.Publish("NAV", "cold reload -> " + current);
        await _client.SendAsync("Page.navigate", new { url = current });
        await Task.Delay(1500);
    }
}
