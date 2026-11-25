using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CityHelper.Services.Emotes;

/// <summary>An emote from the wiki's Emotes page: its name, the aliases that play the same animation, and what it does.</summary>
public sealed class Emote
{
    [JsonPropertyName("name"), JsonPropertyOrder(0)]
    public string Name { get; set; } = "";

    [JsonPropertyName("aliases"), JsonPropertyOrder(1)]
    public List<string> Aliases { get; set; } = new();

    [JsonPropertyName("description"), JsonPropertyOrder(2)]
    public string Description { get; set; } = "";
}
