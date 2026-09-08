namespace Sessions.Core;

/// <summary>A saved context. Runtime process state is deliberately kept separate.</summary>
public sealed record SessionDefinition(
    Guid Id,
    string Name,
    string Description,
    IReadOnlyList<StartProcessAction> Apps,
    Guid? MainAppId = null)
{
    public void Validate()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Description is null || Apps is null)
            throw new ArgumentException("A Session needs an identity, a name, and an app list.");

        var appIds = new HashSet<Guid>();
        foreach (var app in Apps)
        {
            if (app is null || app.Id == Guid.Empty || !appIds.Add(app.Id) ||
                string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.ExecutablePath) ||
                app.Arguments is null || app.WorkingDirectory is null)
                throw new ArgumentException("Every app needs a unique identity, a name, and an executable path.");
        }

        if (MainAppId is { } mainAppId && !appIds.Contains(mainAppId))
            throw new ArgumentException("The app that ends the Session must be in its app list.");
    }
}

public sealed record StartProcessAction(
    Guid Id,
    string Name,
    string ExecutablePath,
    string Arguments = "",
    string WorkingDirectory = "",
    bool RunAsAdministrator = false);
