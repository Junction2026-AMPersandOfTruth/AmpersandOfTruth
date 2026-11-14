using AmpersandOfTruth.Web.Features.Hotspots;
using AmpersandOfTruth.Web.Features.Investigations;

namespace AmpersandOfTruth.Tests.Features.Hotspots;

public sealed class FakeHotspotStore : IHotspotStore
{
    public HotspotData? Data { get; set; }
    public Exception? Failure { get; set; }
    public int Calls { get; private set; }

    public Task<HotspotData?> LoadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;

        if (Failure is not null)
        {
            return Task.FromException<HotspotData?>(Failure);
        }

        return Task.FromResult(Data);
    }

    public static HotspotData SampleData()
    {
        var headline = new HotspotHeadline(
            Revisions: 862_076,
            Articles: 19_742,
            Reverts: 41_606,
            Episodes: 5_178,
            AnonRevertedShare: 0.7011);

        var articles = new List<HotArticle>
        {
            new(
                PageId: 1213,
                TitleFi: "Jatkosota",
                TitleEn: "Continuation War",
                ContestScore: 141.4825,
                Reverts: 977,
                RevertsDispute: 238,
                RevertsVandalism: 737,
                RevertsOther: 2,
                Episodes: 48,
                AnonShare: 0.8185,
                FirstTs: new DateTime(2003, 9, 10, 0, 0, 0, DateTimeKind.Utc),
                LastTs: new DateTime(2026, 8, 19, 0, 0, 0, DateTimeKind.Utc),
                YearlyReverts: [2, 8, 19, 44, 62, 55, 48, 41, 38, 52, 46, 61, 58, 49, 43, 39, 37, 51, 44, 59, 45, 36, 24, 16]),
            new(
                PageId: 1256,
                TitleFi: "Talvisota",
                TitleEn: "Winter War",
                ContestScore: 132.1040,
                Reverts: 884,
                RevertsDispute: 215,
                RevertsVandalism: 660,
                RevertsOther: 9,
                Episodes: 44,
                AnonShare: 0.7420,
                FirstTs: new DateTime(2003, 5, 12, 0, 0, 0, DateTimeKind.Utc),
                LastTs: new DateTime(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc),
                YearlyReverts: [1, 6, 15, 38, 51, 49, 40, 35, 30, 42, 39, 58, 52, 40, 36, 31, 29, 44, 41, 64, 50, 33, 20, 10]),
            new(
                PageId: 4099,
                TitleFi: "Karjalan palautus",
                TitleEn: null,
                ContestScore: 98.4000,
                Reverts: 312,
                RevertsDispute: 164,
                RevertsVandalism: 145,
                RevertsOther: 3,
                Episodes: 31,
                AnonShare: 0.5120,
                FirstTs: new DateTime(2004, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                LastTs: new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc),
                YearlyReverts: [0, 14, 28, 35, 41, 30, 22, 19, 18, 15, 12, 14, 11, 9, 8, 7, 6, 5, 6, 4, 3, 3, 2, 0]),
        };

        var months = new List<HotMonth>
        {
            new("2006-05", Reverts: 120, Episodes: 22, Edits: 2_800, TopPageId: 9001, TopTitleFi: "Suomi", TopTitleEn: "Finland"),
            new("2007-01", Reverts: 483, Episodes: 95, Edits: 7_197, TopPageId: 2001, TopTitleFi: "Kalevala", TopTitleEn: "Kalevala"),
            new("2007-04", Reverts: 410, Episodes: 61, Edits: 7_207, TopPageId: 2002, TopTitleFi: "Mikael Agricola", TopTitleEn: "Mikael Agricola"),
            new("2008-04", Reverts: 357, Episodes: 64, Edits: 4_664, TopPageId: 2003, TopTitleFi: "Suomen historialliset maakunnat", TopTitleEn: "Historical provinces of Finland"),
            new("2012-01", Reverts: 326, Episodes: 49, Edits: 3_580, TopPageId: 2004, TopTitleFi: "Pekka Haavisto", TopTitleEn: "Pekka Haavisto"),
            new("2015-09", Reverts: 310, Episodes: 24, Edits: 2_635, TopPageId: 2005, TopTitleFi: "Sauli Niinistö", TopTitleEn: "Sauli Niinistö"),
            new("2020-05", Reverts: 389, Episodes: 14, Edits: 3_278, TopPageId: 2006, TopTitleFi: "Suomen koronaviruspandemia", TopTitleEn: "COVID-19 pandemic in Finland"),
            new("2024-02", Reverts: 140, Episodes: 18, Edits: 2_100, TopPageId: 2007, TopTitleFi: "Alexander Stubb", TopTitleEn: "Alexander Stubb"),
        };

        var categories = new List<HotCategory>
        {
            new("Kansanedustajat", Articles: 2_435, ContestScore: 1_275.0870, Reverts: 5_561, DisputeReverts: 2_275, Episodes: 592),
            new("Suomen sisällissota", Articles: 214, ContestScore: 412.5000, Reverts: 1_420, DisputeReverts: 680, Episodes: 184),
            new("Suomen historia", Articles: 80, ContestScore: 100.3568, Reverts: 432, DisputeReverts: 196, Episodes: 72),
        };

        return new HotspotData(headline, articles, months, categories);
    }

    public static CorpusUnavailableException DownException() =>
        new("Corpus 127.0.0.1:3307 not available: connection refused");
}
