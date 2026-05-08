namespace Terminal.Maia;

public sealed class MaiaRouter
{
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<IMaiaTransport> transports;
    private readonly Dictionary<string, HealthStatus> availability = new();
    private readonly Dictionary<string, string> handleToTransport = new();
    private readonly object gate = new();
    private DateTime lastProbeAt = DateTime.MinValue;
    private string? forced;

    public MaiaRouter(IReadOnlyList<IMaiaTransport> transports)
    {
        this.transports = transports.OrderBy(t => t.Tier).ToArray();
    }

    public IReadOnlyList<IMaiaTransport> Transports => transports;

    public async Task ProbeAllAsync(CancellationToken ct)
    {
        var probes = await Task.WhenAll(transports.Select(t => t.HealthCheckAsync(ct)));
        lock (gate)
        {
            availability.Clear();
            for (int i = 0; i < transports.Count; i++)
                availability[transports[i].Name] = probes[i];
            lastProbeAt = DateTime.UtcNow;
        }
    }

    private async Task MaybeReprobeAsync(CancellationToken ct)
    {
        bool needs;
        lock (gate) { needs = DateTime.UtcNow - lastProbeAt >= ProbeInterval; }
        if (needs) await ProbeAllAsync(ct);
    }

    private List<IMaiaTransport> ActiveSnapshot()
    {
        lock (gate)
        {
            if (forced is not null)
            {
                var t = transports.FirstOrDefault(x => x.Name == forced);
                if (t is null) throw new ArgumentException($"Unknown transport name: {forced}");
                if (availability.TryGetValue(forced, out var h) && !h.Available)
                    throw new TransportUnavailable($"Forced transport '{forced}' is unavailable: {h.Reason}");
                return new List<IMaiaTransport> { t };
            }
            return transports
                .Where(t => availability.TryGetValue(t.Name, out var h) && h.Available)
                .ToList();
        }
    }

    public void ForceTier(string name)
    {
        if (transports.All(t => t.Name != name))
            throw new ArgumentException($"Unknown transport name: {name}");
        lock (gate) forced = name;
    }

    public void ClearForcedTier()
    {
        lock (gate) forced = null;
    }

    public async Task<SendResult> SendAsync(string prompt, string sentinel, CancellationToken ct)
    {
        await MaybeReprobeAsync(ct);
        var active = ActiveSnapshot();
        if (active.Count == 0)
            throw new TransportUnavailable(BuildExhaustedMessage());

        Exception? last = null;
        foreach (var t in active)
        {
            try
            {
                var r = await t.SendAsync(prompt, sentinel, ct);
                lock (gate) handleToTransport[r.Handle] = t.Name;
                return r;
            }
            catch (TransportUnavailable ex)
            {
                last = ex;
                lock (gate) availability[t.Name] = new HealthStatus(false, t.Tier, t.Name, 0, ex.Message);
            }
        }
        throw new TransportUnavailable($"All Maia transports unavailable. Last reason: {last?.Message}");
    }

    public async Task<StatusResult> StatusAsync(string handle, CancellationToken ct)
    {
        await MaybeReprobeAsync(ct);
        string? transportName;
        lock (gate) handleToTransport.TryGetValue(handle, out transportName);

        var t = transports.FirstOrDefault(x => x.Name == transportName)
            ?? ActiveSnapshot().FirstOrDefault()
            ?? throw new TransportUnavailable(BuildExhaustedMessage());
        return await t.StatusAsync(handle, ct);
    }

    public async Task ResetAsync(CancellationToken ct)
    {
        lock (gate) handleToTransport.Clear();
        foreach (var t in transports)
        {
            try { await t.ResetAsync(ct); }
            catch (TransportUnavailable) { /* not active anyway */ }
        }
    }

    private string BuildExhaustedMessage()
    {
        lock (gate)
        {
            var reasons = transports
                .Select(t => availability.TryGetValue(t.Name, out var h) ? $"{t.Name}: {h.Reason ?? "ok"}" : $"{t.Name}: unprobed")
                .ToArray();
            return $"All Maia transports unavailable. {string.Join(" | ", reasons)}";
        }
    }
}
