using System.Text.Json.Serialization;

namespace Naqsh.Core.Model;

[JsonConverter(typeof(JsonStringEnumConverter<InputKind>))]
public enum InputKind
{
    Color = 0,
    Image = 1,
    Video = 2,
}

/// <summary>Straight (non-premultiplied) RGBA color, 0..1 per channel.</summary>
public readonly record struct RgbaColor(float R, float G, float B, float A = 1f)
{
    public static RgbaColor Black { get; } = new(0, 0, 0);

    public static RgbaColor FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255f, g / 255f, b / 255f, a / 255f);

    public string ToHex()
    {
        static int ToByte(float v) => (int)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"#{ToByte(R):X2}{ToByte(G):X2}{ToByte(B):X2}");
    }

    public static bool TryParseHex(string? hex, out RgbaColor color)
    {
        color = Black;
        if (hex is null)
        {
            return false;
        }

        var s = hex.Trim().TrimStart('#');
        if (s.Length != 6 || !int.TryParse(s, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        color = FromBytes((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }
}

/// <summary>One input as stored in a project and exchanged with the UI.</summary>
public sealed record InputDescriptor
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public InputKind Kind { get; init; }

    /// <summary>User-given name (any language).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>For <see cref="InputKind.Color"/>.</summary>
    public RgbaColor Color { get; init; } = RgbaColor.Black;

    /// <summary>For files: path relative to the project folder when possible, else absolute.</summary>
    public string? Path { get; init; }

    /// <summary>Video files: loop at end.</summary>
    public bool Loop { get; init; } = true;

    /// <summary>Video files: start playing when taken to Program, pause and rewind when leaving.</summary>
    public bool AutoPlay { get; init; } = true;
}
