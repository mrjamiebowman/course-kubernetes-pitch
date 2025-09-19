// Program.cs
// .NET 8+ minimal API demonstrating SAFE, BOUNDED memory pressure testing.
// Run:  dotnet run
// Try:  GET /stats, POST /alloc?mb=64, POST /free, POST /gc

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// In-memory store of allocated chunks so we can free them.
var allocations = new ConcurrentBag<byte[]>();

// Safety rails — adjust but keep conservative.
const int MaxAllocPerRequestMb = 128;     // hard cap per request
const int MaxTotalAllocatedMb = 512;    // hard cap total held by the app

long TotalAllocatedBytes() => allocations.Sum(a => (long)a.Length);
long MbToBytes(int mb) => (long)mb * 1024 * 1024;

// Basic stats endpoint.
app.MapGet("/stats", () =>
{
    var proc = Process.GetCurrentProcess();
    var gcInfo = new
    {
        GCSettings = new
        {
            IsServerGC = System.Runtime.GCSettings.IsServerGC,
            LatencyMode = System.Runtime.GCSettings.LatencyMode.ToString()
        },
        GCCollections = new
        {
            Gen0 = GC.CollectionCount(0),
            Gen1 = GC.CollectionCount(1),
            Gen2 = GC.CollectionCount(2)
        }
    };

    return Results.Ok(new
    {
        Timestamp = DateTimeOffset.UtcNow,
        OS = RuntimeInformation.OSDescription,
        Process = new
        {
            WorkingSetBytes = proc.WorkingSet64,
            PrivateMemoryBytes = proc.PrivateMemorySize64,
            PagedMemoryBytes = proc.PagedMemorySize64,
        },
        GC = gcInfo,
        AppAllocations = new
        {
            HeldBytes = TotalAllocatedBytes(),
            HeldMB = Math.Round(TotalAllocatedBytes() / 1024d / 1024d, 2),
            Chunks = allocations.Count
        }
    });
});

// Allocate bounded memory (adds zeroed arrays to the bag).
// ?mb=64
app.MapGet("/alloc", (int mb) =>
{
    if (mb <= 0) return Results.BadRequest("mb must be > 0");
    if (mb > MaxAllocPerRequestMb) return Results.BadRequest($"Per-request limit is {MaxAllocPerRequestMb} MB.");

    var requestedBytes = MbToBytes(mb);
    var newTotal = TotalAllocatedBytes() + requestedBytes;
    if (newTotal > MbToBytes(MaxTotalAllocatedMb))
        return Results.BadRequest($"Total allocation would exceed {MaxTotalAllocatedMb} MB cap.");

    try
    {
        // Allocate in 8MB chunks to avoid large object heap fragmentation spikes.
        const int chunkMb = 8;
        int remainingMb = mb;
        while (remainingMb > 0)
        {
            int takeMb = Math.Min(chunkMb, remainingMb);
            var buffer = GC.AllocateArray<byte>((int)MbToBytes(takeMb), pinned: false);
            allocations.Add(buffer);
            remainingMb -= takeMb;
        }

        return Results.Ok(new
        {
            AllocatedThisRequestMB = mb,
            TotalHeldMB = Math.Round(TotalAllocatedBytes() / 1024d / 1024d, 2),
            Chunks = allocations.Count
        });
    } catch (OutOfMemoryException)
    {
        // We kept caps, but in very tight environments OOM could still occur.
        return Results.StatusCode(507); // Insufficient Storage / memory pressure
    }
});

// Free all held allocations.
app.MapGet("/free", () =>
{
    var freed = 0L;
    while (allocations.TryTake(out var arr))
    {
        freed += arr.Length;
        // Let GC reclaim; no explicit free needed.
    }
    return Results.Ok(new
    {
        FreedMB = Math.Round(freed / 1024d / 1024d, 2),
        NowHeldMB = Math.Round(TotalAllocatedBytes() / 1024d / 1024d, 2)
    });
});

// Hint the GC to collect now (use sparingly; mostly for lab testing).
app.MapGet("/gc", () =>
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return Results.Ok(new { ForcedGC = true, Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2) });
});

app.Run();