// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

namespace GCForce;

public static class EventPipeDotNetGcRunner
{
    private static volatile bool _eventPipeDataPresent;
    private static volatile bool _gcForceComplete;

    /// <summary>
    ///     Given a factory for creating an EventPipe session with the appropriate provider and keywords turned on,
    ///     generate a GCHeapDump using the resulting events.  The correct keywords and provider name
    ///     are given as input to the Func eventPipeEventSourceFactory.
    /// </summary>
    /// <param name="ct"></param>
    /// <param name="processId"></param>
    /// <param name="log"></param>
    /// <param name="timeout"></param>
    /// <returns></returns>
    public static bool ForceGc(CancellationToken ct, int processId, TextWriter log, int timeout)
    {
        var start = DateTime.Now;
        var getElapsed = () => DateTime.Now - start;

        try
        {
            var lastEventPipeUpdate = getElapsed();
            // Start the providers and trigger the GCs.  
            log.WriteLine("{0,5:n1}s: Requesting a .NET GC", getElapsed().TotalSeconds);

            using var gcForceSession = new EventPipeSessionController(processId,
                new List<EventPipeProvider>
                {
                    new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose,
                        (long)(ClrTraceEventParser.Keywords.GCHeapCollect | ClrTraceEventParser.Keywords.GC))
                });
            log.WriteLine("{0,5:n1}s: gcforce EventPipe Session started", getElapsed().TotalSeconds);

            var gcNum = -1;

            gcForceSession.Source.Clr.GCStart += delegate(GCStartTraceData data)
            {
                if (data.ProcessID != processId) return;

                _eventPipeDataPresent = true;

                if (gcNum < 0 && data.Depth == 2 && data.Type != GCType.BackgroundGC)
                {
                    gcNum = data.Count;
                    log.WriteLine("{0,5:n1}s: .NET Dump Started...", getElapsed().TotalSeconds);
                }
            };

            gcForceSession.Source.Clr.GCStop += delegate(GCEndTraceData data)
            {
                if (data.ProcessID != processId) return;

                if (data.Count == gcNum)
                {
                    log.WriteLine("{0,5:n1}s: .NET GC Complete.", getElapsed().TotalSeconds);
                    _gcForceComplete = true;
                }
            };

            gcForceSession.Source.Clr.GCBulkNode += delegate(GCBulkNodeTraceData data)
            {
                if (data.ProcessID != processId) return;

                _eventPipeDataPresent = true;

                if ((getElapsed() - lastEventPipeUpdate).TotalMilliseconds > 500)
                    log.WriteLine("{0,5:n1}s: Making GC Heap Progress...", getElapsed().TotalSeconds);

                lastEventPipeUpdate = getElapsed();
            };

            // Set up a separate thread that will listen for EventPipe events coming back telling us we succeeded. 
            var readerTask = Task.Run(() =>
            {
                // cancelled before we began
                if (ct.IsCancellationRequested)
                    return;
                log.WriteLine("{0,5:n1}s: Starting to process events", getElapsed().TotalSeconds);
                gcForceSession.Source.Process();
                log.WriteLine("{0,5:n1}s: EventPipe Listener dying", getElapsed().TotalSeconds);
            }, ct);

            for (;;)
            {
                if (ct.IsCancellationRequested)
                {
                    log.WriteLine("{0,5:n1}s: Cancelling...", getElapsed().TotalSeconds);
                    break;
                }

                if (readerTask.Wait(100)) break;

                if (!_eventPipeDataPresent && getElapsed().TotalSeconds > 5) // Assume it started within 5 seconds.  
                {
                    log.WriteLine("{0,5:n1}s: Assume no .NET Heap", getElapsed().TotalSeconds);
                    break;
                }

                if (getElapsed().TotalSeconds > timeout) // Time out after `timeout` seconds. defaults to 30s.
                {
                    log.WriteLine("{0,5:n1}s: Timed out after {1} seconds", getElapsed().TotalSeconds, timeout);
                    break;
                }

                if (_gcForceComplete) break;
            }

            var stopTask = Task.Run(() =>
            {
                log.WriteLine("{0,5:n1}s: Shutting down gcforce EventPipe session", getElapsed().TotalSeconds);
                gcForceSession.EndSession();
                log.WriteLine("{0,5:n1}s: gcforce EventPipe session shut down", getElapsed().TotalSeconds);
            }, ct);

            try
            {
                while (!Task.WaitAll(new[] { readerTask, stopTask }, 1000))
                    log.WriteLine("{0,5:n1}s: still reading...", getElapsed().TotalSeconds);
            }
            catch (AggregateException ae) // no need to throw if we're just cancelling the tasks
            {
                foreach (var e in ae.Flatten().InnerExceptions)
                    if (!(e is TaskCanceledException))
                        throw;
            }

            log.WriteLine("{0,5:n1}s: gcforce EventPipe Session closed", getElapsed().TotalSeconds);

            if (ct.IsCancellationRequested)
                return false;
        }
        catch (Exception e)
        {
            log.WriteLine($"{getElapsed().TotalSeconds,5:n1}s: [Error] Exception during gcforce: {e.ToString()}");
        }

        log.WriteLine("[{0,5:n1}s: Done Forcing .NET GC success={1}]", getElapsed().TotalSeconds, _gcForceComplete);

        return _gcForceComplete;
    }
}

internal class EventPipeSessionController : IDisposable
{
    private readonly List<EventPipeProvider> _providers;
    private readonly EventPipeSession _session;

    public EventPipeSessionController(int pid, List<EventPipeProvider> providers, bool requestRundown = true)
    {
        _providers = providers;
        var client = new DiagnosticsClient(pid);
        _session = client.StartEventPipeSession(providers, requestRundown, 1024);
        Source = new EventPipeEventSource(_session.EventStream);
    }

    public IReadOnlyList<EventPipeProvider> Providers => _providers.AsReadOnly();
    public EventPipeEventSource Source { get; }

    public void EndSession()
    {
        _session.Stop();
    }

    #region IDisposable Support

    private bool _disposedValue; // To detect redundant calls

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _session.Dispose();
                Source.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
    }

    #endregion
}