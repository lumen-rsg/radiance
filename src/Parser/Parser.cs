using Radiance.Lexer;
using Radiance.Parser.Ast;

// ReSharper disable once CheckNamespace
namespace Radiance.Parser;

/// <summary>
/// A recursive-descent parser that converts a flat token stream into an AST.
/// 
/// Grammar (simplified BASH-like):
/// <code>
/// list         := and_or ( separator and_or )* [ separator ]
/// and_or       := pipeline ( ('&&' | '||') pipeline )*
/// pipeline     := command ( '|' command )*
/// command      := simple_command | compound_command
/// compound_command := if_command | for_command | while_command | case_command
/// if_command   := 'if' compound_list 'then' compound_list
///                 ( 'elif' compound_list 'then' compound_list )*
///                 [ 'else' compound_list ] 'fi'
/// for_command  := 'for' WORD [ 'in' word* ] separator 'do' compound_list 'done'
/// while_command := ( 'while' | 'until' ) compound_list 'do' compound_list 'done'
/// case_command := 'case' WORD 'in' case_item* 'esac'
/// case_item    := pattern ( '|' pattern )* ')' compound_list ';;'
/// simple_command := (assignment)* [ word+ ] (redirect)*
/// </code>
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    /// <summary>
    /// Shell keywords recognized in command position.
    /// </summary>
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "if", "then", "elif", "else", "fi",
        "for", "in", "do", "done",
        "while", "until",
        "case", "esac",
        "function",
        "select"
    };

    /// <summary>
    /// Creates a parser over the given token list.
    /// </summary>
    /// <param name="tokens">The tokens from the lexer (must include EOF).</param>
    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
        _pos = 0;
    }

    /// <summary>
    /// Parses the token stream and returns the top-level AST node (a list).
    /// Returns null if the input is empty or only contains comments/whitespace.
    /// </summary>
    /// <returns>The parsed <see cref="ListNode"/>, or null if empty input.</returns>
    public ListNode? Parse()
    {
        SkipCommentsAndNewlines();

        if (IsAtEnd())
            return null;

        return ParseList();
    }

    /// <summary>
    /// Parses a list: pipelines separated by ;, &, newlines, &&, ||.
    /// When <paramref name="terminators"/> is specified, parsing stops when a keyword
    /// matching one of the terminators is encountered in command position.
    /// </summary>
    /// <param name="terminators">Optional keyword terminators (e.g., "then", "fi", "done").</param>
    /// <returns>The parsed list node.</returns>
    private ListNode ParseList(string[]? terminators = null)
    {
        var pipelines = new List<PipelineNode>();
        var separators = new List<TokenType>();

        pipelines.Add(ParseAndOr(terminators));

        while (IsListSeparator(out var separatorType))
        {
            // If terminators are specified and the next meaningful token is a terminator keyword, stop
            if (terminators is not null)
            {
                separators.Add(separatorType);
                Advance(); // consume the separator
                SkipCommentsAndNewlines();

                if (IsAtEnd())
                    break;

                // If the next token is a terminator keyword, the separator we just
                // consumed belongs to this list (e.g., `true; then` in `if true; then`).
                // Stop here — the caller will consume the terminator keyword.
                if (IsKeywordToken(out var kw) && IsTerminator(kw!, terminators))
                {
                    break;
                }

                // A trailing separator doesn't start a new pipeline
                if (IsAtEnd() || IsListSeparatorPeek())
                    break;

                pipelines.Add(ParseAndOr(terminators));
            }
            else
            {
                separators.Add(separatorType);
                Advance(); // consume the separator
                SkipCommentsAndNewlines();

                // A trailing separator doesn't start a new pipeline
                if (IsAtEnd() || IsListSeparatorPeek())
                    break;

                pipelines.Add(ParseAndOr(terminators));
            }
        }

        return new ListNode { Pipelines = pipelines, Separators = separators };
    }

    /// <summary>
    /// Parses a single pipeline. The && and || operators are handled at the
    /// <see cref="ParseList"/> level, so this just delegates to <see cref="ParsePipeline"/>.
    /// </summary>
    /// <param name="terminators">Optional keyword terminators to pass through.</param>
    private PipelineNode ParseAndOr(string[]? terminators = null)
    {
        return ParsePipeline(terminators);
    }

    /// <summary>
    /// Parses a pipeline: commands connected by |.
    /// Each command can be a simple command or a compound command.
    /// </summary>
    /// <param name="terminators">Optional keyword terminators to pass through.</param>
    private PipelineNode ParsePipeline(string[]? terminators = null)
    {
        var commands = new List<AstNode> { ParseCommand(terminators) };

        // Collect piped commands
        while (Current().Type == TokenType.Pipe)
        {
            Advance(); // consume |
            SkipCommentsAndNewlines();
            commands.Add(ParseCommand(terminators));
        }

        return new PipelineNode { Commands = commands };
    }

    /// <summary>
    /// Parses a single command: either a simple command, a compound command, or a function definition.
    /// Dispatches based on the first keyword or pattern.
    /// </summary>
    /// <param name="terminators">Optional keyword terminators for compound list parsing.</param>
    private AstNode ParseCommand(string[]? terminators = null)
    {
        SkipCommentsAndNewlines();

        // Check for compound command keywords
        if (Current().Type == TokenType.Word)
        {
            switch (Current().Value)
            {
                case "if":
                    return ParseIf();
                case "for":
                    return ParseFor();
                case "while":
                    return ParseWhile(isUntil: false);
                case "until":
                    return ParseWhile(isUntil: true);
                case "case":
                    return ParseCase();
                case "function":
                    return ParseFunction();
                case "select":
                    return ParseSelect();
            }

            // Check for name() { ... } pattern — word followed by ( )
            if (Current().Type == TokenType.Word && Lookahead(1)?.Type == TokenType.LParen && Lookahead(2)?.Type == TokenType.RParen)
            {
                return ParseFunctionNameParens();
            }
        }

        // Check for subshell: ( list )
        if (Current().Type == TokenType.LParen)
        {
            return ParseSubshell();
        }

        // Check for brace group: { list; }
        if (Current().Type == TokenType.LBrace)
        {
            return ParseBraceGroup();
        }

        return ParseSimpleCommand();
    }

    // ──── Compound Command Parsers ────

    /// <summary>
    /// Parses an if/elif/else/fi construct.
    /// </summary>
    private IfNode ParseIf()
    {
        ExpectKeyword("if");
        SkipCommentsAndNewlines();

        // Parse condition (stops at 'then')
        var condition = ParseCompoundList("then");
        ExpectKeyword("then");
        SkipCommentsAndNewlines();

        // Parse then-body (stops at 'elif', 'else', or 'fi')
        var thenBody = ParseCompoundList("elif", "else", "fi");

        // Parse optional elif branches
        var elifBranches = new List<(ListNode Condition, ListNode Body)>();
        while (IsKeyword("elif"))
        {
            ExpectKeyword("elif");
            SkipCommentsAndNewlines();
            var elifCondition = ParseCompoundList("then");
            ExpectKeyword("then");
            SkipCommentsAndNewlines();
            var elifBody = ParseCompoundList("elif", "else", "fi");
            elifBranches.Add((elifCondition, elifBody));
        }

        // Parse optional else body
        ListNode? elseBody = null;
        if (IsKeyword("else"))
        {
            ExpectKeyword("else");
            SkipCommentsAndNewlines();
            elseBody = ParseCompoundList("fi");
        }

        ExpectKeyword("fi");

        return new IfNode
        {
            Condition = condition,
            ThenBody = thenBody,
            ElifBranches = elifBranches,
            ElseBody = elseBody
        };
    }

    /// <summary>
    /// Parses a for/in/do/done construct.
    /// </summary>
    private ForNode ParseFor()
    {
        ExpectKeyword("for");
        SkipCommentsAndNewlines();

        // Variable name
        if (Current().Type != TokenType.Word)
        {
            ReportError($"expected variable name after 'for', got {Current().Type}");
            return new ForNode();
        }

        var varName = Advance().Value;

        SkipCommentsAndNewlines();

        // Optional 'in' clause
        var iterableWords = new List<List<WordPart>>();
        if (IsKeyword("in"))
        {
            ExpectKeyword("in");

            // Collect words until separator or 'do'
            while (IsWordToken() && !IsKeyword("do"))
            {
                var wordParts = CollectWordParts();
                iterableWords.Add(wordParts);
            }
        }

        // Skip separator before 'do' (could be ; or newline)
        if (Current().Type is TokenType.Semicolon or TokenType.Newline)
        {
            Advance();
            SkipCommentsAndNewlines();
        }

        ExpectKeyword("do");
        SkipCommentsAndNewlines();

        // Parse body
        var body = ParseCompoundList("done");
        ExpectKeyword("done");

        return new ForNode
        {
            VariableName = varName,
            IterableWords = iterableWords,
            Body = body
        };
    }

    /// <summary>
    /// Parses a select/in/do/done construct.
    /// Displays a numbered menu, reads user selection, sets the variable, and executes the body.
    /// </summary>
    private SelectNode ParseSelect()
    {
        ExpectKeyword("select");
        SkipCommentsAndNewlines();

        // Variable name
        if (Current().Type != TokenType.Word)
        {
            ReportError($"expected variable name after 'select', got {Current().Type}");
            return new SelectNode();
        }

        var varName = Advance().Value;

        SkipCommentsAndNewlines();

        // Optional 'in' clause
        var iterableWords = new List<List<WordPart>>();
        if (IsKeyword("in"))
        {
            ExpectKeyword("in");

            // Collect words until separator or 'do'
            while (IsWordToken() && !IsKeyword("do"))
            {
                var wordParts = CollectWordParts();
                iterableWords.Add(wordParts);
            }
        }

        // Skip separator before 'do' (could be ; or newline)
        if (Current().Type is TokenType.Semicolon or TokenType.Newline)
        {
            Advance();
            SkipCommentsAndNewlines();
        }

        ExpectKeyword("do");
        SkipCommentsAndNewlines();

        // Parse body
        var body = ParseCompoundList("done");
        ExpectKeyword("done");

        return new SelectNode
        {
            VariableName = varName,
            IterableWords = iterableWords,
            Body = body
        };
    }

    /// <summary>
    /// Parses a while/do/done or until/do/done construct.
    /// </summary>
    /// <param name="isUntil">True for 'until', false for 'while'.</param>
    private WhileNode ParseWhile(bool isUntil)
    {
        ExpectKeyword(isUntil ? "until" : "while");
        SkipCommentsAndNewlines();

        // Parse condition (stops at 'do')
        var condition = ParseCompoundList("do");
        ExpectKeyword("do");
        SkipCommentsAndNewlines();

        // Parse body (stops at 'done')
        var body = ParseCompoundList("done");
        ExpectKeyword("done");

        return new WhileNode
        {
            Condition = condition,
            Body = body,
            IsUntil = isUntil
        };
    }

    /// <summary>
    /// Parses a case/esac construct.
    /// </summary>
    private CaseNode ParseCase()
    {
        ExpectKeyword("case");
        SkipCommentsAndNewlines();

        // The word to match
        if (!IsWordToken())
        {
            ReportError($"expected word after 'case', got {Current().Type}");
            return new CaseNode();
        }

        var word = CollectWordParts();

        SkipCommentsAndNewlines();
        ExpectKeyword("in");
        SkipCommentsAndNewlines();

        // Parse case items
        var items = new List<CaseItem>();

        while (!IsKeyword("esac") && !IsAtEnd())
        {
            // Skip optional left paren (BASH allows: case $x in ( pattern) ... )
            if (Current().Type == TokenType.LParen)
            {
                Advance();
                SkipCommentsAndNewlines();
            }

            // Collect patterns separated by |
            var patterns = new List<List<WordPart>>();
            patterns.Add(CollectCasePattern());

            while (Current().Type == TokenType.Pipe)
            {
                Advance(); // consume |
                SkipCommentsAndNewlines();
                patterns.Add(CollectCasePattern());
            }

            // Expect ')'
            if (Current().Type == TokenType.RParen)
            {
                Advance();
            }
            else
            {
                ReportError($"expected ')' after case pattern, got {Current().Type}");
                break;
            }

            SkipCommentsAndNewlines();

            // Parse body (stops at ;; or esac)
            var body = ParseCompoundList(";;", "esac");

            // Consume ;; if present
            if (Current().Type == TokenType.DoubleSemicolon)
            {
                Advance();
                SkipCommentsAndNewlines();
            }

            items.Add(new CaseItem(patterns, body));
        }

        ExpectKeyword("esac");

        return new CaseNode
        {
            Word = word,
            Items = items
        };
    }

    /// <summary>
    /// Collects a single case pattern (word parts until | or )).
    /// </summary>
    private List<WordPart> CollectCasePattern()
    {
        var parts = new List<WordPart>();

        // Handle * as a special catch-all pattern
        if (Current().Type == TokenType.Word && Current().Value == "*")
        {
            parts.Add(new WordPart("*", WordQuoting.None));
            Advance();
            return parts;
        }

        // Collect adjacent word tokens into parts
        while (IsWordToken())
        {
            var token = Current();
            switch (token.Type)
            {
                case TokenType.Word:
                    parts.Add(new WordPart(token.Value, WordQuoting.None));
                    break;
                case TokenType.DoubleQuotedString:
                    parts.Add(new WordPart(token.Value, WordQuoting.Double));
                    break;
                case TokenType.SingleQuotedString:
                    parts.Add(new WordPart(token.Value, WordQuoting.Single));
                    break;
            }
            Advance();
        }

        return parts;
    }

    // ──── Function Parsers ────

    /// <summary>
    /// Parses a function definition using the 'function' keyword syntax.
    /// BASH syntax: <c>function name { body; }</c>
    /// </summary>
    private FunctionNode ParseFunction()
    {
        ExpectKeyword("function");
        SkipCommentsAndNewlines();

        // Get the function name
        if (Current().Type != TokenType.Word)
        {
            ReportError($"expected function name after 'function', got {Current().Type}");
            return new FunctionNode();
        }

        var name = Advance().Value;
        SkipCommentsAndNewlines();

        // Optional () after function name (BASH allows both 'function name' and 'function name()')
        if (Current().Type == TokenType.LParen)
        {
            Advance(); // (
            if (Current().Type == TokenType.RParen)
                Advance(); // )
            SkipCommentsAndNewlines();
        }

        // Expect {
        if (Current().Type != TokenType.LBrace)
        {
            ReportError($"expected '{{' after function name, got '{Current().Value}' ({Current().Type})");
            return new FunctionNode();
        }

        Advance(); // {
        SkipCommentsAndNewlines();

        // Parse function body (stops at })
        var body = ParseBraceBody();

        // Expect }
        if (Current().Type == TokenType.RBrace)
        {
            Advance();
        }
        else
        {
            ReportError($"expected '}}' to close function body, got '{Current().Value}' ({Current().Type})");
        }

        return new FunctionNode { Name = name, Body = body };
    }

    /// <summary>
    /// Parses a function definition using the name() syntax.
    /// BASH syntax: <c>name() { body; }</c>
    /// </summary>
    private FunctionNode ParseFunctionNameParens()
    {
        // Consume function name
        var name = Advance().Value;

        // Consume ( )
        Advance(); // (
        Advance(); // )
        SkipCommentsAndNewlines();

        // Expect {
        if (Current().Type != TokenType.LBrace)
        {
            ReportError($"expected '{{' after '()', got '{Current().Value}' ({Current().Type})");
            return new FunctionNode();
        }

        Advance(); // {
        SkipCommentsAndNewlines();

        // Parse function body (stops at })
        var body = ParseBraceBody();

        // Expect }
        if (Current().Type == TokenType.RBrace)
        {
            Advance();
        }
        else
        {
            ReportError($"expected '}}' to close function body, got '{Current().Value}' ({Current().Type})");
        }

        return new FunctionNode { Name = name, Body = body };
    }

    /// <summary>
    /// Parses a brace-enclosed body, stopping when } is encountered.
    /// Similar to <see cref="ParseCompoundList"/> but uses brace tokens as delimiters.
    /// </summary>
    private ListNode ParseBraceBody()
    {
        var pipelines = new List<PipelineNode>();
        var separators = new List<TokenType>();

        SkipCommentsAndNewlines();

        // If we immediately hit }, return an empty body
        if (Current().Type == TokenType.RBrace)
            return new ListNode();

        // Parse pipelines until we hit }
        pipelines.Add(ParseAndOrForBraceBody());

        while (IsListSeparator(out var separatorType))
        {
            separators.Add(separatorType);
            Advance(); // consume separator
            SkipCommentsAndNewlines();

            // Check for closing brace
            if (Current().Type == TokenType.RBrace || IsAtEnd())
                break;

            pipelines.Add(ParseAndOrForBraceBody());
        }

        return new ListNode { Pipelines = pipelines, Separators = separators };
    }

    /// <summary>
    /// Parses an and_or expression inside a brace body.
    /// Stops at } similarly to how compound list stops at terminator keywords.
    /// </summary>
    private PipelineNode ParseAndOrForBraceBody()
    {
        return ParsePipelineForBraceBody();
    }

    /// <summary>
    /// Parses a pipeline inside a brace body.
    /// </summary>
    private PipelineNode ParsePipelineForBraceBody()
    {
        var commands = new List<AstNode> { ParseCommandForBraceBody() };

        while (Current().Type == TokenType.Pipe)
        {
            Advance();
            SkipCommentsAndNewlines();
            commands.Add(ParseCommandForBraceBody());
        }

        return new PipelineNode { Commands = commands };
    }

    /// <summary>
    /// Parses a single command inside a brace body.
    /// Checks for function definitions as well as compound/simple commands.
    /// </summary>
    private AstNode ParseCommandForBraceBody()
    {
        SkipCommentsAndNewlines();

        if (Current().Type == TokenType.Word)
        {
            switch (Current().Value)
            {
                case "if":
                    return ParseIf();
                case "for":
                    return ParseFor();
                case "while":
                    return ParseWhile(isUntil: false);
                case "until":
                    return ParseWhile(isUntil: true);
                case "case":
                    return ParseCase();
                case "function":
                    return ParseFunction();
                case "select":
                    return ParseSelect();
            }

            // Check for name() { ... } pattern
            if (Lookahead(1)?.Type == TokenType.LParen && Lookahead(2)?.Type == TokenType.RParen)
            {
                return ParseFunctionNameParens();
            }
        }

        // Check for subshell
        if (Current().Type == TokenType.LParen)
        {
            return ParseSubshell();
        }

        // Check for brace group
        if (Current().Type == TokenType.LBrace)
        {
            return ParseBraceGroup();
        }

        return ParseSimpleCommand();
    }

    // ──── Subshell Parser ────

    /// <summary>
    /// Parses a subshell: <c>( list )</c>.
    /// The body is a compound list executed in a child context.
    /// </summary>
    private SubshellNode ParseSubshell()
    {
        Advance(); // consume (
        SkipCommentsAndNewlines();

        // Parse the body — stops at )
        var body = ParseListUntilRParen();

        // Expect )
        if (Current().Type == TokenType.RParen)
        {
            Advance();
        }
        else
        {
            ReportError($"expected ')' to close subshell, got '{Current().Value}' ({Current().Type})");
        }

        // Collect redirections after the subshell
        var redirects = new List<RedirectNode>();
        while (IsRedirectOperator())
        {
            var redirect = ParseRedirect();
            if (redirect is not null)
                redirects.Add(redirect);
        }

        return new SubshellNode { Body = body, Redirects = redirects };
    }

    // ──── Brace Group Parser ────

    /// <summary>
    /// Parses a brace group: <c>{ list; }</c>.
    /// The body is executed in the current shell scope (unlike a subshell).
    /// </summary>
    private BraceGroupNode ParseBraceGroup()
    {
        Advance(); // consume {
        SkipCommentsAndNewlines();

        // Parse the body using the existing brace body parser
        var body = ParseBraceBody();

        // Expect }
        if (Current().Type == TokenType.RBrace)
        {
            Advance();
        }
        else
        {
            ReportError($"expected '}}' to close brace group, got '{Current().Value}' ({Current().Type})");
        }

        // Collect redirections after the brace group
        var redirects = new List<RedirectNode>();
        while (IsRedirectOperator())
        {
            var redirect = ParseRedirect();
            if (redirect is not null)
                redirects.Add(redirect);
        }

        return new BraceGroupNode { Body = body, Redirects = redirects };
    }

    /// <summary>
    /// Parses a list that stops when encountering a closing parenthesis.
    /// Similar to <see cref="ParseList"/> but uses ) as the terminator.
    /// </summary>
    private ListNode ParseListUntilRParen()
    {
        var pipelines = new List<PipelineNode>();
        var separators = new List<TokenType>();

        SkipCommentsAndNewlines();

        if (Current().Type == TokenType.RParen)
            return new ListNode();

        pipelines.Add(ParseAndOrForBraceBody());

        while (IsListSeparator(out var separatorType))
        {
            separators.Add(separatorType);
            Advance();
            SkipCommentsAndNewlines();

            if (Current().Type == TokenType.RParen || IsAtEnd())
                break;

            pipelines.Add(ParseAndOrForBraceBody());
        }

        return new ListNode { Pipelines = pipelines, Separators = separators };
    }

    // ──── Simple Command Parser ────

    /// <summary>
    /// Parses a simple command: optional assignments, command words, and redirections.
    /// Words are stored as <see cref="WordPart"/> lists to preserve quoting context
    /// for the expansion phase.
    /// </summary>
    private SimpleCommandNode ParseSimpleCommand()
    {
        var assignments = new List<AssignmentNode>();
        var arrayAssignments = new List<ArrayAssignmentNode>();
        var words = new List<List<WordPart>>();
        var redirects = new List<RedirectNode>();

        SkipCommentsAndNewlines();

        // Collect prefix assignments
        while (Current().Type == TokenType.AssignmentWord)
        {
            var token = Advance();
            var eqIdx = token.Value.IndexOf('=');
            var name = token.Value[..eqIdx];
            var value = token.Value[(eqIdx + 1)..];

            // Check for array assignment: VAR=(val1 val2 val3)
            if (value.StartsWith('(') && value.EndsWith(')'))
            {
                var inner = value[1..^1].Trim();
                var elements = string.IsNullOrEmpty(inner)
                    ? new List<string>()
                    : SplitArrayElements(inner);
                arrayAssignments.Add(new ArrayAssignmentNode { Name = name, Elements = elements });
            }
            else
            {
                assignments.Add(new AssignmentNode(name, value));
            }
        }

        // Collect command words — includes AssignmentWord tokens after command name
        // (e.g., alias name=value, export VAR=val). Stop at operators and keywords
        // in command position (only when no words collected yet).
        while (IsWordToken() || Current().Type == TokenType.AssignmentWord)
        {
            // Keywords only terminate when in command position (no words yet)
            if (words.Count == 0 && IsKeywordInCommandPosition())
                break;
            
            var wordParts = CollectWordOrAssignmentParts();
            words.Add(wordParts);
        }

        // Collect redirections (with optional fd prefix like 2> or 2>&1)
        var pendingFd = ExtractFdPrefixFromWords(words);

        while (IsRedirectOperator())
        {
            var redirect = ParseRedirect(pendingFd);
            if (redirect is not null)
                redirects.Add(redirect);
            pendingFd = -1; // only the first redirect gets the fd prefix
        }

        return new SimpleCommandNode
        {
            Assignments = assignments,
            ArrayAssignments = arrayAssignments,
            Words = words,
            Redirects = redirects
        };
    }

    /// <summary>
    /// Checks if the last collected word is an fd number prefix (e.g., "2" in "2>&1")
    /// and if so, removes it from the words list. Returns the fd number, or -1 if none.
    /// </summary>
    private int ExtractFdPrefixFromWords(List<List<WordPart>> words)
    {
        if (words.Count == 0)
            return -1;

        var lastWord = words[^1];
        if (lastWord.Count != 1 || lastWord[0].Quoting != WordQuoting.None)
            return -1;

        if (!int.TryParse(lastWord[0].Text, out var fd))
            return -1;

        // The next token must be a redirect operator
        if (!IsRedirectOperator())
            return -1;

        // Remove the fd prefix from words
        words.RemoveAt(words.Count - 1);
        return fd;
    }

    /// <summary>
    /// Splits an array assignment body into elements, respecting quoted strings.
    /// E.g., <c>a "b c" d</c> → ["a", "b c", "d"]
    /// </summary>
    private static List<string> SplitArrayElements(string inner)
    {
        var elements = new List<string>();
        var current = new System.Text.StringBuilder();
        var inSingleQuote = false;
        var inDoubleQuote = false;

        foreach (var c in inner)
        {
            if (c == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
                continue;
            }

            if (c == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
                continue;
            }

            if (char.IsWhiteSpace(c) && !inSingleQuote && !inDoubleQuote)
            {
                if (current.Length > 0)
                {
                    elements.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
            elements.Add(current.ToString());

        return elements;
    }

    /// <summary>
    /// Checks if the current token is a word-like token (Word, DoubleQuotedString, or SingleQuotedString).
    /// </summary>
    private bool IsWordToken() =>
        Current().Type is TokenType.Word or TokenType.DoubleQuotedString or TokenType.SingleQuotedString;

    /// <summary>
    /// Checks if the current token is a keyword that should terminate a simple command.
    /// Keywords like 'then', 'fi', 'do', 'done', 'esac' in command position stop word collection.
    /// </summary>
    private bool IsKeywordInCommandPosition()
    {
        if (Current().Type != TokenType.Word)
            return false;

        // These keywords terminate simple commands when they appear as the first word
        return Current().Value is "then" or "fi" or "elif" or "else"
            or "do" or "done" or "esac";
    }

    /// <summary>
    /// Collects word parts from either regular word tokens or AssignmentWord tokens.
    /// AssignmentWord tokens are treated as a single unquoted word part.
    /// </summary>
    private List<WordPart> CollectWordOrAssignmentParts()
    {
        var wordParts = new List<WordPart>();

        // Handle AssignmentWord token as a single word part
        if (Current().Type == TokenType.AssignmentWord)
        {
            wordParts.Add(new WordPart(Advance().Value, WordQuoting.None));
            return wordParts;
        }

        // Otherwise, use the regular word parts collector
        return CollectWordParts();
    }

    /// <summary>
    /// Collects adjacent word-like tokens into a single word's parts list.
    /// Handles merging of adjacent quoted/unquoted segments (e.g., hello"world").
    /// </summary>
    private List<WordPart> CollectWordParts()
    {
        var wordParts = new List<WordPart>();

        while (IsWordToken() || Current().Type is TokenType.ProcessSubIn or TokenType.ProcessSubOut)
        {
            var token = Current();

            // If this token had leading whitespace, it's a separate word — stop collecting
            if (token.HasLeadingWhitespace && wordParts.Count > 0)
                break;

            switch (token.Type)
            {
                case TokenType.Word:
                    wordParts.Add(new WordPart(token.Value, WordQuoting.None));
                    break;
                case TokenType.DoubleQuotedString:
                    wordParts.Add(new WordPart(token.Value, WordQuoting.Double));
                    break;
                case TokenType.SingleQuotedString:
                    wordParts.Add(new WordPart(token.Value, WordQuoting.Single));
                    break;
                case TokenType.ProcessSubIn:
                {
                    // <(cmd) — extract inner command, strip <( and )
                    var inner = token.Value;
                    if (inner.StartsWith("<(") && inner.EndsWith(')'))
                        inner = inner[2..^1];
                    wordParts.Add(new WordPart("", WordQuoting.None)
                    {
                        ProcessSubstitutionCommand = inner,
                        IsOutputSubstitution = false
                    });
                    break;
                }
                case TokenType.ProcessSubOut:
                {
                    // >(cmd) — extract inner command, strip >( and )
                    var inner = token.Value;
                    if (inner.StartsWith(">(") && inner.EndsWith(')'))
                        inner = inner[2..^1];
                    wordParts.Add(new WordPart("", WordQuoting.None)
                    {
                        ProcessSubstitutionCommand = inner,
                        IsOutputSubstitution = true
                    });
                    break;
                }
            }

            Advance();
        }

        return wordParts;
    }

    /// <summary>
    /// Parses a single I/O redirection.
    /// Handles file redirects (>file, <file, >>file) and fd-duplication redirects (>&N).
    /// </summary>
    private RedirectNode? ParseRedirect(int fdOverride = -1)
    {
        var opToken = Advance();

        // Determine the source file descriptor: use fd prefix if provided,
        // otherwise default from operator type
        var fd = fdOverride >= 0
            ? fdOverride
            : opToken.Type switch
            {
                TokenType.LessThan => 0,
                _ => 1
            };

        // Handle fd-duplication redirect: >&N (e.g., 2>&1)
        if (opToken.Type == TokenType.AmpersandGreaterThan)
        {
            if (!IsWordToken())
            {
                ReportError($"expected file descriptor after '>&', got {Current().Type}");
                return null;
            }

            var targetParts = CollectWordParts();
            var targetText = string.Join("", targetParts.Select(p => p.Text));

            if (!int.TryParse(targetText, out var targetFd))
            {
                ReportError($"expected file descriptor number after '>&', got '{targetText}'");
                return null;
            }

            return new RedirectNode(opToken.Type, null, fd, targetFd);
        }

        if (!IsWordToken())
        {
            ReportError($"expected filename after redirection operator '{opToken.Value}', got {Current().Type}");
            return null;
        }

        // Build word parts for the redirect target (supports quoting)
        var targetParts2 = CollectWordParts();

        return new RedirectNode(opToken.Type, targetParts2, fd);
    }

    // ──── Compound List Helper ────

    /// <summary>
    /// Parses a compound list (used inside control structures) that stops
    /// when encountering one of the terminator keywords.
    /// </summary>
    /// <param name="terminators">Keywords that terminate this compound list.</param>
    /// <returns>The parsed list node.</returns>
    private ListNode ParseCompoundList(params string[] terminators)
    {
        SkipCommentsAndNewlines();

        // If we immediately hit a terminator, return an empty list
        if (IsKeywordToken(out var kw) && IsTerminator(kw!, terminators))
            return new ListNode();

        // If we immediately hit a ;;, also return empty
        if (Current().Type == TokenType.DoubleSemicolon)
            return new ListNode();

        return ParseList(terminators);
    }

    // ──── Keyword Helpers ────

    /// <summary>
    /// Checks if the current token is a Word matching the given keyword.
    /// </summary>
    private bool IsKeyword(string keyword) =>
        Current().Type == TokenType.Word && Current().Value == keyword;

    /// <summary>
    /// Checks if the current token is a keyword Word token, and outputs the keyword value.
    /// </summary>
    private bool IsKeywordToken(out string? keyword)
    {
        if (Current().Type == TokenType.Word && Keywords.Contains(Current().Value))
        {
            keyword = Current().Value;
            return true;
        }

        keyword = null;
        return false;
    }

    /// <summary>
    /// Checks if a keyword is one of the specified terminators.
    /// </summary>
    private static bool IsTerminator(string keyword, string[] terminators)
    {
        foreach (var t in terminators)
        {
            if (t == keyword)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Consumes the current token if it matches the given keyword, otherwise reports an error.
    /// </summary>
    private void ExpectKeyword(string keyword)
    {
        if (Current().Type == TokenType.Word && Current().Value == keyword)
        {
            Advance();
            return;
        }

        ReportError($"expected '{keyword}', got '{Current().Value}' ({Current().Type})");
    }

    /// <summary>
    /// Peeks at the next keyword without consuming. Returns null if not a keyword.
    /// </summary>
    private string? PeekNextKeyword()
    {
        if (Current().Type == TokenType.Word && Keywords.Contains(Current().Value))
            return Current().Value;
        return null;
    }

    // ──── General Helper Methods ────

    /// <summary>
    /// Checks if the current token is a list-level separator (;, &, newline, &&, ||).
    /// </summary>
    private bool IsListSeparator(out TokenType separatorType)
    {
        var type = Current().Type;
        if (type is TokenType.Semicolon or TokenType.Newline or TokenType.Ampersand)
        {
            separatorType = type;
            return true;
        }

        // && and || are also list-level separators for the outer list
        if (type is TokenType.And or TokenType.Or)
        {
            separatorType = type;
            return true;
        }

        separatorType = default;
        return false;
    }

    /// <summary>
    /// Checks if the next token (after current) is a list separator without consuming.
    /// Used to detect trailing separators.
    /// </summary>
    private bool IsListSeparatorPeek()
    {
        var type = Current().Type;
        return type is TokenType.Semicolon or TokenType.Newline or TokenType.Ampersand
            or TokenType.And or TokenType.Or;
    }

    /// <summary>
    /// Checks if the current token is a redirection operator.
    /// </summary>
    private bool IsRedirectOperator() =>
        Current().Type is TokenType.GreaterThan or TokenType.DoubleGreaterThan
            or TokenType.LessThan or TokenType.AmpersandGreaterThan;

    /// <summary>
    /// Checks if the last collected word is a single digit and the next token
    /// is a redirect operator. This detects fd-prefixed redirects like 2>, 2>> or 2>&1.
    /// </summary>
    private bool IsFdRedirectPrefix(List<List<WordPart>> words)
    {
        if (words.Count == 0)
            return false;

        var lastWord = words[^1];
        if (lastWord.Count != 1 || lastWord[0].Quoting != WordQuoting.None)
            return false;

        return int.TryParse(lastWord[0].Text, out _) &&
               Lookahead(1)?.Type is TokenType.GreaterThan or TokenType.DoubleGreaterThan
                   or TokenType.AmpersandGreaterThan;
    }

    /// <summary>
    /// Returns the current token without advancing.
    /// </summary>
    private Token Current() =>
        _pos < _tokens.Count ? _tokens[_pos] : _tokens[^1]; // return EOF if past end

    /// <summary>
    /// Looks ahead <paramref name="offset"/> tokens without advancing.
    /// Returns null if the offset is beyond the token list.
    /// </summary>
    private Token? Lookahead(int offset)
    {
        var idx = _pos + offset;
        return idx < _tokens.Count ? _tokens[idx] : null;
    }

    /// <summary>
    /// Returns the current token and advances the position.
    /// </summary>
    private Token Advance()
    {
        var token = Current();
        if (!IsAtEnd())
            _pos++;
        return token;
    }

    /// <summary>
    /// Checks if we've reached the end of the token stream (EOF).
    /// </summary>
    private bool IsAtEnd() => Current().Type == TokenType.Eof;

    /// <summary>
    /// Skips over comments and newlines.
    /// </summary>
    private void SkipCommentsAndNewlines()
    {
        while (Current().Type is TokenType.Comment or TokenType.Newline)
        {
            Advance();
        }
    }

    /// <summary>
    /// Reports a parse error to stderr.
    /// </summary>
    private static void ReportError(string message)
    {
        Console.Error.WriteLine($"radiance: parse error: {message}");
    }
}