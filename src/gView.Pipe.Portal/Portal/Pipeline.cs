using Markdig;

namespace gView.Pipe.Portal.Portal;

/// <summary>Markdown of results to HTML, without raw HTML in the text.</summary>
static public class Pipeline
{
    static private readonly MarkdownPipeline Markdown = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    static public string Render(string markdown) => Markdig.Markdown.ToHtml(markdown, Markdown);
}
