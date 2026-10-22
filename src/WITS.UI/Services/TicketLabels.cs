namespace WITS.UI.Services;

/// <summary>
/// Seeds defined in the TicketSchema migration. The API only returns ids, so the UI maps them here.
/// shortcut: hard-coded status/priority/type names match the shipped migration seeds; read them
/// from the API when it starts exposing lookup endpoints.
/// </summary>
public static class TicketLabels
{
    public static IReadOnlyList<(int Id, string Name)> Statuses { get; } =
    [
        (1, "Open"),
        (2, "In Progress"),
        (3, "Review"),
        (4, "Resolved"),
        (5, "Closed")
    ];

    public static bool IsKnownStatus(int id)
    {
        foreach ((int Id, _) in Statuses)
        {
            if (Id == id)
            {
                return true;
            }
        }

        return false;
    }

    public static string StatusName(int id)
    {
        foreach ((int Id, string name) in Statuses)
        {
            if (Id == id)
            {
                return name;
            }
        }

        return $"Status {id}";
    }

    public static string StatusTone(int id) => id switch
    {
        1 => "open",
        2 => "progress",
        3 => "review",
        4 => "resolved",
        5 => "closed",
        _ => "none"
    };

    public static string PriorityName(int id) => id switch
    {
        1 => "Low",
        2 => "Medium",
        3 => "High",
        4 => "Critical",
        _ => $"Priority {id}"
    };

    public static string PriorityTone(int id) => id switch
    {
        1 => "low",
        2 => "medium",
        3 => "high",
        4 => "critical",
        _ => "none"
    };

    public static string TypeName(int id) => id switch
    {
        1 => "Bug",
        2 => "Feature",
        3 => "Task",
        4 => "Improvement",
        _ => $"Type {id}"
    };
}
