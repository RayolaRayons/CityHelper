namespace CityHelper.Models;

public sealed record Installation(string Name, string Path, bool IsDefault = false)
{
    public string DisplayName => IsDefault ? Name + " (default)" : Name;
    public override string ToString() => DisplayName;
}
