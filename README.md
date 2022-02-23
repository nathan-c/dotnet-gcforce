# dotnet-gcforce

`dotnet-gcforce` is a tool that borrows heavily from [`dotnet-gcdump`](https://docs.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-gcdump) to force garbage collection on a .NET process running locally. It has the same caveats as running `dotnet-gcdump` but can be useful for investigating heap growth issues.

## Installation

```
dotnet tool install --global dotnet-gcforce
```

## Execution

```
dotnet-gcforce <pid>
```

## Development Notes

The LongRunningProcess tool can be used to test dotnet-gcforce against. It will print its own PID and then sit there logging when the number of Gen2 GC's changes.
