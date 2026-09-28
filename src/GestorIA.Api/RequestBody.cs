namespace GestorIA.Api;

// A request body read whole into memory, up to a limit, for the endpoints that take a file as their body.
public static class RequestBody
{
    // Reads at most maxBytes + 1 bytes: one more than the limit is enough to know the body is over it. Null when it is.
    public static async Task<byte[]?> ReadAtMostAsync(HttpRequest request, int maxBytes)
    {
        if (request.ContentLength > maxBytes)
        {
            return null;
        }

        var buffer = new byte[maxBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await request.Body.ReadAsync(buffer.AsMemory(length), request.HttpContext.RequestAborted)) > 0)
        {
            length += read;
        }

        return length > maxBytes ? null : buffer[..length];
    }
}
