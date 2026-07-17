using System.Text;
using System.Text.Json;
using FanucLsp.JsonRPC;
using FanucLsp.Lsp;

// Create log directory and file path
var logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
var logFilePath = Path.Combine(logDirectory, $"server_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

var stdin = Console.OpenStandardInput();
var stdout = Console.OpenStandardOutput();

// Log server start
LogMessage(logFilePath, $"Server started at {DateTime.Now}");

var server = new LspServer(logFilePath);

//while (!Debugger.IsAttached)
//{
//    Thread.Sleep(1000);
//}

while (true)
{
    try
    {
        // Read exactly one message off stdin at the byte level. Content-Length is a
        // UTF-8 byte count, so the body must be sliced by bytes (not chars) before
        // decoding — otherwise multi-byte content desyncs the stream for every
        // subsequent message.
        var json = JsonRpcReader.ReadMessage(stdin);
        if (json is null)
        {
            LogMessage(logFilePath, "Client closed the connection.");
            break;
        }

        await HandleRequest(server, json, stdout, logFilePath);
    }
    catch (Exception ex)
    {
        LogMessage(logFilePath, $"Unhandled Exception: {ex.Message}");
    }
}

static async Task HandleRequest(LspServer server, string json, Stream stdout, string logFilePath)
{
    var doc = JsonDocument.Parse(json);
    if (!doc.RootElement.TryGetProperty("method", out var method))
    {
        await WriteResponse(stdout, new ResponseError
        {
            Code = ErrorCodes.MethodNotFound,
            Message = "Failed to get request method"
        }, logFilePath);
    }

    LogMessage(logFilePath, $"RECEIVED: [{method}]");
    try
    {
        await server.HandleRequest(method.GetString()!, json, GetCallback(stdout, logFilePath)).ConfigureAwait(false);
    }
    catch (JsonRpcException ex)
    {
        LogMessage(logFilePath, $"An error occured while handling the request: {ex.Message}");
        var errorResponse = new ResponseError
        {
            Code = ex.Code,
            Message = ex.Message,
            Data = ex.Data
        };
        await WriteResponse(stdout, errorResponse, logFilePath);
    }
}

static Func<ResponseMessage, Task> GetCallback(Stream stdout, string logFilePath)
    => async response => await WriteResponse(stdout, response, logFilePath);

static async Task WriteResponse(Stream stdout, ResponseMessage response, string logFilePath)
{
    var bytes = JsonRpcEncoder.Encode(response);
    await stdout.WriteAsync(bytes);
    await stdout.FlushAsync();

    // Log the sent response
    LogMessage(logFilePath, $"SENT: {Encoding.UTF8.GetString(bytes)}");
}

static void LogMessage(string logFilePath, string message)
{
    try
    {
        File.AppendAllText(logFilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error logging message: {ex.Message}");
    }
}
