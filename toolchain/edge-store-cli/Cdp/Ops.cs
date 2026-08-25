namespace Vainreef.EdgeStore.Cdp;

/// <summary>
/// Shared, ultra-light operation tracer. Every real browser interaction
/// (navigation, wait, click, type, select, check) publishes a timestamped line
/// to the console and to ops.log under the session state directory.
/// </summary>
public static class Ops
{
    public static string LogRoot { get; set; } = string.Empty;

    private static readonly object Gate = new();

    public static void Publish(string level, string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
        Console.WriteLine(line);
        if (!string.IsNullOrWhiteSpace(LogRoot))
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LogRoot);
                    File.AppendAllText(Path.Combine(LogRoot, "ops.log"), line + Environment.NewLine, new System.Text.UTF8Encoding(false));
                }
            }
            catch
            {
                // logging must never break the run
            }
        }
    }

    public static void Nav(string url) => Publish("NAV", "navigate -> " + url);
    public static void Wait(string d) => Publish("WAIT", "wait: " + d);
    public static void WaitOk(string d) => Publish("WAIT-OK", "wait satisfied: " + d);
    public static void Click(string what, string detail = "") => Publish("CLICK", (detail.Length > 0 ? detail + " | " : "") + what);
    public static void Type(string where, string value) => Publish("TYPE", $"set value [{value}] into {where}");
    public static void Select(string what, string option) => Publish("SELECT", $"choose [{option}] for {what}");
    public static void Check(string what, bool to) => Publish("CHECK", $"{what} -> {to}");
    public static void Eval(string tag, string detail = "") => Publish("EVAL", (detail.Length > 0 ? detail + " | " : "") + tag);
    public static void Info(string m) => Publish("INFO", m);
}
