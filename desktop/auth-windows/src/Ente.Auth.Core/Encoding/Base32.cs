namespace Ente.Auth.Core.Encoding;

public static class Base32
{
    public static byte[] Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Where(c => !char.IsWhiteSpace(c) && c is not '-' and not '=')
            .Select(char.ToUpperInvariant).ToArray();
        var output = new List<byte>((normalized.Length * 5) / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var character in normalized)
        {
            var index = character switch
            {
                >= 'A' and <= 'Z' => character - 'A',
                >= '2' and <= '7' => character - '2' + 26,
                _ => throw new FormatException($"Invalid Base32 character '{character}'."),
            };
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            output.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }

        return output.ToArray();
    }
}
