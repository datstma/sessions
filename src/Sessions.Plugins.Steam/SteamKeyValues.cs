using System.Text;

namespace Sessions.Plugins.Steam;

/// <summary>Small bounded reader for Steam's text VDF/ACF files. Does not write or repair client files.</summary>
internal sealed class SteamKeyValues
{
    public Dictionary<string, object> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string? Text(string key) => Values.GetValueOrDefault(key) as string;
    public SteamKeyValues? Object(string key) => Values.GetValueOrDefault(key) as SteamKeyValues;

    public static SteamKeyValues Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Steam metadata is too large.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[(int)stream.Length + 1];
        var length = reader.ReadBlock(buffer, 0, buffer.Length);
        if (length == buffer.Length) throw new InvalidDataException("Steam metadata is too large.");
        var parser = new Parser(new string(buffer, 0, length));
        return parser.Parse();
    }

    private sealed class Parser(string text)
    {
        private int _position;
        public SteamKeyValues Parse(int depth = 0)
        {
            if (depth > 32) throw new InvalidDataException("Steam metadata nesting is too deep.");
            var result = new SteamKeyValues();
            while (true)
            {
                var key = Token(out var keyIsBrace);
                if (key is null)
                {
                    if (depth > 0) throw new InvalidDataException("Steam metadata is incomplete.");
                    return result;
                }
                if (keyIsBrace && key == "}")
                {
                    if (depth == 0) throw new InvalidDataException("Unexpected Steam metadata closing brace.");
                    return result;
                }
                if (keyIsBrace) throw new InvalidDataException("Missing Steam metadata key.");
                var value = Token(out var valueIsBrace) ?? throw new InvalidDataException("Missing Steam metadata value.");
                if (valueIsBrace && value == "}") throw new InvalidDataException("Missing Steam metadata value.");
                if (!result.Values.TryAdd(key, valueIsBrace ? Parse(depth + 1) : value))
                    throw new InvalidDataException("Steam metadata contains duplicate keys.");
            }
        }

        private string? Token(out bool isBrace)
        {
            isBrace = false;
            while (_position < text.Length)
            {
                if (char.IsWhiteSpace(text[_position])) { _position++; continue; }
                if (text[_position] == '/' && _position + 1 < text.Length && text[_position + 1] == '/')
                { while (_position < text.Length && text[_position] != '\n') _position++; continue; }
                break;
            }
            if (_position == text.Length) return null;
            var first = text[_position++];
            if (first is '{' or '}') { isBrace = true; return first.ToString(); }
            var result = new StringBuilder();
            if (first != '"')
            {
                result.Append(first);
                while (_position < text.Length && !char.IsWhiteSpace(text[_position]) && text[_position] is not ('{' or '}')) result.Append(text[_position++]);
                return result.ToString();
            }
            while (_position < text.Length)
            {
                var value = text[_position++];
                if (value == '"') return result.ToString();
                if (value == '\\' && _position < text.Length && text[_position] is '\\' or '"') value = text[_position++];
                result.Append(value);
            }
            throw new InvalidDataException("Unterminated Steam metadata string.");
        }
    }
}
