using System.Text.Json;

namespace Vainreef.EdgeStore.Cdp;

public class AxLocator
{
    private readonly CdpClient _client;
    private readonly DomDriver _dom;

    public AxLocator(CdpClient client, DomDriver dom)
    {
        _client = client;
        _dom = dom;
    }

    public async Task<ResolvedNode?> FindByRoleAndNameAsync(string role, string name, bool exact = true)
    {
        var response = await _client.SendAsync("Accessibility.queryAXTree", new
        {
            role,
            accessibleName = name
        });

        var nodes = response.RootElement.GetProperty("result").GetProperty("nodes");
        foreach (var node in nodes.EnumerateArray())
        {
            string? nodeName = null;
            if (node.TryGetProperty("name", out var nameProp) && nameProp.TryGetProperty("value", out var valProp))
            {
                nodeName = valProp.GetString();
            }

            if (!string.IsNullOrEmpty(nodeName))
            {
                bool matches = exact
                    ? string.Equals(nodeName.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)
                    : nodeName.Contains(name, StringComparison.OrdinalIgnoreCase);

                if (matches && node.TryGetProperty("backendDOMNodeId", out var backendProp))
                {
                    int backendId = backendProp.GetInt32();
                    return new ResolvedNode
                    {
                        BackendNodeId = backendId,
                        Role = role,
                        Name = nodeName,
                        Source = "AXTree"
                    };
                }
            }
        }

        return null;
    }

    public async Task<ResolvedNode?> FindByCssAsync(string selector)
    {
        int rootId = await _dom.GetRootNodeIdAsync();
        int? nodeId = await _dom.QuerySelectorAsync(rootId, selector);
        if (nodeId.HasValue)
        {
            return new ResolvedNode
            {
                NodeId = nodeId.Value,
                Source = "CSS:" + selector
            };
        }

        return null;
    }

    public async Task<ResolvedNode?> FindVisibleByJsAsync(string selector)
    {
        var result = await _client.EvaluateAsync<JsElementRect>($$"""
        (() => {
          const els = Array.from(document.querySelectorAll('{{selector}}')).filter(e => {
            const r = e.getBoundingClientRect(), s = getComputedStyle(e);
            return r.width > 0 && r.height > 0 && s.display !== 'none' && s.visibility !== 'hidden' && !e.disabled;
          });
          if (els.length !== 1) return null;
          const r = els[0].getBoundingClientRect();
          return { x: r.left + r.width / 2, y: r.top + r.height / 2, width: r.width, height: r.height };
        })()
        """);

        if (result != null)
        {
            return new ResolvedNode
            {
                DirectCoordinates = (result.X, result.Y),
                Source = "JSVisible:" + selector
            };
        }

        return null;
    }

    public async Task<(double X, double Y)?> GetCenterAsync(ResolvedNode node)
    {
        if (node.DirectCoordinates.HasValue)
        {
            return node.DirectCoordinates.Value;
        }

        await _dom.ScrollIntoViewIfNeededAsync(node.NodeId, node.BackendNodeId);
        var box = await _dom.GetBoxModelAsync(node.NodeId, node.BackendNodeId);
        if (box != null)
        {
            return (box.CenterX, box.CenterY);
        }

        return null;
    }
}

public class ResolvedNode
{
    public int? NodeId { get; set; }
    public int? BackendNodeId { get; set; }
    public string? Role { get; set; }
    public string? Name { get; set; }
    public (double X, double Y)? DirectCoordinates { get; set; }
    public string Source { get; set; } = string.Empty;
}

public class JsElementRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}
