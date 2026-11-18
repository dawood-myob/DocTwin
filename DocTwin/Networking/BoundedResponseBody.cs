using System.Net.Http;

namespace DocTwin.Networking;

internal static class BoundedResponseBody
{
    public static async Task<byte[]> ReadAsync(HttpContent content, int maximumBytes, TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (timeout != Timeout.InfiniteTimeSpan)
            deadline.CancelAfter(timeout);
        try
        {
            await content.LoadIntoBufferAsync(maximumBytes, deadline.Token);
            return await content.ReadAsByteArrayAsync(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            throw new TimeoutException("The service response body did not complete within the configured timeout.");
        }
    }
}
