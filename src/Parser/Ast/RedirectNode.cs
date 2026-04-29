namespace Radiance.Parser.Ast;

/// <summary>
/// Represents an I/O redirection (e.g., >file, &lt;file, >>file, 2>file, 2>&amp;1).
/// Also supports here-documents (&lt;&lt;), here-document with tab stripping (&lt;&lt;-),
/// and here-strings (&lt;&lt;&lt;).
/// </summary>
/// <param name="RedirectType">The type of redirection (token type of the operator).</param>
/// <param name="Target">The target filename, as word parts for expansion. Null for fd-duplication redirects (>&amp;). For heredocs, this is the delimiter word.</param>
/// <param name="FileDescriptor">The file descriptor being redirected (0=stdin, 1=stdout, 2=stderr). Default is 1 for output, 0 for input.</param>
/// <param name="DuplicateTargetFd">For fd-duplication redirects (>&amp;), the target file descriptor number (e.g., 1 for 2>&amp;1). -1 means not an fd-dup redirect.</param>
/// <param name="HeredocContent">For heredoc redirects, the raw content between the delimiters. Lines are joined with \n.</param>
/// <param name="HeredocStripTabs">True for &lt;&lt;- — strip leading tabs from heredoc content.</param>
/// <param name="HeredocQuoted">True if the heredoc delimiter was quoted (suppresses expansion in the content).</param>
public sealed record RedirectNode(
    Radiance.Lexer.TokenType RedirectType,
    List<WordPart>? Target,
    int FileDescriptor = 1,
    int DuplicateTargetFd = -1,
    string? HeredocContent = null,
    bool HeredocStripTabs = false,
    bool HeredocQuoted = false);
