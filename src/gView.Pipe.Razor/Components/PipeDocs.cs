using System.Globalization;

namespace gView.Pipe.Razor.Components;

/// <summary>
/// Where the documentation lives, for the help links of the UI.
/// The host registers it (the Shell: its own <c>/docs</c> when built, else the public site).
/// </summary>
public sealed class PipeDocs
{
    public const string PublicUrl = "https://docs.gviewonline.com/other/gview-pipe/";

    public PipeDocs(string? baseUrl = null)
    {
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? PublicUrl : baseUrl.TrimEnd('/') + "/";
    }

    public string BaseUrl { get; }

    /// <summary>Link to a page of the site, e.g. <c>user-guide/editor.html#workflow-settings</c>; German pages when the UI language is German.</summary>
    public string Url(string page = "index.html")
        => BaseUrl + (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de/" : "") + page;

    /// <summary>Page of a node in the node reference, as written by <c>gView.Pipe.Docs</c>: the category page, anchored at the node.</summary>
    public string NodeUrl(string category, string node) => Url($"user-guide/nodes/{Slug(category)}.html#{Slug(node)}");

    // the slug MkDocs makes for a heading: lower case, everything else becomes '-'
    static private string Slug(string text)
    {
        var slug = new System.Text.StringBuilder();
        foreach (var character in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }
        }
        return slug.ToString().TrimEnd('-');
    }
}
