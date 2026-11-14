using System.Globalization;
using System.Text;
using AmpersandOfTruth.Web.Features.Articles;
using AmpersandOfTruth.Web.Features.Evidence;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace AmpersandOfTruth.Web.Components.Shared;

public partial class ArticleTimeline : ComponentBase
{
    private const double FullWidth = 900;
    private const double FullHeight = 242;
    private const double LeftPad = 36;
    private const double RightEdge = 892;
    private const double TopPad = 8;
    private const double BarLaneHeight = 140;
    private const double SizeLaneTop = 182;
    private const double SizeLaneHeight = 52;

    private const double SparkWidth = 300;
    private const double SparkHeight = 20;
    private const double SparkTop = 1;
    private const double SparkBarHeight = 18;

    private long _lastPageId;
    private MonthBucket? _anchorMonth;
    private bool _awaitingRangeEnd;

    [Parameter, EditorRequired]
    public ArticleAggregates Data { get; set; } = default!;

    [Parameter]
    public DateRange? Range { get; set; }

    [Parameter]
    public bool Spark { get; set; }

    [Parameter]
    public IReadOnlyList<IncidentMark>? Marks { get; set; }

    [Parameter]
    public EventCallback<DateRange> OnRangeSelected { get; set; }

    [Parameter]
    public string? HighlightedIncidentId { get; set; }

    private IReadOnlyList<MonthBucket> OrderedMonths =>
        Data.Months.Count <= 1
            ? Data.Months
            : Data.Months.OrderBy(m => m.Year).ThenBy(m => m.Month).ToList();

    private string SummaryText => ArticleTimelineService.Summary(Data);

    private ContestedPeriod? Contested => ArticleTimelineService.MostContested(Data);

    private DateRange AllPreset => ArticleTimelineService.AllRange(Data);

    private DateRange LastFiveYearsPreset => ArticleTimelineService.LastFiveYearsRange(Data);

    protected override void OnParametersSet()
    {
        if (Data is not null && Data.PageId != _lastPageId)
        {
            _lastPageId = Data.PageId;
            _anchorMonth = null;
            _awaitingRangeEnd = false;
        }
    }

    private LayoutMetrics ComputeLayout()
    {
        var months = OrderedMonths;
        double l = Spark ? 0 : LeftPad;
        double r = Spark ? SparkWidth : RightEdge;
        double t = Spark ? SparkTop : TopPad;
        double barH = Spark ? SparkBarHeight : BarLaneHeight;

        if (months.Count == 0)
        {
            return new LayoutMetrics(months, 0, 1, l, r, t, barH, r - l, 1, 1, 1);
        }

        int minIdx = MonthIdx(months[0].Year, months[0].Month);
        int maxIdx = MonthIdx(months[^1].Year, months[^1].Month);
        int spanMonths = Math.Max(1, maxIdx - minIdx + 1);
        double bw = (r - l) / spanMonths;

        int maxEdits = Math.Max(1, months.Max(m => m.NamedEdits + m.AnonEdits));
        int maxRemovals = Math.Max(1, months.Max(m => m.Removals));
        int maxBytes = Math.Max(1, months.Max(m => m.MaxBytes));

        return new LayoutMetrics(months, minIdx, spanMonths, l, r, t, barH, bw, maxEdits, maxRemovals, maxBytes);
    }

    private static IReadOnlyList<int> ComputeYearTicks(IReadOnlyList<MonthBucket> months)
    {
        if (months.Count == 0) return [];

        int firstYear = months[0].Year;
        int lastYear = months[^1].Year;
        int span = lastYear - firstYear;

        if (span == 0) return [firstYear];

        int step = span switch
        {
            <= 12 => 1,
            <= 24 => 2,
            <= 50 => 5,
            _ => 10,
        };

        int minIdx = MonthIdx(months[0].Year, months[0].Month);
        int maxIdx = MonthIdx(months[^1].Year, months[^1].Month);

        var ticks = new List<int>();
        int startYear = firstYear % step == 0 ? firstYear : firstYear + (step - (firstYear % step));
        for (int yr = startYear; yr <= lastYear; yr += step)
        {
            int idx = MonthIdx(yr, 1);
            if (idx >= minIdx && idx <= maxIdx)
            {
                ticks.Add(yr);
            }
        }

        if (ticks.Count == 0)
        {
            ticks.Add(firstYear);
        }

        return ticks;
    }

    private static string BuildSizePoints(LayoutMetrics layout)
    {
        if (layout.Months.Count == 0) return "";

        var sb = new StringBuilder(layout.Months.Count * 14);
        for (int i = 0; i < layout.Months.Count; i++)
        {
            var m = layout.Months[i];
            double x = layout.MonthX(m.Year, m.Month) + (layout.BarSlotWidth * 0.5);
            double y = SizeLaneTop + SizeLaneHeight - ((double)m.MaxBytes / layout.MaxBytes * SizeLaneHeight);
            if (i > 0) sb.Append(' ');
            sb.Append(F(x)).Append(',').Append(F(y));
        }

        return sb.ToString();
    }

    private static (double X, double Width)? ComputeRangePixels(LayoutMetrics layout, DateRange? range)
    {
        if (range is null || layout.Months.Count == 0 || range.To <= range.From) return null;

        int fromIdx = MonthIdx(range.From.Year, range.From.Month);
        var toAdjusted = range.To.Day == 1 && range.To.TimeOfDay == TimeSpan.Zero
            ? range.To
            : new DateTime(range.To.Year, range.To.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        int toIdx = MonthIdx(toAdjusted.Year, toAdjusted.Month);
        if (toIdx <= fromIdx) toIdx = fromIdx + 1;

        double x1 = Math.Clamp(layout.IndexX(fromIdx), layout.Left, layout.Right);
        double x2 = Math.Clamp(layout.IndexX(toIdx), layout.Left, layout.Right);
        if (x2 <= x1) return null;

        return (x1, x2 - x1);
    }

    private async Task HandleBarClickAsync(MonthBucket clicked, MouseEventArgs e)
    {
        DateRange selected;
        if ((e.ShiftKey || _awaitingRangeEnd) && _anchorMonth is not null)
        {
            var aIdx = MonthIdx(_anchorMonth.Year, _anchorMonth.Month);
            var bIdx = MonthIdx(clicked.Year, clicked.Month);
            var startBucket = aIdx <= bIdx ? _anchorMonth : clicked;
            var endBucket = aIdx <= bIdx ? clicked : _anchorMonth;

            var from = new DateTime(startBucket.Year, startBucket.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var to = new DateTime(endBucket.Year, endBucket.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
            selected = new DateRange(from, to);
            _awaitingRangeEnd = false;
        }
        else
        {
            _anchorMonth = clicked;
            _awaitingRangeEnd = true;
            var from = new DateTime(clicked.Year, clicked.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            selected = new DateRange(from, from.AddMonths(1));
        }

        if (OnRangeSelected.HasDelegate)
        {
            await OnRangeSelected.InvokeAsync(selected);
        }
    }

    private async Task SelectPresetAsync(DateRange? preset)
    {
        if (preset is null) return;
        _anchorMonth = null;
        _awaitingRangeEnd = false;
        if (OnRangeSelected.HasDelegate)
        {
            await OnRangeSelected.InvokeAsync(preset);
        }
    }

    private bool IsPresetActive(DateRange? preset) =>
        preset is not null && Range is not null && Range.From == preset.From && Range.To == preset.To;

    internal static string FormatTooltip(MonthBucket b)
    {
        var label = new DateTime(b.Year, b.Month, 1, 0, 0, 0, DateTimeKind.Utc)
            .ToString("MMM yyyy", CultureInfo.InvariantCulture);
        return $"{label}: {b.NamedEdits.ToString("N0", CultureInfo.InvariantCulture)} named, " +
               $"{b.AnonEdits.ToString("N0", CultureInfo.InvariantCulture)} anonymous edits, " +
               $"{b.Removals.ToString("N0", CultureInfo.InvariantCulture)} removed";
    }

    private string MarkClass(IncidentMark mark)
    {
        var info = EvidenceTiers.Get(mark.Tier);
        var hot = !string.IsNullOrEmpty(HighlightedIncidentId) &&
                  string.Equals(HighlightedIncidentId, mark.IncidentId, StringComparison.Ordinal)
            ? " is-hot"
            : "";
        return $"aot-mk aot-mk--{info.Id}{hot}";
    }

    private static bool IsCircleMark(IncidentMark mark) => mark.Tier == EvidenceTier.Reverted;

    private static bool IsDiamondMark(IncidentMark mark) => mark.Tier == EvidenceTier.Contested;

    private static string Rotate45(double cx, double cy) => $"rotate(45 {F(cx)} {F(cy)})";

    private static string HitPath(double x, double y, double w, double h) =>
        $"M{F(x)} {F(y)}h{F(w)}v{F(h)}h-{F(w)}Z";

    private static int MonthIdx(int year, int month) => (year * 12) + (month - 1);

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private sealed record LayoutMetrics(
        IReadOnlyList<MonthBucket> Months,
        int MinMonthIdx,
        int SpanMonths,
        double Left,
        double Right,
        double Top,
        double BarHeight,
        double BarSlotWidth,
        int MaxEdits,
        int MaxRemovals,
        int MaxBytes)
    {
        public double IndexX(int monthIdx) => Left + ((monthIdx - MinMonthIdx) * BarSlotWidth);

        public double MonthX(int year, int month) => IndexX(MonthIdx(year, month));

        public double MarkX(DateTime at)
        {
            double frac = (Math.Max(1, at.Day) - 1) / 31.0;
            double raw = Left + ((MonthIdx(at.Year, at.Month) - MinMonthIdx + frac) * BarSlotWidth);
            return Math.Clamp(raw, Left, Right);
        }
    }
}
