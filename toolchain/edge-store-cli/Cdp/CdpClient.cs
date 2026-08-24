using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Vainreef.EdgeStore.Cdp;

public class CdpClient : IAsyncDisposable
{
    private readonly ClientWebSocket _ws = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonDocument>> _pendingRequests = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoopTask;
    private int _nextId = 1;

    public bool IsConnected => _ws.State == WebSocketState.Open;

    public async Task ConnectAsync(Uri webSocketUrl, CancellationToken cancellationToken = default)
    {
        await _ws.ConnectAsync(webSocketUrl, cancellationToken);
        _receiveLoopTask = Task.Run(ReceiveLoopAsync, _cts.Token);

        // Enable essential domains
        await SendAsync("Page.enable");
        await SendAsync("Runtime.enable");
        await SendAsync("DOM.enable");
        await SendAsync("Accessibility.enable");
    }

    public async Task<JsonDocument> SendAsync(string method, object? parameters = null, TimeSpan? timeout = null)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("CDP WebSocket is not connected.");
        }

        int id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[id] = tcs;

        var requestObj = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters ?? new { }
        };

        string json = JsonSerializer.Serialize(requestObj);
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);

        var actualTimeout = timeout ?? TimeSpan.FromSeconds(30);
        using var timeoutCts = new CancellationTokenSource(actualTimeout);
        using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
        {
            try
            {
                return await tcs.Task;
            }
            catch (TaskCanceledException)
            {
                _pendingRequests.TryRemove(id, out _);
                throw new TimeoutException($"CDP method [{method}] timed out after {actualTimeout.TotalSeconds}s.");
            }
        }
    }

    public async Task<T?> EvaluateAsync<T>(string expression, TimeSpan? timeout = null)
    {
        var response = await SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true,
            awaitPromise = true,
            userGesture = true
        }, timeout);

        var root = response.RootElement;
        if (root.TryGetProperty("result", out var resultObj))
        {
            if (resultObj.TryGetProperty("exceptionDetails", out var exceptionDetails))
            {
                string text = exceptionDetails.TryGetProperty("text", out var t) ? t.GetString() ?? "error" : "error";
                string desc = exceptionDetails.TryGetProperty("exception", out var ex) && ex.TryGetProperty("description", out var d)
                    ? d.GetString() ?? "" : "";
                throw new InvalidOperationException($"JavaScript evaluation failed: {text} {desc}\nExpression: {expression}");
            }

            if (resultObj.TryGetProperty("result", out var remoteObj))
            {
                if (remoteObj.TryGetProperty("value", out var val))
                {
                    return val.Deserialize<T>();
                }
            }
        }

        return default;
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[65536];
        var ms = new MemoryStream();

        try
        {
            while (!_cts.Token.IsCancellationRequested && _ws.State == WebSocketState.Open)
            {
                ms.SetLength(0);
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                string messageText = Encoding.UTF8.GetString(ms.ToArray());
                ProcessIncomingMessage(messageText);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(ex);
            }
        }
    }

    private void ProcessIncomingMessage(string rawText)
    {
        // Split potential multiple coalesced JSON payloads
        var chunks = SplitJsonObjects(rawText);
        foreach (var chunk in chunks)
        {
            try
            {
                var doc = JsonDocument.Parse(chunk);
                if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.TryGetInt32(out int id))
                {
                    if (_pendingRequests.TryRemove(id, out var tcs))
                    {
                        if (doc.RootElement.TryGetProperty("error", out var errorProp))
                        {
                            string msg = errorProp.TryGetProperty("message", out var m) ? m.GetString() ?? "Unknown error" : "Unknown error";
                            tcs.TrySetException(new InvalidOperationException($"CDP Error ({id}): {msg}"));
                        }
                        else
                        {
                            tcs.TrySetResult(doc);
                        }
                    }
                }
            }
            catch { }
        }
    }

    private static List<string> SplitJsonObjects(string payload)
    {
        var list = new List<string>();
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        var sb = new StringBuilder();

        foreach (char c in payload)
        {
            if (inString)
            {
                sb.Append(c);
                if (escaped) escaped = false;
                else if (c == '\\') escaped = true;
                else if (c == '"') inString = false;
                continue;
            }

            if (c == '"')
            {
                inString = true;
                sb.Append(c);
                continue;
            }

            if (c == '{')
            {
                depth++;
                sb.Append(c);
                continue;
            }

            if (c == '}')
            {
                depth--;
                sb.Append(c);
                if (depth == 0)
                {
                    list.Add(sb.ToString());
                    sb.Clear();
                }
                continue;
            }

            if (depth > 0)
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 0)
        {
            list.Add(sb.ToString());
        }

        return list;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disposed", CancellationToken.None);
            }
            catch { }
        }
        _ws.Dispose();
        _cts.Dispose();
    }
}
