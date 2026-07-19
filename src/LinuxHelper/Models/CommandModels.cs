using System.Text.Json.Serialization;

namespace LinuxHelper.Models;

/// <summary>
/// Top-level index loaded from Data/commands.json.
/// Keep this file lean; put full detail in per-command JSON files.
/// </summary>
public sealed class CommandIndex
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("commands")]
    public List<CommandSummary> Commands { get; set; } = [];
}

/// <summary>
/// Lightweight entry used for listing and search.
/// detailFile is relative to Data/commands/.
/// </summary>
public sealed class CommandSummary
{
    /// <summary>Primary command name (e.g. "ls"). Used for exact query match.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>One-line summary shown in the home listing.</summary>
    [JsonPropertyName("summary")]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Most common example shown indented under the name line on lists.</summary>
    [JsonPropertyName("example")]
    public string? Example { get; set; }

    /// <summary>When true, included in the default basic list (GET /). Use ?list=full for everything.</summary>
    [JsonPropertyName("basic")]
    public bool Basic { get; set; }

    /// <summary>Concept tags: files, disk, memory, network, process, package, system, text, user, archive.</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>Filename under Data/commands/ (e.g. "ls.json").</summary>
    [JsonPropertyName("detailFile")]
    public string DetailFile { get; set; } = string.Empty;

    /// <summary>
    /// Distros this command applies to. Use ["all"] for universal tools.
    /// Known ids: ubuntu, debian, fedora, arch, opensuse, alpine, rhel, centos.
    /// </summary>
    [JsonPropertyName("distros")]
    public List<string> Distros { get; set; } = ["all"];

    /// <summary>Optional aliases that also resolve to this command (e.g. "ll" -> ls).</summary>
    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    /// <summary>True for package-manager tools; omitted from command lists (see package cheatsheet).</summary>
    public bool IsPackageCommand =>
        Tags.Any(t => t.Equals("package", StringComparison.OrdinalIgnoreCase));
}

/// <summary>basic (default short list) or full (all non-package commands).</summary>
public enum ListMode
{
    Basic,
    Full
}


/// <summary>
/// Full command detail loaded from Data/commands/{name}.json.
/// </summary>
public sealed class CommandDetail
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("synopsis")]
    public string Synopsis { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("examples")]
    public List<CommandExample> Examples { get; set; } = [];

    [JsonPropertyName("options")]
    public List<CommandOption> Options { get; set; } = [];

    [JsonPropertyName("related")]
    public List<string> Related { get; set; } = [];

    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Optional distro-specific notes keyed by distro id.</summary>
    [JsonPropertyName("distroNotes")]
    public Dictionary<string, string>? DistroNotes { get; set; }

    /// <summary>Distro-specific alternate commands (e.g. package install variants).</summary>
    [JsonPropertyName("distroVariants")]
    public Dictionary<string, string>? DistroVariants { get; set; }
}

public sealed class CommandExample
{
    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public sealed class CommandOption
{
    [JsonPropertyName("flag")]
    public string Flag { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

/// <summary>Supported Linux distributions (Data/distros.json).</summary>
public sealed class DistroCatalog
{
    [JsonPropertyName("distros")]
    public List<DistroInfo> Distros { get; set; } = [];
}

public sealed class DistroInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("packageManager")]
    public string PackageManager { get; set; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; set; } = string.Empty;

    /// <summary>Command to refresh indexes and upgrade packages.</summary>
    [JsonPropertyName("update")]
    public string Update { get; set; } = string.Empty;

    /// <summary>Command to install a package (use &lt;pkg&gt; placeholder).</summary>
    [JsonPropertyName("install")]
    public string Install { get; set; } = string.Empty;

    /// <summary>Command to remove a package.</summary>
    [JsonPropertyName("remove")]
    public string Remove { get; set; } = string.Empty;
}

