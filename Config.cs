using System.Text.Json.Serialization;
using CounterStrikeSharp.API.Core;

namespace SyntX34_AdminSounds;

public class PluginConfig : BasePluginConfig
{
    [JsonPropertyName("ConfigVersion")]
    public override int Version { get; set; } = 1;

    [JsonPropertyName("PluginEnabled")]
    public bool PluginEnabled { get; set; } = true;

    [JsonPropertyName("AdminPermission")]
    public string AdminPermission { get; set; } = "";
}

public class SoundsConfig
{
    [JsonPropertyName("ConfigVersion")]
    public int ConfigVersion { get; set; } = 1;

    [JsonPropertyName("Sounds")]
    public Dictionary<string, SoundConfig> Sounds { get; set; } = new();
}

public class SoundConfig
{
    [JsonPropertyName("Path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("Duration")]
    public float Duration { get; set; } = 0;

    [JsonPropertyName("Cooldown")]
    public float Cooldown { get; set; } = 0;
}