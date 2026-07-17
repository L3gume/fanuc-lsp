using System.Text;
using FanucLsp.JsonRPC;

namespace FanucLsp.Tests;

/// <summary>
/// Framing must count UTF-8 *bytes*, per the LSP spec, not .NET chars.
/// A body containing multi-byte characters (accents, °, …) is where the
/// byte/char distinction bites: the char count is smaller than the byte count.
/// </summary>
public class JsonRpcFramingTests
{
    // Contains multi-byte UTF-8 chars: "é" (2 bytes) and "°" (2 bytes).
    private const string MultiByteBody = "{\"method\":\"test\",\"comment\":\"Vitesse 100°/s café\"}";

    private static byte[] Frame(string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {bodyBytes.Length}\r\n\r\n");
        return [.. header, .. bodyBytes];
    }

    [Fact]
    public void ReadMessage_body_with_multibyte_chars_does_not_overread_into_next_frame()
    {
        var body1 = MultiByteBody;
        var body2 = "{\"method\":\"second\"}";

        using var stream = new MemoryStream([.. Frame(body1), .. Frame(body2)]);

        var first = JsonRpcReader.ReadMessage(stream);
        var second = JsonRpcReader.ReadMessage(stream);

        Assert.Equal(body1, first);
        Assert.Equal(body2, second);
    }

    [Fact]
    public void ReadMessage_returns_null_at_end_of_stream()
    {
        using var stream = new MemoryStream(Frame("{\"method\":\"only\"}"));

        Assert.NotNull(JsonRpcReader.ReadMessage(stream));
        Assert.Null(JsonRpcReader.ReadMessage(stream));
    }

    [Fact]
    public void Encode_content_length_counts_utf8_bytes_not_chars()
    {
        var response = new ResponseError
        {
            Id = 1,
            Code = ErrorCodes.InternalError,
            Message = "Température 100°C — café"
        };

        var framed = JsonRpcEncoder.Encode(response);
        var text = Encoding.UTF8.GetString(framed);

        var separator = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var declaredLength = int.Parse(text["Content-Length: ".Length..separator]);
        var actualBodyBytes = Encoding.UTF8.GetByteCount(text[(separator + 4)..]);

        Assert.Equal(actualBodyBytes, declaredLength);
    }
}