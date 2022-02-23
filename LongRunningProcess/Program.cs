// See https://aka.ms/new-console-template for more information

using System.Diagnostics;

using (var p = Process.GetCurrentProcess())
{
    Console.WriteLine($"Starting. PID: {p.Id}");
}

var run = true;

Console.CancelKeyPress += (_, _) => run = false;

var lastGen2Count = 0;

while (run)
{
    var gen2Count = GC.CollectionCount(2);
    if (gen2Count != lastGen2Count)
    {
        Console.WriteLine($"Gen2 Count: {gen2Count}");
        lastGen2Count = gen2Count;
    }

    Thread.Sleep(1000);
}

Console.WriteLine("Finished");