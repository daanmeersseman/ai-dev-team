namespace AiDevTeam.Core.Interfaces;

/// <summary>
/// Renders markdown to sanitized HTML, safe for display in the UI.
/// </summary>
public interface IMarkdownService
{
    string RenderToSafeHtml(string markdown);
}
