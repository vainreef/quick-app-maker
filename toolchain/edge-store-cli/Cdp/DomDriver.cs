using System.Text.Json;

namespace Vainreef.EdgeStore.Cdp;

public class DomDriver
{
    private readonly CdpClient _client;

    public DomDriver(CdpClient client)
    {
        _client = client;
    }

    public async Task<int> GetRootNodeIdAsync()
    {
        // Never serialize an entire Partner Center SPA. A shallow root is enough
        // for DOM.requestNode / querySelector and stays bounded on large pages.
        var doc = await _client.SendAsync("DOM.getDocument", new { depth = 1, pierce = false });
        return doc.RootElement.GetProperty("result").GetProperty("root").GetProperty("nodeId").GetInt32();
    }

    public async Task<int?> RequestNodeBySelectorAsync(string selector)
    {
        var response = await _client.SendAsync("Runtime.evaluate", new
        {
            expression = $"document.querySelector({JsonSerializer.Serialize(selector)})",
            returnByValue = false
        });
        var remote = response.RootElement.GetProperty("result").GetProperty("result");
        if (!remote.TryGetProperty("objectId", out var objectId)) return null;
        var node = await _client.SendAsync("DOM.requestNode", new { objectId = objectId.GetString() });
        int id = node.RootElement.GetProperty("result").GetProperty("nodeId").GetInt32();
        return id > 0 ? id : null;
    }

    public async Task<int?> RequestNodeByExpressionAsync(string expression)
    {
        var response = await _client.SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = false,
            awaitPromise = true
        });
        var remote = response.RootElement.GetProperty("result").GetProperty("result");
        if (!remote.TryGetProperty("objectId", out var objectId)) return null;
        var node = await _client.SendAsync("DOM.requestNode", new { objectId = objectId.GetString() });
        int id = node.RootElement.GetProperty("result").GetProperty("nodeId").GetInt32();
        return id > 0 ? id : null;
    }

    public async Task<int?> QuerySelectorAsync(int rootNodeId, string selector)
    {
        var resp = await _client.SendAsync("DOM.querySelector", new { nodeId = rootNodeId, selector });
        int nodeId = resp.RootElement.GetProperty("result").GetProperty("nodeId").GetInt32();
        return nodeId > 0 ? nodeId : null;
    }

    public async Task<List<int>> QuerySelectorAllAsync(int rootNodeId, string selector)
    {
        var resp = await _client.SendAsync("DOM.querySelectorAll", new { nodeId = rootNodeId, selector });
        var nodeIds = resp.RootElement.GetProperty("result").GetProperty("nodeIds");
        return nodeIds.EnumerateArray().Select(e => e.GetInt32()).ToList();
    }

    public async Task<BoxModel?> GetBoxModelAsync(int? nodeId = null, int? backendNodeId = null)
    {
        var param = new Dictionary<string, object>();
        if (nodeId.HasValue) param["nodeId"] = nodeId.Value;
        if (backendNodeId.HasValue) param["backendNodeId"] = backendNodeId.Value;

        try
        {
            var resp = await _client.SendAsync("DOM.getBoxModel", param);
            var model = resp.RootElement.GetProperty("result").GetProperty("model");
            var content = model.GetProperty("content").EnumerateArray().Select(e => e.GetDouble()).ToArray();

            // 8 coordinates: [x1, y1, x2, y2, x3, y3, x4, y4]
            double x = (content[0] + content[2] + content[4] + content[6]) / 4.0;
            double y = (content[1] + content[3] + content[5] + content[7]) / 4.0;
            int width = model.GetProperty("width").GetInt32();
            int height = model.GetProperty("height").GetInt32();

            return new BoxModel(x, y, width, height);
        }
        catch
        {
            return null;
        }
    }

    public async Task ScrollIntoViewIfNeededAsync(int? nodeId = null, int? backendNodeId = null)
    {
        var param = new Dictionary<string, object>();
        if (nodeId.HasValue) param["nodeId"] = nodeId.Value;
        if (backendNodeId.HasValue) param["backendNodeId"] = backendNodeId.Value;

        try
        {
            await _client.SendAsync("DOM.scrollIntoViewIfNeeded", param);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CDP-WARN] scrollIntoView failed: {ex.Message}");
        }
    }

    public async Task SetFileInputFilesAsync(int nodeId, string[] files)
    {
        await _client.SendAsync("DOM.setFileInputFiles", new
        {
            nodeId,
            files = files.Select(Path.GetFullPath).ToArray()
        });
    }

    public async Task<string?> GetObjectIdByExpressionAsync(string expression)
    {
        var response = await _client.SendAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = false,
            awaitPromise = true
        });
        var remote = response.RootElement.GetProperty("result").GetProperty("result");
        return remote.TryGetProperty("objectId", out var objectId) ? objectId.GetString() : null;
    }

    public async Task SetFileInputFilesByObjectIdAsync(string objectId, string[] files)
    {
        await _client.SendAsync("DOM.setFileInputFiles", new
        {
            objectId,
            files = files.Select(Path.GetFullPath).ToArray()
        });
    }
}

public record BoxModel(double CenterX, double CenterY, int Width, int Height);
