using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FanucLsp.Lsp;
using Sprache;

namespace FanucLsp.JsonRPC;

/// <summary>
/// JSON-RPC and LSP error codes
/// </summary>
public enum ErrorCodes
{
    ParseError = -32700,
    InvalidRequest = -32600,
    MethodNotFound = -32601,
    InvalidParams = -32602,
    InternalError = -32603,

    ServerErrorStart = -32099,
    ServerErrorEnd = -32000,

    RequestCancelled = -32800,
    ContentModified = -32801
}

/// <summary>
/// Base message interface following JSON-RPC 2.0 spec
/// </summary>
public abstract class Message
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";
}

/// <summary>
/// Request message sent from client to server
/// </summary>
public class RequestMessage : Message
{
    [JsonPropertyName("id")]
    public int Id { get; set; } = 0;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
}

/// <summary>
/// Response message sent from server to client
/// </summary>
[JsonDerivedType(typeof(InitializeResponse))]
[JsonDerivedType(typeof(TextDocumentHoverResponse))]
[JsonDerivedType(typeof(TextDocumentDefinitionResponse))]
[JsonDerivedType(typeof(TextDocumentCodeActionResponse))]
[JsonDerivedType(typeof(TextDocumentCompletionResponse))]
[JsonDerivedType(typeof(PublishDiagnosticsNotification))]
[JsonDerivedType(typeof(TextDocumentFormattingResponse))]
[JsonDerivedType(typeof(TextDocumentReferencesResponse))]
[JsonDerivedType(typeof(ResponseError))]
public class ResponseMessage : Message
{
    [JsonPropertyName("id")]
    public int? Id { get; set; }
}

/// <summary>
/// Error response object
/// </summary>
public class ResponseError : ResponseMessage
{
    [JsonPropertyName("code")]
    public ErrorCodes Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

public class JsonRpcEncoder
{
    /// <summary>
    /// Frames a response as an LSP message. Content-Length counts UTF-8 *bytes*
    /// (per the LSP spec), so the body is serialized to bytes and measured there —
    /// counting chars would understate the length for any non-ASCII content.
    /// </summary>
    public static byte[] Encode(ResponseMessage message)
    {
        if (message == null)
        {
            throw new JsonRpcException(ErrorCodes.InternalError, "Null message cannot be encoded.");
        }

        var body = JsonSerializer.SerializeToUtf8Bytes(message);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return [.. header, .. body];
    }
}

public class JsonRpcReader
{
    /// <summary>
    /// Reads a single LSP message off the raw stream and returns its UTF-8 decoded
    /// JSON body, or <c>null</c> at end of stream. Framing is done at the byte level:
    /// Content-Length is a byte count, so we read exactly that many bytes before
    /// decoding. (A char-oriented <see cref="StreamReader"/> cannot do this correctly
    /// once the body contains multi-byte characters — it over-reads into the next frame.)
    /// </summary>
    public static string? ReadMessage(Stream stream)
    {
        var headerBytes = new List<byte>();
        while (true)
        {
            var b = stream.ReadByte();
            if (b == -1)
            {
                // Clean EOF between messages; anything buffered here is a truncated header.
                return headerBytes.Count == 0 ? null : throw new JsonRpcException(
                    ErrorCodes.ParseError, "Stream ended mid-header.");
            }

            headerBytes.Add((byte)b);
            var n = headerBytes.Count;
            if (n >= 4
                && headerBytes[n - 4] == (byte)'\r' && headerBytes[n - 3] == (byte)'\n'
                && headerBytes[n - 2] == (byte)'\r' && headerBytes[n - 1] == (byte)'\n')
            {
                break;
            }
        }

        var headers = Encoding.ASCII.GetString([.. headerBytes]);
        var contentLength = JsonRpcDecoder.HeaderParser.Parse(headers);

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var read = stream.Read(body, offset, contentLength - offset);
            if (read == 0)
            {
                throw new JsonRpcException(ErrorCodes.ParseError, "Stream ended mid-body.");
            }

            offset += read;
        }

        return Encoding.UTF8.GetString(body, 0, offset);
    }
}

public class JsonRpcDecoder
{
    public static Parser<int> HeaderParser =
        from ident in Parse.String("Content-Length: ")
        from contentLength in Parse.Number.Select(int.Parse)
        select contentLength;

    public static TMessage? Decode<TMessage>(string json)
        => JsonSerializer.Deserialize<TMessage>(json);
}

public class JsonRpcException(ErrorCodes code, string message) : Exception(message)
{
    public ErrorCodes Code { get; set; } = code;
}
