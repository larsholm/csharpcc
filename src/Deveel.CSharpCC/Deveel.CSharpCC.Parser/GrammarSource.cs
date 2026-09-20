using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis.Text;

namespace Deveel.CSharpCC.Parser;

// Retain the original text before the legacy character stream decodes Unicode
// escapes. Embedded C# belongs to Roslyn; grammar tokens still use the existing lexer.
internal sealed class GrammarSource : CSharpCharStream {
    internal SourceText Text { get; }
    private readonly int firstLine;
    private readonly int firstColumn;

    internal GrammarSource(TextReader reader, int line, int column)
        : this(reader.ReadToEnd(), line, column) { }

    internal GrammarSource(Stream stream, Encoding? encoding, int line, int column)
        : this(ReadStream(stream, encoding), line, column) { }

    private GrammarSource(string source, int line, int column)
        : base(new StringReader(source), line, column) {
        Text = SourceText.From(source);
        firstLine = line;
        firstColumn = column;
    }

    private static string ReadStream(Stream stream, Encoding? encoding) {
        using var reader = new StreamReader(stream, encoding ?? Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return reader.ReadToEnd();
    }

    internal int Offset(Token token) {
        var line = Text.Lines[token.beginLine - firstLine];
        int column = line.LineNumber == 0 ? firstColumn : 1;
        int offset = line.Start;
        while (offset < line.End && column < token.beginColumn) {
            column = Text[offset] == '\t' ? column + 8 - (column - 1) % 8 : column + 1;
            offset++;
        }
        return offset;
    }

    internal int After(Token token) => token.endLine == 0 ? 0 :
        Offset(new Token { beginLine = token.endLine, beginColumn = token.endColumn + 1 });

    private (int Line, int Column) Coordinates(int offset) {
        var line = Text.Lines.GetLineFromPosition(offset);
        int column = line.LineNumber == 0 ? firstColumn : 1;
        for (int i = line.Start; i < offset; i++)
            column = Text[i] == '\t' ? column + 8 - (column - 1) % 8 : column + 1;
        return (line.LineNumber + firstLine, column);
    }

    internal Token MakeToken(int kind, int start, int end, string image) {
        var begin = Coordinates(start);
        var finish = Coordinates(Math.Max(start, end - 1));
        return new Token {
            kind = kind, image = image, beginLine = begin.Line, beginColumn = begin.Column,
            endLine = finish.Line, endColumn = finish.Column
        };
    }

    internal void Seek(int offset) {
        var position = Coordinates(offset);
        base.ReInit(new SourceReader(Text, offset),
            position.Line, position.Column);
    }

    private sealed class SourceReader(SourceText source, int position) : TextReader {
        public override int Read() => position < source.Length ? source[position++] : -1;

        public override int Read(char[] buffer, int index, int count) {
            int length = Math.Min(count, source.Length - position);
            source.CopyTo(position, buffer, index, length);
            position += length;
            return length;
        }
    }
}
