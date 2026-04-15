namespace Radiance.Parser.Ast;

/// <summary>
/// Represents an I/O redirection (e.g., >file, <file, >>file, 2>file, 2>&1).
/// </summary>
/// <param name="RedirectType">The type of redirection (token type of the operator).</param>
/// <param name="Target">The target filename, as word parts for expansion. Null for fd-duplication redirects (>&).</param>
/// <param name="FileDescriptor">The file descriptor being redirected (0=stdin, 1=stdout, 2=stderr). Default is 1 for output, 0 for input.</param>
/// <param name="DuplicateTargetFd">For fd-duplication redirects (>&), the target file descriptor number (e.g., 1 for 2>&1). -1 means not an fd-dup redirect.</param>
public sealed record RedirectNode(
    Radiance.Lexer.TokenType RedirectType,
    List<WordPart>? Target,
    int FileDescriptor = 1,
    int DuplicateTargetFd = -1);
