using System.Text;
using System.Text.Json;
using LinuxHelper.Models;

namespace LinuxHelper.Services;

/// <summary>
/// Loads command index + detail JSON at startup and serves lookups from memory.
/// To add a command: append an entry to Data/commands.json and add Data/commands/{name}.json.
/// Package management cheatsheet lives in Data/distros.json (shown at the bottom of the home page).
/// </summary>
public sealed class CommandCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly ILogger<CommandCatalogService> _logger;
    private readonly IReadOnlyList<CommandSummary> _summaries;
    private readonly IReadOnlyDictionary<string, CommandDetail> _detailsByName;
    private readonly IReadOnlyDictionary<string, string> _aliasToName;
    private readonly IReadOnlyList<DistroInfo> _distros;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<CommandSummary>> _byTag;
    private readonly IReadOnlyList<string> _allTags;

    public CommandCatalogService(IWebHostEnvironment env, ILogger<CommandCatalogService> logger)
    {
        _logger = logger;
        var dataRoot = Path.Combine(env.ContentRootPath, "Data");
        var commandsDir = Path.Combine(dataRoot, "commands");

        var index = LoadJson<CommandIndex>(Path.Combine(dataRoot, "commands.json"))
            ?? throw new InvalidOperationException("Data/commands.json is missing or invalid.");
        var distroCatalog = LoadJson<DistroCatalog>(Path.Combine(dataRoot, "distros.json"))
            ?? new DistroCatalog();

        _distros = distroCatalog.Distros;

        var details = new Dictionary<string, CommandDetail>(StringComparer.OrdinalIgnoreCase);
        var aliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var validSummaries = new List<CommandSummary>();

        foreach (var summary in index.Commands)
        {
            if (string.IsNullOrWhiteSpace(summary.Name))
            {
                _logger.LogWarning("Skipping command with empty name in index.");
                continue;
            }

            var detailPath = Path.Combine(commandsDir, summary.DetailFile);
            if (!File.Exists(detailPath))
            {
                _logger.LogWarning("Detail file missing for {Command}: {Path}", summary.Name, detailPath);
                continue;
            }

            var detail = LoadJson<CommandDetail>(detailPath);
            if (detail is null)
            {
                _logger.LogWarning("Failed to parse detail for {Command}", summary.Name);
                continue;
            }

            if (summary.Tags.Count == 0 && detail.Tags.Count > 0)
                summary.Tags = detail.Tags;

            if (string.IsNullOrWhiteSpace(summary.Example))
                summary.Example = PickPrimaryExample(summary.Name, detail);

            details[summary.Name] = detail;
            aliasMap[summary.Name] = summary.Name;
            foreach (var alias in summary.Aliases)
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    aliasMap[alias] = summary.Name;
            }

            validSummaries.Add(summary);
        }

        _summaries = validSummaries
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        _detailsByName = details;
        _aliasToName = aliasMap;

        var byTag = new Dictionary<string, List<CommandSummary>>(StringComparer.OrdinalIgnoreCase);
        foreach (var cmd in _summaries)
        {
            foreach (var tag in cmd.Tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;
                if (!byTag.TryGetValue(tag, out var list))
                {
                    list = [];
                    byTag[tag] = list;
                }
                list.Add(cmd);
            }
        }

        _byTag = byTag.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<CommandSummary>)kv.Value
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            StringComparer.OrdinalIgnoreCase);
        _allTags = _byTag.Keys.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();

        _logger.LogInformation(
            "Loaded {CommandCount} commands ({BasicCount} basic), {TagCount} tags, {DistroCount} distros from {DataRoot}",
            _summaries.Count,
            _summaries.Count(c => c.Basic),
            _allTags.Count,
            _distros.Count,
            dataRoot);
    }

    public IReadOnlyList<CommandSummary> AllCommands => _summaries;
    public IReadOnlyList<string> AllTags => _allTags;
    public IReadOnlyList<DistroInfo> Distros => _distros;

    public static ListMode ParseListMode(string? list, string? full, string? all)
    {
        if (IsTruthy(full) || IsTruthy(all))
            return ListMode.Full;

        if (string.IsNullOrWhiteSpace(list))
            return ListMode.Basic;

        return list.Trim().ToLowerInvariant() switch
        {
            "full" or "all" or "everything" => ListMode.Full,
            "basic" or "short" or "common" => ListMode.Basic,
            _ => ListMode.Basic
        };
    }

    public bool TryGetCommand(string query, out CommandSummary summary, out CommandDetail detail)
    {
        summary = null!;
        detail = null!;
        if (string.IsNullOrWhiteSpace(query))
            return false;

        if (!_aliasToName.TryGetValue(query.Trim(), out var name))
            return false;
        if (!_detailsByName.TryGetValue(name, out detail!))
            return false;

        summary = _summaries.First(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    public IReadOnlyList<CommandSummary> GetByTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return Array.Empty<CommandSummary>();
        return _byTag.TryGetValue(tag.Trim(), out var list) ? list : Array.Empty<CommandSummary>();
    }

    public bool IsKnownTag(string tag) =>
        !string.IsNullOrWhiteSpace(tag) && _byTag.ContainsKey(tag.Trim());

    /// <summary>
    /// Free-text search: any word/phrase in name, aliases, summary, description,
    /// tags, options, examples, or notes. Includes package-manager commands.
    /// </summary>
    public IReadOnlyList<CommandSummary> Search(string query, ListMode listMode)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<CommandSummary>();

        var q = query.Trim();
        var pool = GetVisibleCommands(listMode, includePackageCommands: true);

        var hits = new List<(CommandSummary Cmd, int Score)>();
        foreach (var cmd in pool)
        {
            if (!_detailsByName.TryGetValue(cmd.Name, out var detail))
                continue;

            var score = ScoreMatch(cmd, detail, q);
            if (score > 0)
                hits.Add((cmd, score));
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Cmd.Name, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.Cmd)
            .ToList();
    }

    /// <summary>
    /// Commands for listings. Package managers are omitted from home lists
    /// (covered by the cheatsheet at the bottom) unless includePackageCommands is true.
    /// </summary>
    public IReadOnlyList<CommandSummary> GetVisibleCommands(ListMode listMode, bool includePackageCommands = false)
    {
        IEnumerable<CommandSummary> q = _summaries;

        if (!includePackageCommands)
            q = q.Where(c => !c.IsPackageCommand);

        if (listMode == ListMode.Basic)
            q = q.Where(c => c.Basic);

        return q.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public string FormatHome(ListMode listMode)
    {
        var visible = GetVisibleCommands(listMode);
        var sb = new StringBuilder();

        sb.AppendLine("linux-helper — quick Linux command reference");
        sb.AppendLine(new string('=', 52));
        sb.AppendLine();
        sb.AppendLine("USAGE");
        sb.AppendLine("  GET /                    Basic common commands (default)");
        sb.AppendLine("  GET /?list=full          All commands");
        sb.AppendLine("  GET /?q=<word>           Search name/description (or exact command)");
        sb.AppendLine();
        sb.AppendLine("  Params: q|query, list=basic|full  (also full=1)");
        sb.AppendLine();
        sb.AppendLine("EXAMPLES");
        var baseUrl = "https://linux-helper.com";
        sb.AppendLine($"  curl '{baseUrl}/'");
        sb.AppendLine($"  curl '{baseUrl}/?list=full'");
        sb.AppendLine($"  curl '{baseUrl}/?q=ls'");
        sb.AppendLine($"  curl '{baseUrl}/?q=permission'");
        sb.AppendLine();
        sb.AppendLine($"LIST:  {(listMode == ListMode.Basic ? "basic" : "full")}  ({visible.Count} commands)");
        if (listMode == ListMode.Basic)
            sb.AppendLine("      Use ?list=full for every command.");
        sb.AppendLine();

        if (listMode == ListMode.Basic)
            AppendGroupedCompactList(sb, visible);
        else
        {
            sb.AppendLine("COMMANDS");
            sb.AppendLine(new string('-', 52));
            AppendCompactList(sb, visible);
        }

        sb.AppendLine($"Total: {visible.Count}.  ?q=<name> for details.");
        sb.AppendLine();

        // Package management is always the last section on the home page.
        AppendPackageCheatsheet(sb);

        return sb.ToString();
    }

    public string FormatCommandDetail(CommandSummary summary, CommandDetail detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{detail.Name}  —  {summary.Summary}");
        sb.AppendLine(new string('=', 52));
        sb.AppendLine();
        sb.AppendLine(detail.Description.Trim());
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(summary.Example))
        {
            sb.AppendLine("COMMON");
            sb.AppendLine($"  {summary.Example}");
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(detail.Synopsis))
        {
            sb.AppendLine("SYNOPSIS");
            foreach (var line in detail.Synopsis.Split('\n'))
                sb.AppendLine($"  {line.TrimEnd()}");
            sb.AppendLine();
        }

        if (detail.Tags.Count > 0)
        {
            sb.AppendLine("TAGS");
            sb.AppendLine("  " + string.Join(", ", detail.Tags));
            sb.AppendLine();
        }

        if (detail.Options.Count > 0)
        {
            sb.AppendLine("OPTIONS");
            var flagWidth = Math.Min(22, detail.Options.Max(o => o.Flag.Length));
            foreach (var opt in detail.Options)
                sb.AppendLine($"  {opt.Flag.PadRight(flagWidth)}  {opt.Description}");
            sb.AppendLine();
        }

        if (detail.Examples.Count > 0)
        {
            sb.AppendLine("EXAMPLES");
            foreach (var ex in detail.Examples)
            {
                sb.AppendLine($"  {ex.Command}");
                if (!string.IsNullOrWhiteSpace(ex.Description))
                    sb.AppendLine($"    {ex.Description}");
            }
            sb.AppendLine();
        }

        if (summary.IsPackageCommand)
            AppendPackageCheatsheetForManager(sb, summary.Name);

        if (!string.IsNullOrWhiteSpace(detail.Notes))
        {
            sb.AppendLine("NOTES");
            sb.AppendLine($"  {detail.Notes.Trim()}");
            sb.AppendLine();
        }

        if (detail.Related.Count > 0)
        {
            sb.AppendLine("RELATED");
            sb.AppendLine("  " + string.Join(", ", detail.Related));
            sb.AppendLine();
        }

        if (summary.Aliases.Count > 0)
        {
            sb.AppendLine("ALIASES");
            sb.AppendLine("  " + string.Join(", ", summary.Aliases));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    public string FormatCommandList(
        string title,
        IReadOnlyList<CommandSummary> commands,
        ListMode listMode)
    {
        var allowed = new HashSet<string>(
            GetVisibleCommands(listMode, includePackageCommands: true).Select(c => c.Name),
            StringComparer.OrdinalIgnoreCase);

        var filtered = commands
            .Where(c => allowed.Contains(c.Name))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Package hits still show on basic search results.
        if (listMode == ListMode.Basic)
        {
            filtered = filtered
                .Where(c => c.Basic || c.IsPackageCommand)
                .ToList();
        }

        var sb = new StringBuilder();
        sb.AppendLine(title);
        sb.AppendLine(new string('=', 52));
        sb.AppendLine($"Matches: {filtered.Count}");
        sb.AppendLine();

        if (filtered.Count == 0)
        {
            sb.AppendLine("No matching commands.");
            sb.AppendLine("Try ?list=full or a different word.  GET / for the basic list.");
            return sb.ToString();
        }

        AppendCompactList(sb, filtered);
        sb.AppendLine();
        sb.AppendLine($"?q=<name> for full details.  Example: ?q={filtered[0].Name}");
        return sb.ToString();
    }

    public string FormatNotFound(string query)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Nothing found for: '{query}'");
        sb.AppendLine();

        var suggestions = _summaries
            .Select(c => c.Name)
            .Where(n => n.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || query.Contains(n, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        var tagHits = _allTags
            .Where(t => t.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(8)
            .ToList();

        if (suggestions.Count > 0)
        {
            sb.AppendLine("Close command names:");
            foreach (var s in suggestions)
                sb.AppendLine($"  ?q={s}");
            sb.AppendLine();
        }

        if (tagHits.Count > 0)
        {
            sb.AppendLine("Close tags:");
            foreach (var t in tagHits)
                sb.AppendLine($"  ?q={t}");
            sb.AppendLine();
        }

        sb.AppendLine("GET / for basic commands.  ?list=full for everything.");
        return sb.ToString();
    }

    private void AppendPackageCheatsheet(StringBuilder sb)
    {
        sb.AppendLine("PACKAGE MANAGEMENT");
        sb.AppendLine(new string('-', 52));
        sb.AppendLine("Distro / family          Mgr      Update / Install / Remove");
        sb.AppendLine();

        foreach (var d in _distros)
        {
            sb.AppendLine($"{d.Name,-24} {d.PackageManager,-8}");
            if (!string.IsNullOrWhiteSpace(d.Update))
                sb.AppendLine($"  update   {d.Update}");
            if (!string.IsNullOrWhiteSpace(d.Install))
                sb.AppendLine($"  install  {d.Install}");
            if (!string.IsNullOrWhiteSpace(d.Remove))
                sb.AppendLine($"  remove   {d.Remove}");
            sb.AppendLine();
        }

        sb.AppendLine("  More detail: ?q=apt  ?q=dnf  ?q=pacman  ?q=zypper  ?q=apk");
        sb.AppendLine();
    }

    private void AppendPackageCheatsheetForManager(StringBuilder sb, string packageManager)
    {
        var rows = _distros
            .Where(d => d.PackageManager.Equals(packageManager, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (rows.Count == 0)
            return;

        sb.AppendLine("DISTROS USING THIS TOOL");
        foreach (var d in rows)
        {
            sb.AppendLine($"  {d.Name}");
            if (!string.IsNullOrWhiteSpace(d.Update))
                sb.AppendLine($"    update   {d.Update}");
            if (!string.IsNullOrWhiteSpace(d.Install))
                sb.AppendLine($"    install  {d.Install}");
            if (!string.IsNullOrWhiteSpace(d.Remove))
                sb.AppendLine($"    remove   {d.Remove}");
        }
        sb.AppendLine();
    }

    private void AppendCompactList(StringBuilder sb, IReadOnlyList<CommandSummary> commands)
    {
        if (commands.Count == 0)
            return;

        var maxName = Math.Min(14, commands.Max(c => c.Name.Length));
        foreach (var cmd in commands)
        {
            var example = cmd.Example;
            if (string.IsNullOrWhiteSpace(example) && _detailsByName.TryGetValue(cmd.Name, out var detail))
                example = PickPrimaryExample(cmd.Name, detail);

            if (!string.IsNullOrWhiteSpace(example))
                sb.AppendLine($"{cmd.Name.PadRight(maxName)}  {cmd.Summary}  →  {example}");
            else
                sb.AppendLine($"{cmd.Name.PadRight(maxName)}  {cmd.Summary}");
        }
    }

    private void AppendGroupedCompactList(StringBuilder sb, IReadOnlyList<CommandSummary> commands)
    {
        // Most-specific tag first so e.g. du (disk+files) lands under DISK, not FILES.
        // "files" and "directory" share one display section: DIRECTORY/FILES.
        // Package managers are not grouped here — they only appear in the bottom cheatsheet.
        string[] groupPriority =
        [
            "disk", "memory", "network", "process", "service", "archive",
            "text", "permissions", "security", "user", "shell", "search", "web", "docs",
            "files", "system"
        ];

        string PrimaryTag(CommandSummary c)
        {
            foreach (var tag in groupPriority)
            {
                if (tag.Equals("files", StringComparison.OrdinalIgnoreCase))
                {
                    if (c.Tags.Any(t =>
                            t.Equals("files", StringComparison.OrdinalIgnoreCase)
                            || t.Equals("directory", StringComparison.OrdinalIgnoreCase)))
                        return "files";
                    continue;
                }

                if (c.Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    return tag;
            }
            return c.Tags.FirstOrDefault() ?? "other";
        }

        static string DisplayName(string tag) =>
            tag.Equals("files", StringComparison.OrdinalIgnoreCase) ? "DIRECTORY/FILES" : tag.ToUpperInvariant();

        var groups = commands
            .GroupBy(PrimaryTag, StringComparer.OrdinalIgnoreCase)
            .Select(g => (
                Title: DisplayName(g.Key),
                Items: g.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(g => g.Items.Count > 0)
            .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var (title, items) in groups)
        {
            sb.AppendLine(title);
            sb.AppendLine(new string('-', 52));
            AppendCompactList(sb, items);
            sb.AppendLine();
        }
    }

    private static int ScoreMatch(CommandSummary cmd, CommandDetail detail, string q)
    {
        if (cmd.Name.Equals(q, StringComparison.OrdinalIgnoreCase))
            return 1000;
        if (cmd.Aliases.Any(a => a.Equals(q, StringComparison.OrdinalIgnoreCase)))
            return 950;
        if (cmd.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 800;
        if (cmd.Aliases.Any(a => a.Contains(q, StringComparison.OrdinalIgnoreCase)))
            return 750;
        if (cmd.Tags.Any(t => t.Equals(q, StringComparison.OrdinalIgnoreCase)))
            return 700;
        if (cmd.Summary.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 500;
        if (detail.Description.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 400;
        if (cmd.Tags.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase)))
            return 350;
        if (!string.IsNullOrWhiteSpace(cmd.Example) && cmd.Example.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 300;
        if (detail.Examples.Any(e =>
                e.Command.Contains(q, StringComparison.OrdinalIgnoreCase)
                || e.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
            return 250;
        if (detail.Options.Any(o =>
                o.Flag.Contains(q, StringComparison.OrdinalIgnoreCase)
                || o.Description.Contains(q, StringComparison.OrdinalIgnoreCase)))
            return 200;
        if (!string.IsNullOrWhiteSpace(detail.Notes) && detail.Notes.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 150;
        if (detail.Synopsis.Contains(q, StringComparison.OrdinalIgnoreCase))
            return 100;

        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length > 1)
        {
            var blob = BuildSearchBlob(cmd, detail);
            if (tokens.All(t => blob.Contains(t, StringComparison.OrdinalIgnoreCase)))
                return 120;
        }

        return 0;
    }

    private static string BuildSearchBlob(CommandSummary cmd, CommandDetail detail)
    {
        var sb = new StringBuilder();
        sb.Append(cmd.Name).Append(' ')
            .Append(cmd.Summary).Append(' ')
            .Append(string.Join(' ', cmd.Aliases)).Append(' ')
            .Append(string.Join(' ', cmd.Tags)).Append(' ')
            .Append(detail.Description).Append(' ')
            .Append(detail.Synopsis).Append(' ')
            .Append(detail.Notes).Append(' ')
            .Append(cmd.Example);
        foreach (var e in detail.Examples)
            sb.Append(' ').Append(e.Command).Append(' ').Append(e.Description);
        foreach (var o in detail.Options)
            sb.Append(' ').Append(o.Flag).Append(' ').Append(o.Description);
        return sb.ToString();
    }

    private static string? PickPrimaryExample(string name, CommandDetail detail)
    {
        if (detail.Examples.Count == 0)
            return null;

        var withArgs = detail.Examples.FirstOrDefault(e =>
            !string.Equals(e.Command.Trim(), name, StringComparison.OrdinalIgnoreCase)
            && e.Command.Trim().StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (withArgs is not null)
            return withArgs.Command.Trim();

        return detail.Examples[0].Command.Trim();
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "y" or "on" or "full" or "all";
    }

    private static T? LoadJson<T>(string path) where T : class
    {
        if (!File.Exists(path))
            return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
