using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace VpyAudioCutter;

public enum ScriptSyntax
{
    VapourSynth,
    AviSynth
}

public sealed record TrimSection(int StartFrame, int EndFrame, int SourceLine);

public sealed class VpyParseResult
{
    public List<TrimSection> Sections { get; } = new();
    public List<string> Warnings { get; } = new();
}

public static class VpyTrimParser
{
    private static readonly Regex TrimCallRegex = new(
        @"(?<![A-Za-z0-9_])(?:(?:[A-Za-z_]\w*)\s*\.\s*)*Trim\s*\(",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IntegerAssignmentRegex = new(
        @"(?m)^\s*(?<name>[A-Za-z_]\w*)\s*=\s*(?<value>-?\d+)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex VapourSynthStaticTrimRegex = new(
        @"(?:^|\.)std\s*\.\s*Trim\s*\($",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static VpyParseResult Parse(string source, ScriptSyntax syntax = ScriptSyntax.VapourSynth)
    {
        var result = new VpyParseResult();
        var code = RemoveCommentsAndStrings(source);
        var constants = ReadIntegerConstants(code);
        var seenCallPositions = new HashSet<int>();

        foreach (Match match in TrimCallRegex.Matches(code))
        {
            if (!seenCallPositions.Add(match.Index))
                continue;

            var openParen = code.IndexOf('(', match.Index + match.Length - 1);
            var closeParen = FindMatchingParenthesis(code, openParen);
            if (closeParen < 0)
            {
                result.Warnings.Add($"第 {GetLineNumber(code, match.Index)} 行的 Trim 调用缺少右括号。");
                continue;
            }

            var arguments = SplitTopLevelArguments(code[(openParen + 1)..closeParen]);
            var line = GetLineNumber(code, match.Index);
            // VapourSynth's core.std.Trim takes the clip as its first argument.
            // Bound calls (clip.trim) and AviSynth's bare Trim use frame arguments immediately.
            var firstArgumentIndex = VapourSynthStaticTrimRegex.IsMatch(match.Value) ? 1 : 0;
            if (arguments.Count <= firstArgumentIndex)
            {
                result.Warnings.Add($"第 {line} 行的 Trim 没有提供起始帧。");
                continue;
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = firstArgumentIndex; i < arguments.Count; i++)
            {
                var argument = arguments[i].Trim();
                var equals = FindTopLevelEquals(argument);
                var valuePosition = i - firstArgumentIndex;
                if (equals > 0)
                {
                    var name = argument[..equals].Trim();
                    var value = argument[(equals + 1)..].Trim();
                    values[name] = value;
                }
                else if (valuePosition == 0)
                {
                    values["first"] = argument;
                }
                else if (valuePosition == 1)
                {
                    values["last"] = argument;
                }
                else if (valuePosition == 2)
                {
                    values["length"] = argument;
                }
            }

            if (!TryResolveInteger(values, ["first", "first_frame"], constants, out var start))
            {
                result.Warnings.Add($"第 {line} 行的 Trim 起始帧不是可识别的整数。");
                continue;
            }

            var hasLast = false;
            var end = 0;
            if (syntax == ScriptSyntax.AviSynth)
            {
                if (TryResolveInteger(values, ["end"], constants, out var explicitEnd))
                {
                    end = explicitEnd;
                    hasLast = true;
                }
                else if (TryResolveInteger(values, ["length"], constants, out var explicitLength))
                {
                    if (explicitLength <= 0)
                    {
                        result.Warnings.Add($"第 {line} 行的 Trim length 必须大于 0。");
                        continue;
                    }

                    end = start + explicitLength - 1;
                    hasLast = true;
                }
                else if (TryResolveInteger(values, ["last", "last_frame"], constants, out var aviSynthLast))
                {
                    if (aviSynthLast == 0)
                    {
                        result.Warnings.Add($"第 {line} 行的 AviSynth Trim 使用 last_frame=0（直到片尾），静态解析无法确定片尾帧。");
                        continue;
                    }

                    if (aviSynthLast < 0)
                    {
                        var frameCount = -(long)aviSynthLast;
                        var computedEnd = start + frameCount - 1L;
                        if (computedEnd > int.MaxValue)
                        {
                            result.Warnings.Add($"第 {line} 行的 Trim 帧范围超出整数上限。");
                            continue;
                        }

                        end = (int)computedEnd;
                    }
                    else
                    {
                        end = aviSynthLast;
                    }

                    hasLast = true;
                }
            }
            else
            {
                hasLast = TryResolveInteger(values, ["last", "last_frame"], constants, out end);
                if (!hasLast && TryResolveInteger(values, ["length"], constants, out var length))
                {
                    end = start + length - 1;
                    hasLast = true;
                }
            }

            if (!hasLast)
            {
                result.Warnings.Add($"第 {line} 行的 Trim 没有可识别的结束帧或 length。");
                continue;
            }

            if (start < 0 || end < start)
            {
                result.Warnings.Add($"第 {line} 行的 Trim 区间无效：{start} - {end}。");
                continue;
            }

            result.Sections.Add(new TrimSection(start, end, line));
        }

        return result;
    }

    private static Dictionary<string, int> ReadIntegerConstants(string code)
    {
        var constants = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in IntegerAssignmentRegex.Matches(code))
        {
            if (int.TryParse(match.Groups["value"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                constants[match.Groups["name"].Value] = value;
        }

        return constants;
    }

    private static bool TryResolveInteger(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyList<string> names,
        IReadOnlyDictionary<string, int> constants,
        out int value)
    {
        value = 0;
        var expression = names
            .Select(name => values.TryGetValue(name, out var candidate) ? candidate : null)
            .FirstOrDefault(candidate => candidate is not null);
        if (expression is null)
            return false;

        expression = expression.Trim();
        if (int.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            return true;

        return constants.TryGetValue(expression, out value);
    }

    private static string RemoveCommentsAndStrings(string source)
    {
        var output = new StringBuilder(source.Length);
        var inComment = false;
        var quote = '\0';
        var tripleQuote = false;
        var escaped = false;

        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';
            var nextNext = i + 2 < source.Length ? source[i + 2] : '\0';

            if (inComment)
            {
                if (current is '\r' or '\n')
                {
                    inComment = false;
                    output.Append(current);
                }
                else
                {
                    output.Append(' ');
                }

                continue;
            }

            if (quote != '\0')
            {
                if (tripleQuote && current == quote && next == quote && nextNext == quote)
                {
                    output.Append("   ");
                    i += 2;
                    quote = '\0';
                    tripleQuote = false;
                    escaped = false;
                }
                else if (!tripleQuote && !escaped && current == quote)
                {
                    output.Append(' ');
                    quote = '\0';
                }
                else
                {
                    output.Append(current is '\r' or '\n' ? current : ' ');
                    escaped = !escaped && current == '\\';
                    if (current != '\\')
                        escaped = false;
                }

                continue;
            }

            if (current == '#')
            {
                inComment = true;
                output.Append(' ');
                continue;
            }

            if (current is '\'' or '"')
            {
                quote = current;
                tripleQuote = next == current && nextNext == current;
                output.Append(' ');
                if (tripleQuote)
                {
                    output.Append("  ");
                    i += 2;
                }

                continue;
            }

            output.Append(current);
        }

        return output.ToString();
    }

    private static int FindMatchingParenthesis(string text, int openParen)
    {
        var depth = 0;
        for (var i = openParen; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                        return i;
                    break;
            }
        }

        return -1;
    }

    private static List<string> SplitTopLevelArguments(string text)
    {
        var result = new List<string>();
        var start = 0;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case ',' when parentheses == 0 && brackets == 0 && braces == 0:
                    result.Add(text[start..i]);
                    start = i + 1;
                    break;
            }
        }

        result.Add(text[start..]);
        return result;
    }

    private static int FindTopLevelEquals(string text)
    {
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;

        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses--;
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets--;
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces--;
                    break;
                case '=' when parentheses == 0 && brackets == 0 && braces == 0:
                    if ((i == 0 || text[i - 1] != '=') && (i + 1 >= text.Length || text[i + 1] != '='))
                        return i;
                    break;
            }
        }

        return -1;
    }

    private static int GetLineNumber(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }
}
