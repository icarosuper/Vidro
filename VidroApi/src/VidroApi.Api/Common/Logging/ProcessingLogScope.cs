using Serilog.Context;

namespace VidroApi.Api.Common.Logging;

public static class ProcessingLogScope
{
    public static IDisposable Begin(string processType, string? correlationId = null, string? methodName = null)
    {
        var disposables = new List<IDisposable>(3)
        {
            LogContext.PushProperty(LoggingDefaults.ProcessTypeProperty, processType)
        };

        if (correlationId is not null)
            disposables.Add(LogContext.PushProperty(LoggingDefaults.CorrelationIdProperty, correlationId));

        if (methodName is not null)
            disposables.Add(LogContext.PushProperty(LoggingDefaults.MethodNameProperty, methodName));

        return new CompositeDisposable(disposables);
    }

    private sealed class CompositeDisposable(IEnumerable<IDisposable> disposables) : IDisposable
    {
        private readonly IDisposable[] _disposables = disposables.ToArray();

        public void Dispose()
        {
            foreach (var d in _disposables)
                d.Dispose();
        }
    }
}
