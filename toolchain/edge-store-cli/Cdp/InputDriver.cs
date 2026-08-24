namespace Vainreef.EdgeStore.Cdp;

public class InputDriver
{
    private readonly CdpClient _client;
    private readonly AxLocator _locator;

    public InputDriver(CdpClient client, AxLocator locator)
    {
        _client = client;
        _locator = locator;
    }

    public async Task ClickNodeAsync(ResolvedNode node, string label = "")
    {
        var center = await _locator.GetCenterAsync(node);
        if (!center.HasValue)
        {
            throw new InvalidOperationException($"Cannot resolve clickable coordinates for [{label} ({node.Source})]");
        }

        await ClickCoordinatesAsync(center.Value.X, center.Value.Y, label);
    }

    public async Task ClickCoordinatesAsync(double x, double y, string label = "")
    {
        int ix = (int)Math.Round(x);
        int iy = (int)Math.Round(y);

        await _client.SendAsync("Input.dispatchMouseEvent", new
        {
            type = "mouseMoved",
            x = ix,
            y = iy
        });
        await Task.Delay(50);

        await _client.SendAsync("Input.dispatchMouseEvent", new
        {
            type = "mousePressed",
            button = "left",
            clickCount = 1,
            x = ix,
            y = iy
        });
        await Task.Delay(50);

        await _client.SendAsync("Input.dispatchMouseEvent", new
        {
            type = "mouseReleased",
            button = "left",
            clickCount = 1,
            x = ix,
            y = iy
        });
        await Task.Delay(100);
    }

    public async Task InsertTextAsync(string text)
    {
        await _client.SendAsync("Input.insertText", new { text });
        await Task.Delay(50);
    }

    public async Task PressKeyAsync(string key, string code = "Enter", int virtualKeyCode = 13)
    {
        await _client.SendAsync("Input.dispatchKeyEvent", new
        {
            type = "keyDown",
            key,
            code,
            windowsVirtualKeyCode = virtualKeyCode,
            nativeVirtualKeyCode = virtualKeyCode
        });
        await Task.Delay(50);

        await _client.SendAsync("Input.dispatchKeyEvent", new
        {
            type = "keyUp",
            key,
            code,
            windowsVirtualKeyCode = virtualKeyCode,
            nativeVirtualKeyCode = virtualKeyCode
        });
        await Task.Delay(50);
    }
}
