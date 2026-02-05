using Microsoft.AspNetCore.SignalR;
using System.Runtime.CompilerServices;

public class HubErrorFilter : IHubFilter
{
    private readonly ILogger<HubErrorFilter> _logger;
    private const string GlobalErrorMessage = "System: An internal server error occurred. Connection might be unstable.";

    public HubErrorFilter(ILogger<HubErrorFilter> logger)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        try
        {
            return await next(invocationContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Hub Method: {Method}", invocationContext.HubMethodName);
            await SendErrorToClient(invocationContext.Hub);
            return null;
        }
    }

    public async IAsyncEnumerable<object?> OnStreamMethodServerEnumerator(
        HubInvocationContext invocationContext,
        IAsyncEnumerable<object?> enumerator,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var e = enumerator.GetAsyncEnumerator(cancellationToken);
        bool isFaulted = false;

        while (true)
        {
            object? item = null;
            try
            {
                if (!await e.MoveNextAsync()) break;
                item = e.Current;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during streaming in method: {Method}", invocationContext.HubMethodName);
                isFaulted = true;
            }

            if (isFaulted)
            {
                yield return (object?)GlobalErrorMessage;
                yield break;
            }

            if(item != null)
                yield return item;
        }
    }

    private async Task SendErrorToClient(Hub hub)
    {
        await hub.Clients.Caller.SendAsync("ReceiveChunk", GlobalErrorMessage);
    }
}