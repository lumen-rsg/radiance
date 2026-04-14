using System.Text;
using System.Text.RegularExpressions;
using Radiance.Interpreter;

namespace Radiance.Expansion;

/// <summary>
/// Performs arithmetic expansion on shell words, evaluating <c>$((expression))</c>
/// and <c>$[expression]</c> patterns.
/// <para>
/// Supports integer arithmetic operators:
/// <list type="bullet">
/// <item>Basic: <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c>, <c>%</c> (modulo)</item>
/// <item>Comparison: <c><</c>, <c>></c>, <c><=</c>, <c>>=</c>, <c>==</c>, <c>!=</c></item>
/// <item>Logical: <c>&&</c>, <c>||</c></item>
/// <item>Bitwise: <c>&</c>, <c>|</c>, <c>^</c>, <c>~</c>, <c><<</c>, <c>>></c></item>
/// <item>Unary: <c>-</c>, <c>+</c>, <c>!</c></item>
/// <item>Parentheses for grouping</item>
/// <item>Variable references: bare names or <c>$VAR</c></item>
/// </list>
/// </para>
/// </summary>
public sealed class ArithmeticExpander
{
    private readonly ShellContext _context;

    /// <summary>
    /// Creates a new arithmetic expander.
    /// </summary>
    /// <param name="context">The shell execution context for variable lookups.</param>
    public ArithmeticExpander(ShellContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Expands all <c>$((expression))</c> arithmetic expansions in the given text.
    /// </summary>
    /// <param name="text">The text potentially containing arithmetic expressions.</param>
    /// <returns>The text with all arithmetic expressions evaluated and substituted.</returns>
    public string Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Match $((...)) patterns — handle nested parens
        var result = new StringBuilder();
        var i = 0;

        while (i < text.Length)
        {
            if (i + 3 < text.Length && text[i] == '$' && text[i + 1] == '(' && text[i + 2] == '(')
            {
                // Find matching ))
                var start = i + 3;
                var depth = 2; // we've seen two opening parens
                var j = start;

                while (j < text.Length && depth > 0)
                {
                    if (text[j] == '(')
                        depth++;
                    else if (text[j] == ')')
                        depth--;
                    j++;
                }

                if (depth == 0 && j >= 2)
                {
                    // The last two characters we passed should be ))
                    var expr = text[start..(j - 2)];
                    var value = Evaluate(expr);
                    result.Append(value);
                    i = j;
                    continue;
                }
            }

            result.Append(text[i]);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Evaluates an arithmetic expression and returns the result as a string.
    /// </summary>
    /// <param name="expression">The arithmetic expression to evaluate.</param>
    /// <returns>The result of the expression, or "0" on error.</returns>
    private long Evaluate(string expression)
    {
        try
        {
            // First, expand variable references in the expression
            var expanded = ExpandVariablesInExpr(expression.Trim());
            var tokens = TokenizeArithmetic(expanded);
            var parser = new ArithmeticParser(tokens);
            return parser.ParseExpression();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"radiance: arithmetic expansion error: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Expands variable references within an arithmetic expression.
    /// Variables can be referenced as bare names or with $ prefix.
    /// </summary>
    private string ExpandVariablesInExpr(string expr)
    {
        // Replace $VAR references with their values
        var result = new StringBuilder();
        var i = 0;

        while (i < expr.Length)
        {
            if (expr[i] == '$' && i + 1 < expr.Length && (char.IsLetter(expr[i + 1]) || expr[i + 1] == '_'))
            {
                var name = new StringBuilder();
                i++; // skip $
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                {
                    name.Append(expr[i]);
                    i++;
                }

                var value = _context.GetVariable(name.ToString());
                result.Append(string.IsNullOrEmpty(value) ? "0" : value);
            }
            else if (char.IsLetter(expr[i]) || expr[i] == '_')
            {
                // Bare variable name in arithmetic context
                var name = new StringBuilder();
                while (i < expr.Length && (char.IsLetterOrDigit(expr[i]) || expr[i] == '_'))
                {
                    name.Append(expr[i]);
                    i++;
                }

                var nameStr = name.ToString();
                // Check if it's a keyword/operator (not a variable)
                if (nameStr is "and" or "or" or "not" or "xor")
                {
                    result.Append(nameStr);
                }
                else
                {
                    var value = _context.GetVariable(nameStr);
                    result.Append(string.IsNullOrEmpty(value) ? "0" : value);
                }
            }
            else
            {
                result.Append(expr[i]);
                i++;
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Tokenizes an arithmetic expression into numbers and operators.
    /// </summary>
    private static List<ArithToken> TokenizeArithmetic(string expr)
    {
        var tokens = new List<ArithToken>();
        var i = 0;

        while (i < expr.Length)
        {
            if (char.IsWhiteSpace(expr[i]))
            {
                i++;
                continue;
            }

            // Numbers
            if (char.IsDigit(expr[i]))
            {
                var num = new StringBuilder();
                while (i < expr.Length && char.IsDigit(expr[i]))
                {
                    num.Append(expr[i]);
                    i++;
                }

                tokens.Add(new ArithToken(ArithTokenType.Number, num.ToString()));
                continue;
            }

            // Two-character operators
            if (i + 1 < expr.Length)
            {
                var twoChar = expr[i..(i + 2)];
                if (twoChar is "<=" or ">=" or "==" or "!=" or "&&" or "||" or "<<" or ">>")
                {
                    tokens.Add(new ArithToken(ArithTokenType.Operator, twoChar));
                    i += 2;
                    continue;
                }
            }

            // Single-character operators
            if (expr[i] is '+' or '-' or '*' or '/' or '%' or '<' or '>' or '&' or '|' or '^' or '~' or '!' or '=')
            {
                tokens.Add(new ArithToken(ArithTokenType.Operator, expr[i].ToString()));
                i++;
                continue;
            }

            // Parentheses
            if (expr[i] == '(')
            {
                tokens.Add(new ArithToken(ArithTokenType.LParen, "("));
                i++;
                continue;
            }

            if (expr[i] == ')')
            {
                tokens.Add(new ArithToken(ArithTokenType.RParen, ")"));
                i++;
                continue;
            }

            // Unknown character — skip
            i++;
        }

        tokens.Add(new ArithToken(ArithTokenType.Eof, ""));
        return tokens;
    }

    // ──── Arithmetic Token Types ────

    private enum ArithTokenType
    {
        Number,
        Operator,
        LParen,
        RParen,
        Eof,
    }

    private sealed record ArithToken(ArithTokenType Type, string Value);

    // ──── Recursive Descent Arithmetic Parser ────
    // Precedence (low to high):
    //   ||  &&  |  ^  &  == !=  < > <= >=  << >>  + -  * / %  unary  ()

    private sealed class ArithmeticParser(List<ArithToken> tokens)
    {
        private int _pos = 0;

        public long ParseExpression()
        {
            return ParseOr();
        }

        private ArithToken Current() => _pos < tokens.Count ? tokens[_pos] : tokens[^1];

        private ArithToken Advance()
        {
            var t = Current();
            _pos++;
            return t;
        }

        // ||
        private long ParseOr()
        {
            var left = ParseAnd();
            while (Current().Value == "||")
            {
                Advance();
                var right = ParseAnd();
                left = left != 0 || right != 0 ? 1 : 0;
            }

            return left;
        }

        // &&
        private long ParseAnd()
        {
            var left = ParseBitwiseOr();
            while (Current().Value == "&&")
            {
                Advance();
                var right = ParseBitwiseOr();
                left = left != 0 && right != 0 ? 1 : 0;
            }

            return left;
        }

        // |
        private long ParseBitwiseOr()
        {
            var left = ParseBitwiseXor();
            while (Current().Value == "|" && Current().Value != "||")
            {
                Advance();
                left |= ParseBitwiseXor();
            }

            return left;
        }

        // ^
        private long ParseBitwiseXor()
        {
            var left = ParseBitwiseAnd();
            while (Current().Value == "^")
            {
                Advance();
                left ^= ParseBitwiseAnd();
            }

            return left;
        }

        // &
        private long ParseBitwiseAnd()
        {
            var left = ParseEquality();
            while (Current().Value == "&" && Current().Value != "&&")
            {
                Advance();
                left &= ParseEquality();
            }

            return left;
        }

        // == !=
        private long ParseEquality()
        {
            var left = ParseComparison();
            while (Current().Value is "==" or "!=")
            {
                var op = Advance().Value;
                var right = ParseComparison();
                left = op == "==" ? (left == right ? 1 : 0) : (left != right ? 1 : 0);
            }

            return left;
        }

        // < > <= >=
        private long ParseComparison()
        {
            var left = ParseShift();
            while (Current().Value is "<" or ">" or "<=" or ">=")
            {
                var op = Advance().Value;
                var right = ParseShift();
                left = op switch
                {
                    "<" => left < right ? 1 : 0,
                    ">" => left > right ? 1 : 0,
                    "<=" => left <= right ? 1 : 0,
                    ">=" => left >= right ? 1 : 0,
                    _ => 0
                };
            }

            return left;
        }

        // << >>
        private long ParseShift()
        {
            var left = ParseAddSub();
            while (Current().Value is "<<" or ">>")
            {
                var op = Advance().Value;
                var right = ParseAddSub();
                left = op == "<<" ? left << (int)right : left >> (int)right;
            }

            return left;
        }

        // + -
        private long ParseAddSub()
        {
            var left = ParseMulDivMod();
            while (Current().Value is "+" or "-")
            {
                var op = Advance().Value;
                var right = ParseMulDivMod();
                left = op == "+" ? left + right : left - right;
            }

            return left;
        }

        // * / %
        private long ParseMulDivMod()
        {
            var left = ParseUnary();
            while (Current().Value is "*" or "/" or "%")
            {
                var op = Advance().Value;
                var right = ParseUnary();
                left = op switch
                {
                    "*" => left * right,
                    "/" => right != 0 ? left / right : 0,
                    "%" => right != 0 ? left % right : 0,
                    _ => 0
                };
            }

            return left;
        }

        // unary - + ! ~
        private long ParseUnary()
        {
            if (Current().Value == "-")
            {
                Advance();
                return -ParseUnary();
            }

            if (Current().Value == "+")
            {
                Advance();
                return ParseUnary();
            }

            if (Current().Value == "!")
            {
                Advance();
                var val = ParseUnary();
                return val == 0 ? 1 : 0;
            }

            if (Current().Value == "~")
            {
                Advance();
                return ~ParseUnary();
            }

            return ParsePrimary();
        }

        // number or (expr)
        private long ParsePrimary()
        {
            if (Current().Type == ArithTokenType.Number)
            {
                var value = long.Parse(Advance().Value);
                return value;
            }

            if (Current().Type == ArithTokenType.LParen)
            {
                Advance(); // skip (
                var result = ParseExpression();
                if (Current().Type == ArithTokenType.RParen)
                    Advance(); // skip )
                return result;
            }

            // Unexpected token — return 0
            Advance();
            return 0;
        }
    }
}