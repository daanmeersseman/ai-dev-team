using AiDevTeam.Core.Interfaces;
using Markdig;

namespace AiDevTeam.Infrastructure.Services;

public class MarkdownService : IMarkdownService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public string RenderToSafeHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;
        return Markdown.ToHtml(markdown, Pipeline);
    }
}
