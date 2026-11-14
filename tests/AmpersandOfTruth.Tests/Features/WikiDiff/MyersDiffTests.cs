using AmpersandOfTruth.Web.Features.WikiDiff;

namespace AmpersandOfTruth.Tests.Features.WikiDiff;

public class MyersDiffTests
{
    [Fact]
    public void Edit_script_rebuilds_the_new_sequence_and_is_as_short_as_possible()
    {
        var rng = new Random(29);
        for (int round = 0; round < 500; round++)
        {
            var a = Enumerable.Range(0, rng.Next(0, 25)).Select(_ => rng.Next(0, 5)).ToArray();
            var b = Enumerable.Range(0, rng.Next(0, 25)).Select(_ => rng.Next(0, 5)).ToArray();

            var ops = MyersDiff.Diff(a, b, maxD: 1000)!;

            var rebuilt = new List<int>();
            int i = 0, j = 0;
            foreach (var op in ops)
            {
                switch (op)
                {
                    case EditOp.Equal:
                        Assert.Equal(a[i], b[j]);
                        rebuilt.Add(a[i]);
                        i++;
                        j++;
                        break;
                    case EditOp.Delete: i++; break;
                    case EditOp.Insert: rebuilt.Add(b[j++]); break;
                }
            }
            Assert.Equal((a.Length, b.Length), (i, j));
            Assert.Equal(b, rebuilt);
            Assert.Equal(Lcs(a, b), ops.Count(o => o == EditOp.Equal));
        }
    }

    [Fact]
    public void Gives_up_beyond_the_edit_budget()
    {
        int[] a = [1, 2, 3, 4, 5, 6];
        int[] b = [7, 8, 9, 10, 11, 12];
        Assert.Null(MyersDiff.Diff(a, b, maxD: 5));
        Assert.NotNull(MyersDiff.Diff(a, b, maxD: 12));
    }

    private static int Lcs(int[] a, int[] b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
            dp[i, j] = a[i - 1] == b[j - 1] ? dp[i - 1, j - 1] + 1 : Math.Max(dp[i - 1, j], dp[i, j - 1]);
        return dp[a.Length, b.Length];
    }
}
