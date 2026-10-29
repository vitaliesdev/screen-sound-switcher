namespace MonitorTv.Core.Monitors;

/// <summary>
/// The safety net for "Windows changed my screens by itself". When a monitor disappears (a TV turned off, a cable pulled)
/// Windows picks a layout of its own for the screens that are left, often turning on one that was off. This decides whether
/// to put the layout the person chose back, and builds it, without touching anything when nothing needs to change:
/// <list type="bullet">
/// <item>it only acts when a monitor disappeared; a monitor that appears (the TV switched on on purpose) is left alone;</item>
/// <item>a layout change with the same monitors connected is the person's own (Win+P, Display settings): never undone;</item>
/// <item>when Windows already left the screens as they should be, there is no change to make, so no flicker;</item>
/// <item>only monitors that are still connected are asked for.</item>
/// </list>
/// </summary>
public static class DisplayGuard
{
    /// <summary>Device paths of the monitors Windows reports as connected right now, on or off.</summary>
    public static HashSet<string> ConnectedPaths() =>
        new(LayoutBuilder.Available().Select(m => m.Target.DevicePath), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The layout to put back, or null when there is nothing to do.
    /// </summary>
    /// <param name="intended">The layout the person chose (the last profile applied, or the one the app started with).</param>
    /// <param name="now">What Windows has now.</param>
    /// <param name="before">Monitors connected before the change.</param>
    /// <param name="after">Monitors connected after it.</param>
    public static DisplayConfig.Layout? Plan(DisplayConfig.Layout intended, DisplayConfig.Layout now,
        IReadOnlySet<string> before, IReadOnlySet<string> after)
    {
        // only the disappearance of a monitor is Windows deciding for the person
        if (!before.Any(path => !after.Contains(path))) return null;

        var chosen = Positions(DisplayIdentity.Refresh(intended));
        if (chosen == null) return null;

        var connected = LayoutBuilder.Available();
        var wanted = connected.Where(m => chosen.ContainsKey(m.Target.DevicePath)).ToList();
        if (wanted.Count == 0) return null; // none of the chosen screens is here any more: nothing sensible to restore

        // Windows needs the primary screen at 0,0: keep the chosen one there, or else the top-left one
        var anchor = wanted.Select(m => chosen[m.Target.DevicePath])
            .OrderBy(p => p.X != 0 || p.Y != 0).ThenBy(p => p.X).ThenBy(p => p.Y).First();
        var layout = LayoutBuilder.Build(
            wanted.Select(m => (m, chosen[m.Target.DevicePath].X - anchor.X, chosen[m.Target.DevicePath].Y - anchor.Y)).ToList(), intended);

        return SameScreens(layout, now) ? null : layout;
    }

    /// <summary>Device path -> top-left position of every monitor that is on in a layout; null when the layout has no device paths.</summary>
    private static Dictionary<string, (int X, int Y)>? Positions(DisplayConfig.Layout layout)
    {
        if (layout.Targets is not { Count: > 0 } targets) return null;
        var result = new Dictionary<string, (int X, int Y)>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in LayoutEditor.Read(layout))
        {
            var target = targets.FirstOrDefault(t => t.TargetId == box.TargetId);
            if (target != null) result[target.DevicePath] = (box.X, box.Y);
        }
        return result.Count == 0 ? null : result;
    }

    /// <summary>Same monitors on, in the same places (compared as the layouts are, not byte by byte).</summary>
    public static bool SameScreens(DisplayConfig.Layout a, DisplayConfig.Layout b)
    {
        var pa = Positions(a);
        var pb = Positions(b);
        if (pa == null || pb == null || pa.Count != pb.Count) return false;
        return pa.All(kv => pb.TryGetValue(kv.Key, out var other) && other == kv.Value);
    }
}
