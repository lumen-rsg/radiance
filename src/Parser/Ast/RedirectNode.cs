namespace Radiance.Parser.Ast;

/// <summary>
/// Represents an I/O redirection (e.g., >file, <file, >>file, 2>file).
/// </summary>
/// <param name="RedirectType">The type of redirection (token type of the operator).</param>
/// <param name="Target">The target filename or file descriptor, as word parts for expansion.</param>
/// <param name="FileDescriptor">The file descriptor being redirected (0=stdin, 1=stdout, 2=stderr). Default is 1 for output, 0 for input.</param>
public sealed record RedirectNode(
    Radiance.Lexer.TokenType RedirectType,
    List<WordPart> Target,
    int FileDescriptor = 1);
