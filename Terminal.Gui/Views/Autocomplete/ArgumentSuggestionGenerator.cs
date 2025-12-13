using System.CommandLine.Completions;
using System.Text;
using Terminal.Gui.Drawing;

namespace Terminal.Gui.Views;

public record ArgumentSuggestion(string Name, ICollection<string> Aliases, string Description);

public class ArgumentSuggestionGenerator : ISuggestionGenerator
{
    // All quote marks from the Unicode documentation
    public static readonly HashSet<int> QuoteMarks = new([
        // Basic quotation marks
        '\u0022', // " - Quotation mark
        '\u0027', // ' - Apostrophe
        // '\u00AB', // « - Left-pointing double angle quotation mark
        // '\u00BB', // » - Right-pointing double angle quotation mark
    
        // Curved quotes
        '\u2018', // ' - Left single quotation mark
        '\u2019', // ' - Right single quotation mark
        '\u201A', // ‚ - Single low-9 quotation mark
        '\u201B', // ‛ - Single high-reversed-9 quotation mark
        '\u201C', // " - Left double quotation mark
        '\u201D', // " - Right double quotation mark
        '\u201E', // „ - Double low-9 quotation mark
        '\u201F', // ‟ - Double high-reversed-9 quotation mark
    
        // Angle quotes
        // '\u2039', // ‹ - Single left-pointing angle quotation mark
        // '\u203A', // › - Single right-pointing angle quotation mark
        '\u2E42', // ⹂ - Double low-reversed-9 quotation mark
    
        // Dingbats (decorative)
        '\u275B', // ❛ - Heavy single turned comma quotation mark ornament
        '\u275C', // ❜ - Heavy single comma quotation mark ornament
        '\u275D', // ❝ - Heavy double turned comma quotation mark ornament
        '\u275E', // ❞ - Heavy double comma quotation mark ornament
        0x1F676, // 🙶 - Sans-serif heavy double turned comma quotation mark ornament
        0x1F677, // 🙷 - Sans-serif heavy double comma quotation mark ornament
        0x1F678, // 🙸 - Sans-serif heavy low double comma quotation mark ornament
    
        // Braille
        // '\u2826', // ⠦ - Braille pattern dots-236
        // '\u2834', // ⠴ - Braille pattern dots-356
    
        // CJK (Chinese, Japanese, Korean)
        // '\u300C', // 「 - Left corner bracket
        // '\u300D', // 」 - Right corner bracket
        // '\u300E', // 『 - Left white corner bracket
        // '\u300F', // 』 - Right white corner bracket
        '\u301D', // 〝 - Reversed double prime quotation mark
        '\u301E', // 〞 - Double prime quotation mark
        '\u301F', // 〟 - Low double prime quotation mark
    
        // Alternate encodings
        // '\uFE41', // ﹁ - Presentation form for vertical left corner bracket
        // '\uFE42', // ﹂ - Presentation form for vertical right corner bracket
        // '\uFE43', // ﹃ - Presentation form for vertical left white corner bracket
        // '\uFE44', // ﹄ - Presentation form for vertical right white corner bracket
        '\uFF02', // ＂ - Fullwidth quotation mark
        '\uFF07', // ＇ - Fullwidth apostrophe
    ]);

    /// <summary>The full set of all strings that can be suggested.</summary>
    /// <returns></returns>
    public virtual List<CompletionItem> AllSuggestions { get; set; } = [];
    public int SelectedIdx { get; private set; } = -1;
    public int ViewingIdx { get; set; } = -1;

    public Action<CompletionItem>? SelectedSuggestionChanged;
    public Action<CompletionItem>? ViewingSuggestionChanged;

    /// <inheritdoc/>
    public IEnumerable<Suggestion> GenerateSuggestions(AutocompleteContext context)
    {
        ChangeSelectedIdx(SelectedIdx);
        // if there is nothing to pick from
        if (AllSuggestions.Count == 0)
        {
            return [];
        }

        List<string> line = [.. context.CurrentLine.Select(c => c.Grapheme)];
        string currentWord = IdxToWord(line, context.CursorPosition, out int startIdx);
        context.CursorPosition = startIdx < 1 ? startIdx : Math.Min(startIdx + 1, line.Count);

        if (string.IsNullOrWhiteSpace(currentWord))
        {
            var pos = context.CursorPosition;
            while (pos > 0 && char.IsWhiteSpace(line[pos - 1], 0))
            {
                pos--;
            }
            if (pos <= 0 || string.IsNullOrWhiteSpace(IdxToWord(line, Math.Max(0, pos), out startIdx)))
            {
                return [];
            }
        }

        if (ViewingIdx < 0 || ViewingIdx >= AllSuggestions.Count)
        {
            ChangeViewingIdx(0);
        }

        return AllSuggestions.Select(o => new Suggestion(currentWord.Length, o.InsertText, o.Label + (string.IsNullOrEmpty(o.Detail) ? "" : " - " + o.Detail)))
                             .ToList()
                             .AsReadOnly();
    }

    public void ChangeSelectedIdx(int idx)
    {
        if (idx == SelectedIdx)
        {
            return;
        }
        if (idx < 0 || idx >= AllSuggestions.Count)
        {
            SelectedIdx = -1;
            return;
        }
        ViewingIdx = idx;
        SelectedIdx = idx;
        SelectedSuggestionChanged?.Invoke(AllSuggestions[SelectedIdx]);
    }

    public void ChangeViewingIdx(int idx)
    {
        if (idx < 0 || idx >= AllSuggestions.Count || idx == ViewingIdx)
        {
            return;
        }
        ViewingIdx = idx;
        ViewingSuggestionChanged?.Invoke(AllSuggestions[ViewingIdx]);
    }

    public bool IsCurrentWordIsSuggestion(string text, int cursorPosition, Suggestion suggestion)
    {
        List<Cell> currentLine = Cell.ToCellList(text);
        int idx = Math.Min(cursorPosition, currentLine.Count);

        List<string> line = [.. currentLine.Select(c => c.Grapheme)];
        string currentWord = IdxToWord(line, idx, out int startIdx);

        return currentWord == suggestion.Replacement;
    }

    /// <summary>
    ///     Return true if the given symbol should be considered part of a word and can be contained in matches. Base
    ///     behavior is to use <see cref="char.IsLetterOrDigit(char)"/> but since we need to deal with command line
    ///     arguments, we also consider quote marks as part of words.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns></returns>
    public virtual bool IsWordChar(string text)
    {
        return !string.IsNullOrEmpty(text)
               && (Rune.IsLetterOrDigit(text.EnumerateRunes().First())
                    || QuoteMarks.Contains(text.EnumerateRunes().First().Value));
    }

    /// <summary>
    ///     <para>
    ///         Given a <paramref name="line"/> of characters, returns the word which ends at <paramref name="idx"/> or null.
    ///         Also returns null if the <paramref name="idx"/> is positioned in the middle of a word.
    ///     </para>
    ///     <para>
    ///         Use this method to determine whether autocomplete should be shown when the cursor is at a given point in a
    ///         line and to get the word from which suggestions should be generated. Use the <paramref name="columnOffset"/> to
    ///         indicate if search the word at left (negative), at right (positive) or at the current column (zero) which is
    ///         the default.
    ///     </para>
    /// </summary>
    /// <param name="line"></param>
    /// <param name="idx"></param>
    /// <param name="startIdx">The start index of the word.</param>
    /// <param name="columnOffset"></param>
    /// <returns></returns>
    protected virtual string IdxToWord(List<string> line, int idx, out int startIdx, int columnOffset = 0)
    {
        var sb = new StringBuilder();
        startIdx = idx;

        // get the ending word index
        while (startIdx < line.Count)
        {
            if (IsWordChar(line[startIdx]))
            {
                startIdx++;
            }
            else
            {
                break;
            }
        }

        // It isn't a word char then there is no way to autocomplete that word
        if (startIdx == idx && columnOffset != 0)
        {
            return null;
        }

        // we are at the end of a word. Work out what has been typed so far
        while (startIdx-- > 0)
        {
            if (IsWordChar(line[startIdx]))
            {
                sb.Insert(0, line[startIdx]);
            }
            else
            {
                break;
            }
        }

        startIdx = Math.Max(startIdx, 0);

        return sb.ToString();
    }
}
