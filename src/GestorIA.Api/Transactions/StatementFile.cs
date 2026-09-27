using System.Text;
using GestorIA.Domain.Interfaces;
using Microsoft.Net.Http.Headers;

namespace GestorIA.Api.Transactions;

// The boundary between an uploaded statement and its text: the size limit, the refusal of what is not text, and the decoding.
public static class StatementFile
{
    // A year of a personal account is a few thousand lines, well under 200 KB.
    public const int MaxBytes = 2 * 1024 * 1024;

    // A CSV file as browsers label it: text/csv, but Windows labels .csv application/vnd.ms-excel when Excel is installed, and
    // some systems text/plain. Anything else, a form upload or JSON among them, is not a statement file.
    public static readonly string[] MediaTypes = ["text/csv", "text/plain", "application/vnd.ms-excel"];

    public static bool IsAcceptedMediaType(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var parsed)
        && MediaTypes.Contains(parsed.MediaType.ToString(), StringComparer.OrdinalIgnoreCase);

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    // Windows-1252 is not built into .NET; its provider ships with the runtime and is registered once, here.
    private static readonly Encoding Windows1252 = CodePages();

    // Reads at most MaxBytes + 1 bytes: one more than the limit is enough to know the file is over it. Null when it is.
    public static async Task<byte[]?> ReadAsync(HttpRequest request)
    {
        if (request.ContentLength > MaxBytes)
        {
            return null;
        }

        var buffer = new byte[MaxBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(length), request.HttpContext.RequestAborted)) > 0)
        {
            length += read;
        }

        return length > MaxBytes ? null : buffer[..length];
    }

    // UTF-8, with or without its byte-order mark, else Windows-1252, the code page of Spanish bank exports that are not UTF-8.
    public static string Decode(byte[] bytes)
    {
        if (bytes is [0x50, 0x4B, 0x03, 0x04, ..])
        {
            throw Refused("The file is an XLSX workbook or another ZIP archive; export the statement as CSV.");
        }

        if (bytes.AsSpan().Contains((byte)0))
        {
            throw Refused("The file is not text; export the statement as CSV.");
        }

        var body = bytes.AsSpan(bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0);
        try
        {
            return StrictUtf8.GetString(body);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252.GetString(body);
        }
    }

    private static InvalidStatementException Refused(string reason) => new(new Dictionary<string, string[]> { ["file"] = [reason] });

    private static Encoding CodePages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
