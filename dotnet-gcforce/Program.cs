using System.CommandLine;
using GCForce;

const int DEFAULT_TIMEOUT = 30;

var pidArgument = new Argument<int>("pid", "Process ID to force GC on");
var timeoutOption = new Option<int>(
    new[] { "-t", "--timeout" },
    description:
    $"Give up after this many seconds.",
    getDefaultValue: () => DEFAULT_TIMEOUT);

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, _) => cts.Cancel();

var cmd = new RootCommand("Tool to force garbage collection on a .NET process running locally.")
{
    pidArgument,
    timeoutOption
};

cmd.SetHandler(
    (int pid, int timeout) => { EventPipeDotNetGcRunner.ForceGc(cts.Token, pid, Console.Out, timeout); },
    pidArgument, timeoutOption);

return cmd.Invoke(args);