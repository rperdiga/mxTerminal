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

    /// <summary>
    /// First active introspection-capable transport, or <c>null</c> if none
    /// of the currently-available transports implement <see cref="IMaiaIntrospection"/>.
    /// Used by <see cref="MaiaActions.BusyAsync"/> / <see cref="MaiaActions.NewChatAsync"/>.
    /// </summary>
    public IMaiaIntrospection? GetIntrospection()
    {
        // Prefer the first ACTIVE introspection-capable transport — same
        // tier-priority order ActiveSnapshot uses for routed sends. Falls
        // through to the lowest-tier introspection transport even when the
        // probe hasn't run yet (early-call edge case during action-server
        // startup); calling code can still get a TransportUnavailable on
        // the actual JS eval if discovery fails.
        var active = ActiveSnapshot();
        var introspection = active.OfType<IMaiaIntrospection>().FirstOrDefault();
        if (introspection is not null) return introspection;
        return transports.OfType<IMaiaIntrospection>().FirstOrDefault();
    }

    /// <summary>
    /// Bridge-state snapshot for the <c>maia__health</c> tool — no traffic
    /// to Maia, just a read of the router's bookkeeping. The caller can
    /// compose this with <see cref="MaiaActions.BusyAsync"/> /
    /// <see cref="MaiaActions.PingAsync"/> to build a full health response.
    /// </summary>
    public RouterHealthSnapshot GetHealthSnapshot()
    {
        lock (gate)
        {
            var rows = transports.Select(t =>
            {
                if (availability.TryGetValue(t.Name, out var h))
                    return new TransportHealth(t.Name, t.Tier, h.Available, h.LatencyMs, h.Reason);
                return new TransportHealth(t.Name, t.Tier, false, 0.0, "unprobed");
            }).ToArray();
            DateTimeOffset? probeAt = lastProbeAt == DateTime.MinValue
                ? null
                : new DateTimeOffset(lastProbeAt, TimeSpan.Zero);
            return new RouterHealthSnapshot(probeAt, rows, handleToTransport.Count, forced);
        }
    }

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
        bool routerHadBinding;
        lock (gate)
        {
            routerHadBinding = handleToTransport.TryGetValue(handle, out transportName);
        }

        var t = transports.FirstOrDefault(x => x.Name == transportName)
            ?? ActiveSnapshot().FirstOrDefault()
            ?? throw new TransportUnavailable(BuildExhaustedMessage());
        var result = await t.StatusAsync(handle, ct);

        // v4.2.0 (per reviewer C6): translate UnknownHandle into Lost=true
        // ONLY when the router itself had previously bound this handle. A
        // genuinely-unknown handle (caller typo, never sent) bubbles up as
        // an Unknown-handle exception. The transport returns UnknownHandle
        // as a structured signal; this is the layer that decides.
        if (result.UnknownHandle)
        {
            if (routerHadBinding)
            {
                return result with { Lost = true, UnknownHandle = false };
            }
            throw new TransportUnavailable($"Unknown handle: {handle}");
        }
        return result;
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
