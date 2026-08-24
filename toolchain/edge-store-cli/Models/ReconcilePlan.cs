namespace Vainreef.EdgeStore.State;

public class ReconcilePlan
{
    public string Phase { get; set; } = string.Empty;
    public List<ReconcileAction> Actions { get; set; } = [];

    public bool HasDifferences => Actions.Count > 0;

    public void AddChange(string field, object? current, object? desired, string description)
    {
        Actions.Add(new ReconcileAction
        {
            Phase = Phase,
            Field = field,
            CurrentValue = current?.ToString() ?? "(null)",
            DesiredValue = desired?.ToString() ?? "(null)",
            Description = description
        });
    }

    public override string ToString()
    {
        if (!HasDifferences)
        {
            return $"[{Phase}] 0 differences (already converged)";
        }

        var lines = Actions.Select(a => $"  * {a.Field}: [{a.CurrentValue}] -> [{a.DesiredValue}] ({a.Description})");
        return $"[{Phase}] {Actions.Count} difference(s) detected:\n" + string.Join("\n", lines);
    }
}

public class ReconcileAction
{
    public string Phase { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string CurrentValue { get; set; } = string.Empty;
    public string DesiredValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
