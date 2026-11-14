namespace AmpersandOfTruth.Web.Features.WikiDiff;

internal enum EditOp : byte { Equal, Delete, Insert }

/// <summary>
/// Myers' O((N+M)·D) shortest edit script over interned ids. The common prefix and suffix are stripped first, so a
/// typical revision costs about as much as its changed region. Returns <c>null</c> when the script needs more than
/// <paramref name="maxD"/> edits; callers then fall back to "remove everything, add everything".
/// </summary>
internal static class MyersDiff
{
    public static List<EditOp>? Diff(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int maxD)
    {
        int n = a.Length, m = b.Length;
        int pre = 0;
        while (pre < n && pre < m && a[pre] == b[pre]) pre++;
        int suf = 0;
        while (suf < n - pre && suf < m - pre && a[n - 1 - suf] == b[m - 1 - suf]) suf++;

        var ops = new List<EditOp>(n + m);
        for (int i = 0; i < pre; i++) ops.Add(EditOp.Equal);
        if (!Core(a.Slice(pre, n - pre - suf), b.Slice(pre, m - pre - suf), maxD, ops)) return null;
        for (int i = 0; i < suf; i++) ops.Add(EditOp.Equal);
        return ops;
    }

    private static bool Core(ReadOnlySpan<int> a, ReadOnlySpan<int> b, int maxD, List<EditOp> ops)
    {
        int n = a.Length, m = b.Length;
        if (n == 0 || m == 0)
        {
            for (int i = 0; i < n; i++) ops.Add(EditOp.Delete);
            for (int i = 0; i < m; i++) ops.Add(EditOp.Insert);
            return true;
        }

        int max = n + m;
        int limit = Math.Min(max, maxD);
        int offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();   // trace[d] = V before step d, for k in [-d, d] (index k + d)

        for (int d = 0; d <= limit; d++)
        {
            var snapshot = new int[2 * d + 1];
            Array.Copy(v, offset - d, snapshot, 0, 2 * d + 1);
            trace.Add(snapshot);

            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[x] == b[y]) { x++; y++; }
                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    Backtrack(trace, d, n, m, ops);
                    return true;
                }
            }
        }
        return false;
    }

    private static void Backtrack(List<int[]> trace, int dEnd, int n, int m, List<EditOp> ops)
    {
        var reversed = new List<EditOp>(n + m);
        int x = n, y = m;
        for (int d = dEnd; d > 0; d--)
        {
            var prev = trace[d];
            int k = x - y;
            int prevK = k == -d || (k != d && prev[k - 1 + d] < prev[k + 1 + d]) ? k + 1 : k - 1;
            int prevX = prev[prevK + d];
            int prevY = prevX - prevK;
            while (x > prevX && y > prevY) { reversed.Add(EditOp.Equal); x--; y--; }
            reversed.Add(x == prevX ? EditOp.Insert : EditOp.Delete);
            x = prevX;
            y = prevY;
        }
        while (x > 0 && y > 0) { reversed.Add(EditOp.Equal); x--; y--; }
        for (int i = reversed.Count - 1; i >= 0; i--) ops.Add(reversed[i]);
    }
}
