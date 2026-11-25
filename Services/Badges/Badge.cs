using System.Text.Json.Serialization;

namespace CityHelper.Services.Badges;

/// <summary>A badge from badges-all.json: the names and descriptions for heroes and villains, its category, icons and requirements.</summary>
public sealed class Badge
{
    [JsonPropertyName("internalName")] public string InternalName { get; set; } = "";
    [JsonPropertyName("heroName")] public string HeroName { get; set; } = "";
    [JsonPropertyName("villainName")] public string VillainName { get; set; } = "";
    [JsonPropertyName("icon")] public string Icon { get; set; } = "";
    [JsonPropertyName("villainIcon")] public string VillainIcon { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("categoryKey")] public string CategoryKey { get; set; } = "";
    [JsonPropertyName("releaseDate")] public string ReleaseDate { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("villainDescription")] public string VillainDescription { get; set; } = "";
    [JsonPropertyName("requirements")] public string Requirements { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("badgeId")] public string BadgeId { get; set; } = "";
}
