using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CityHelper.Services.SlashCommands;

/// <summary>A number a parameter can take and what it does ("0" = turn the particle system on), from the command's own wiki page.</summary>
public sealed class SlashCommandNumber
{
    [JsonPropertyName("value"), JsonPropertyOrder(0)]
    public double Value { get; set; }

    [JsonPropertyName("meaning"), JsonPropertyOrder(1)]
    public string Meaning { get; set; } = "";
}

/// <summary>One parameter of a slash command, as far as the wiki's syntax text tells.</summary>
public sealed class SlashCommandParam
{
    [JsonPropertyName("name"), JsonPropertyOrder(0)]
    public string Name { get; set; } = "";

    /// <summary>int, float, string or enum.</summary>
    [JsonPropertyName("type"), JsonPropertyOrder(1)]
    public string Type { get; set; } = "string";

    [JsonPropertyName("min"), JsonPropertyOrder(2), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Min { get; set; }

    [JsonPropertyName("max"), JsonPropertyOrder(3), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Max { get; set; }

    [JsonPropertyName("optional"), JsonPropertyOrder(4)]
    public bool Optional { get; set; }

    /// <summary>The value used when the parameter is left out, when the command's page names one ("Default = 0").</summary>
    [JsonPropertyName("default"), JsonPropertyOrder(5), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Default { get; set; }

    /// <summary>The numbers the command's page lists for this parameter, with their meaning.</summary>
    [JsonPropertyName("numbers"), JsonPropertyOrder(6), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<SlashCommandNumber>? Numbers { get; set; }

    /// <summary>The choices of an enum parameter ([on|off]).</summary>
    [JsonPropertyName("values"), JsonPropertyOrder(7), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Values { get; set; }
}

/// <summary>A slash command from the wiki's "List of Slash Commands": its name, aliases, description, key and parameters.</summary>
public sealed class SlashCommand
{
    [JsonPropertyName("name"), JsonPropertyOrder(0)]
    public string Name { get; set; } = "";

    [JsonPropertyName("aliases"), JsonPropertyOrder(1)]
    public List<string> Aliases { get; set; } = new();

    [JsonPropertyName("description"), JsonPropertyOrder(2)]
    public string Description { get; set; } = "";

    /// <summary>The key the command is bound to by default, when the wiki names one (for example F2); otherwise null.</summary>
    [JsonPropertyName("defaultKey"), JsonPropertyOrder(3)]
    public string? DefaultKey { get; set; }

    [JsonPropertyName("wikiUrl"), JsonPropertyOrder(4)]
    public string WikiUrl { get; set; } = "";

    [JsonPropertyName("params"), JsonPropertyOrder(5)]
    public List<SlashCommandParam> Params { get; set; } = new();

    /// <summary>The syntax exactly as the wiki shows it, for example [0-359]; null when the command takes nothing.</summary>
    [JsonPropertyName("rawSyntax"), JsonPropertyOrder(6)]
    public string? RawSyntax { get; set; }

    /// <summary>
    /// For a command that takes 0 or 1, the form that flips between the two, written like the command: ++noparticles.
    /// Null for every other command.
    /// </summary>
    [JsonPropertyName("toggle"), JsonPropertyOrder(7), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Toggle { get; set; }

    /// <summary>The explanation from the command's own wiki page (paragraphs and notes), one per line; null when there is none.</summary>
    [JsonPropertyName("details"), JsonPropertyOrder(8), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Details { get; set; }

    /// <summary>The example uses from the command's page, without the leading slash: angle_snap 45.</summary>
    [JsonPropertyName("examples"), JsonPropertyOrder(9), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Examples { get; set; }

    [JsonPropertyName("source"), JsonPropertyOrder(20)]
    public string Source { get; set; } = "wiki";
}
