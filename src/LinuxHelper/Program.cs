using LinuxHelper.Models;
using LinuxHelper.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // In containers, load Data/ next to the published files (not the process cwd).
    ContentRootPath = AppContext.BaseDirectory
});

// Cloud Run injects PORT (often 8080). Prefer it over any static Urls config.
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Load all command JSON at startup and keep it in memory for the process lifetime.
builder.Services.AddSingleton<CommandCatalogService>();

var app = builder.Build();

// Force catalog load at startup so missing data fails fast.
_ = app.Services.GetRequiredService<CommandCatalogService>();

app.MapGet("/", (
    HttpRequest request,
    CommandCatalogService catalog) =>
{
    var query = FirstNonEmpty(
        request.Query["q"].FirstOrDefault(),
        request.Query["query"].FirstOrDefault());

    var listMode = CommandCatalogService.ParseListMode(
        request.Query["list"].FirstOrDefault(),
        request.Query["full"].FirstOrDefault(),
        request.Query["all"].FirstOrDefault());

    // No query → home: basic/full command list + package cheatsheet at the bottom.
    if (string.IsNullOrWhiteSpace(query))
        return TextResult(catalog.FormatHome(listMode));

    var q = query.Trim();

    // 1) Exact command / alias → full detail page.
    if (catalog.TryGetCommand(q, out var summary, out var detail))
        return TextResult(catalog.FormatCommandDetail(summary, detail));

    // 2) Exact tag → compact list for that tag (always full set for the tag).
    if (catalog.IsKnownTag(q))
    {
        var tagged = catalog.GetByTag(q);
        return TextResult(catalog.FormatCommandList($"TAG: {q}", tagged, ListMode.Full));
    }

    // 3) Free-text search across name, summary, description, examples, options, …
    var hits = catalog.Search(q, listMode);
    if (hits.Count == 1)
    {
        catalog.TryGetCommand(hits[0].Name, out var s, out var d);
        return TextResult(catalog.FormatCommandDetail(s, d));
    }

    if (hits.Count > 1)
        return TextResult(catalog.FormatCommandList($"SEARCH: {q}", hits, listMode));

    // 4) If basic mode found nothing, retry against full catalog before 404.
    if (listMode == ListMode.Basic)
    {
        var fullHits = catalog.Search(q, ListMode.Full);
        if (fullHits.Count > 0)
        {
            return TextResult(catalog.FormatCommandList(
                $"SEARCH: {q}  (from full list — not in basic)",
                fullHits,
                ListMode.Full));
        }
    }

    return TextResult(catalog.FormatNotFound(q), StatusCodes.Status404NotFound);
})
.WithName("Helper");

app.Run();

static string? FirstNonEmpty(params string?[] values) =>
    values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

static IResult TextResult(string body, int statusCode = StatusCodes.Status200OK) =>
    Results.Text(body, contentType: "text/plain; charset=utf-8", statusCode: statusCode);
