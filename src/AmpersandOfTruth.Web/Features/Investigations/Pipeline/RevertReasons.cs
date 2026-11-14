using System.Text.RegularExpressions;
using AmpersandOfTruth.Web.Features.Evidence;

namespace AmpersandOfTruth.Web.Features.Investigations.Pipeline;

/// <summary>
/// P1.S5 (#32) ported from <c>research/32-s5-summaries/lexicon.py</c>: the human-written reason inside a revert's edit
/// summary (MediaWiki / FlaggedRevs / gadget templates stripped, so a username in the template never counts as a reason)
/// and its Finnish + English reason categories. Kept regex-for-regex identical to the Python; the parity fixture
/// <c>tests/…/Investigations/Fixtures/s5-parity.jsonl</c> (generated from the Python by <c>s5_parity.py</c>) guards it.
/// Insert flags and user extraction are not ported (not needed by the pipeline yet). Pure and thread-safe.
/// </summary>
public static class RevertReasons
{
    public const string VandalismTest = "vandalism_test", Sourcing = "sourcing", NeutralityPov = "neutrality_pov",
        FactualError = "factual_error", OriginalResearch = "original_research", BlockEvasion = "block_evasion",
        Copyright = "copyright", Maintenance = "maintenance", Consensus = "consensus";

    /// <summary>The removal reason the evidence standard uses: accuracy &gt; neutrality &gt; sourcing (incl. original research)
    /// &gt; vandalism &gt; other. Only the first three count as a community-dispute signal (docs/evidence-standard.md).</summary>
    public static RemovalReason ToRemovalReason(IReadOnlySet<string> categories) =>
        categories.Contains(FactualError) ? RemovalReason.Accuracy
        : categories.Contains(NeutralityPov) ? RemovalReason.Neutrality
        : categories.Contains(Sourcing) || categories.Contains(OriginalResearch) ? RemovalReason.Sourcing
        : categories.Contains(VandalismTest) ? RemovalReason.Vandalism
        : categories.Count > 0 ? RemovalReason.Other
        : RemovalReason.None;

    /// <summary>Removal reason of a revert from its raw edit summary.</summary>
    public static RemovalReason Of(string? revertComment) => ToRemovalReason(Categories(ReasonText(revertComment)));

    private const RegexOptions I = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private const RegexOptions IS = I | RegexOptions.Singleline;

    // Alphabetic word boundaries (Unicode-aware, digits/underscores/hyphens do not block boundaries).
    private const string WbL = @"(?<![^\W\d_])";
    private const string WbR = @"(?![^\W\d_])";

    private static readonly Regex Section = new(@"^(?:\s*/\*.*?\*/\s*)+", RegexOptions.Singleline);

    private const string TalkParen = @"(?:\s*\((?:\[\[:?(?:User[ _]talk|Keskustelu[ _]käyttäjästä)[^\]]*\]\]|keskustelu|talk)\))?";
    private const string UserToken = @"(?:\[\[[^\]]+\]\]|[^\s()\[\],:;]+)";

    private static readonly Regex GadgetSuffix = new(
        @"(?:\s*\((?:MobileUndo\s+v[\d.]+|TwinkleGlobal|\[\[:?(?:meta|metawiki):[^\]]+\]\])\))+\s*$", I);

    // 1. MediaWiki Rollback
    private static readonly Regex RollbackFull = new(
        @"^(?<pre>.*?\s)?(?:Käyttäjän\s+(?<user1>" + UserToken + @")|(?<user2>" + UserToken + @"):n)" + TalkParen + @"\s+" +
        @"(?:muokkaukset|vandalismi)\s+kumottiin\s+ja\s+(?:sivu|artikkeli)\s+palautettiin\s+" +
        @"(?:viimeisimpään\s+)?(?:käyttäjän\s+" + UserToken + @"|.+?:n)\s+tekemään\s+versioon(?:\s+(?<rev>\d+))?\.?(?<tail>.*)$", IS);
    private static readonly Regex RollbackTrunc = new(
        @"^(?<pre>.*?\s)?(?:Käyttäjän\s+(?<user1>" + UserToken + @")|(?<user2>" + UserToken + @"):n)" + TalkParen + @"\s+" +
        @"(?:muokkaukset|vandalismi)\s+kumottiin\s+ja\s+(?:sivu|artikkeli)\s+pal.*$", I);

    // 2. Modern MediaWiki Undo
    private const string UndoRev = @"(?:#?(?<rev1>\d+)|\[\[:?(?:Special:Diff|Toiminnot:Ero)/(?<rev2>\d+)(?:\|[^\]]*)?\]\])";
    private static readonly Regex UndoModern = new(
        @"^(?<pre>.*?)Kumottu\s+muokkaus\s+" + UndoRev +
        @"(?:,\s*jonka\s+teki\s+(?<user>" + UserToken + @")" + TalkParen + @")?(?<tail>.*)$", IS);

    // 3. FlaggedRevs Reject
    private static readonly Regex FlaggedRevs = new(
        @"^(?<pre>.*?)(?:(?<=^)|(?<=[\s.!?;:]))" +
        @"Hylättiin\s+(?:viimeisin|viimeisimmät|viimeiset|viimeinen|ensimmäinen|ensimmäiset)\s+" +
        @"(?:\d+\s+)?tekstimuuto\w*(?:\s+sotkemisena)?\s*" +
        @"(?:\((?:tehnyt|tehneet|käyttäjältä)\s+(?<users>.+?)\)\s*)?" +
        @"(?:(?:ja\s+palautettiin\s+versio|,\s*jot?ka\s+tehtiin\s+versioon)\s+(?<rev>\d+)" +
        @"(?:(?:,\s*jonka\s+on\s+tehnyt|\s+käyttäjältä)\s+(?<rest>.*)|(?<ver_tail>.*))" +
        @"|(?<direct_tail>.*))$", IS);
    private static readonly Regex RestoredSelfDelim = new(
        @"^(?:\[\[[^\]]+\]\]" +
        @"|[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{0,4}){2,7}" +
        @"|\d{1,3}(?:\.\d{1,3}){3})(?<tail>.*)$", RegexOptions.Singleline);
    private static readonly Regex RestoredPunctSplit = new(
        @"^[^:;]+?(?=(?::\s|;\s|,\s|-->|\s-\s|\s\(|[:;]$))(?<tail>.*)$", RegexOptions.Singleline);

    // 4. Legacy MediaWiki Undo
    private static readonly Regex UndoLegacyUserRev = new(
        @"^(?<pre>.*?\s)?Käyttäjän\s+(?<user>" + UserToken + @")" + TalkParen + @"\s+muokkaus\s+(?<rev>\d+)\s+kumottiin(?<tail>.*)$", IS);
    private static readonly Regex UndoLegacyBareRev = new(@"^muokkaus\s+(?<rev>\d+)\s+kumottiin(?<tail>.*)$", IS);
    private static readonly Regex UndoLegacyNoRev = new(
        @"^Käyttäjän\s+(?<user>" + UserToken + @")" + TalkParen + @"\s+(?:[\wÄÖÅäöå]+\s+){0,3}muokkaukset\s+kumottiin(?<tail>.*)$", IS);

    // 5. Navigation Popups / EN gadgets
    private static readonly Regex PopupsPrior = new(
        @"^Revert(?:ed)?\s+to\s+the\s+revision\s+prior\s+to\s+revision\s+(?<rev>\d+)" +
        @"(?:\s+dated\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})?" +
        @"(?:\s+by\s+(?<user>" + UserToken + @"))?" +
        @"(?:\s+using\s+\[\[[^\]]+\]\])?(?<tail>.*)$", IS);
    private static readonly Regex PopupsRev = new(
        @"^Revert(?:ed)?\s+to\s+revision\s+(?<rev>\d+)" +
        @"(?:\s+dated\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})?" +
        @"(?:\s+by\s+.+?(?=\s+using\s+\[\[|[:;(]|$))?" +
        @"(?:\s+using\s+\[\[[^\]]+\]\])?(?<tail>.*)$", IS);
    private static readonly Regex EnRevertedBy = new(
        @"^Reverted\s+(?:\d+\s+)?edits?\s+by\s+(?<user>" + UserToken + @")" + TalkParen +
        @"(?:\s+to\s+last\s+revision\s+by\s+[^:;()]+)?(?<tail>.*)$", IS);
    private static readonly Regex EnUndid = new(
        @"^Undid\s+revision\s+(?<rev>\d+)\s+by\s+(?<user>" + UserToken + @")" + TalkParen + @"(?<tail>.*)$", IS);

    // 6. Legacy Rollback (2004–2006)
    private static readonly Regex LegacyRbKayttajan = new(
        @"^Käyttäjän\s+(?<user>" + UserToken + @")" + TalkParen + @"\s+muokkaukset\s+palautettiin\s+" +
        @"(?:viimeisimpään|viimeiseen)\s+käyttäjän\s+.+?\s+tekemään\s+muutokseen\.?(?<tail>.*)$", IS);
    private static readonly Regex LegacyRbGen = new(
        @"^Palautettiin\s+(?<user>.+?):n\s+muokkaukset\s+" +
        @"(?:viimeisimpään|viimeiseen)\s+.+?\s+tekemään\s+muutokseen\.?(?<tail>.*)$", IS);
    private static readonly Regex LegacyRbPlain = new(@"^Palautett(?:iin|u)\s+.+?\s+muutokseen\.?(?<tail>.*)$", IS);

    private static readonly Regex TrailingPunct = new(@"[:;,.\-–—\s]+$");
    private static readonly Regex LeadingPunct = new(@"^(?:-->|[:;,.\-–—\s])+");

    public static string StripSection(string? comment) =>
        string.IsNullOrEmpty(comment) ? "" : Section.Replace(comment, "", 1).Trim();

    /// <summary>The human-written reason of any edit summary: the tail/prefix of an auto-revert template, else the summary
    /// without its <c>/* section */</c> markers (lexicon.py <c>extract_reason_text</c>).</summary>
    public static string ReasonText(string? comment) => AutoReasonText(comment) ?? StripSection(comment);

    /// <summary>lexicon.py <c>parse_auto(...)["reason_text"]</c>; null when the summary is not an auto-revert template.</summary>
    public static string? AutoReasonText(string? comment)
    {
        var body = StripSection(comment);
        if (body.Length == 0) return null;

        Match m;
        if ((m = RollbackFull.Match(body)).Success) return CleanTail(m.Groups["tail"], m.Groups["pre"]);
        if ((m = RollbackTrunc.Match(body)).Success) return CleanTail(null, m.Groups["pre"]);
        if ((m = UndoModern.Match(body)).Success) return CleanTail(m.Groups["tail"], m.Groups["pre"]);
        if ((m = FlaggedRevs.Match(body)).Success)
        {
            string tail = "";
            if (m.Groups["rest"].Success)
            {
                var rest = m.Groups["rest"].Value.Trim();
                if (rest.Length > 0)
                {
                    if (RestoredSelfDelim.Match(rest) is { Success: true } sd) tail = sd.Groups["tail"].Value;
                    else if (RestoredPunctSplit.Match(rest) is { Success: true } ps) tail = ps.Groups["tail"].Value;
                }
            }
            else
            {
                tail = m.Groups["ver_tail"].Success && m.Groups["ver_tail"].Value.Length > 0 ? m.Groups["ver_tail"].Value
                    : m.Groups["direct_tail"].Success ? m.Groups["direct_tail"].Value : "";
            }
            return CleanTail(tail, m.Groups["pre"].Success ? m.Groups["pre"].Value : null);
        }
        if ((m = UndoLegacyUserRev.Match(body)).Success) return CleanTail(m.Groups["tail"], m.Groups["pre"]);
        if ((m = UndoLegacyBareRev.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = UndoLegacyNoRev.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = PopupsPrior.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = PopupsRev.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = EnRevertedBy.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = EnUndid.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = LegacyRbKayttajan.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = LegacyRbGen.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        if ((m = LegacyRbPlain.Match(body)).Success) return CleanTail(m.Groups["tail"], null);
        return null;
    }

    private static string CleanTail(Group? tail, Group? pre) =>
        CleanTail(tail is { Success: true } t ? t.Value : null, pre is { Success: true } p ? p.Value : null);

    private static string CleanTail(string? tail, string? pre)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(pre))
        {
            var p = TrailingPunct.Replace(pre.Trim(), "").Trim();
            if (p.Length > 0) parts.Add(p);
        }
        if (!string.IsNullOrEmpty(tail))
        {
            var t = GadgetSuffix.Replace(tail, "").Trim();
            t = LeadingPunct.Replace(t, "").Trim();
            // Unwrap single outer parentheses `(reason)` when balanced across the whole tail.
            if (t.StartsWith('(') && t.EndsWith(')') && t.Length >= 2)
            {
                var inner = t[1..^1].Trim();
                var depth = 0;
                var balanced = true;
                foreach (var ch in inner)
                {
                    if (ch == '(') depth++;
                    else if (ch == ')' && --depth < 0) { balanced = false; break; }
                }
                if (balanced && depth == 0) t = inner;
            }
            t = GadgetSuffix.Replace(t, "").Trim();
            if (t.Length > 0) parts.Add(t);
        }
        return string.Join(" ", parts).Trim();
    }

    // ── Finnish + English reason lexicon (9 categories) ──

    private static readonly Regex IaBot = new(@"#iabot\b", I);

    private static readonly Regex Vandalism = new(
        WbL + @"(?:" +
        @"vandalis\w*|vandaal\w*|vandal\w*" +
        @"|sotk(?:u|e|i|enta|enna)\w*" +
        @"|häirik\w*" +
        @"|hiekkalaatik\w*" +
        @"|testimuokkau\w*|testail\w*|testi" +
        @"|kokeil\w*" +
        @"|töhry\w*|töhri\w*" +
        @"|hölynpöly\w*|pelleily\w*|pilail\w*|hassuttel\w*" +
        @"|asiaton\w*|asiattom\w*|epäasialli\w*" +
        @"|späm\w*|roskapost\w*" +
        @"|rvv|spam\w*|nonsense|gibberish|graffiti" +
        @")" + WbR +
        @"|sivu\s+tyhjennettiin|tyhjensi\s+sivun|sivun\s+tyhjennys", I);
    private static readonly Regex Roska = new(WbL + @"roska(?!luok)\w*" + WbR, I);
    private static readonly Regex EiRoskaa = new(WbL + @"ei(?:kä)?\s+(?:ole\s+)?roska\w*" + WbR, I);

    private static readonly Regex SourcingRe = new(
        WbL + @"(?:" +
        @"lähteetön\w*|lähteettöm\w*" +
        @"|viitteetön\w*|viitteettöm\w*" +
        @"|lähdepyynt\w*" +
        @"|unsourced|unreferenced" +
        @")" + WbR +
        @"|" + WbL + @"(?:tieto)?lähde\s*\?+" +
        @"|" + WbL + @"(?:ei|ilman)\s+(?:\w+\s+)?(?:tieto)?läh(?:dettä|teitä)" + WbR +
        @"|" + WbL + @"(?:(?:tieto)?lähde|lähteet|viite|viitteet)\s+puuttu\w*" + WbR +
        @"|" + WbL + @"(?:vaatii|kaipaa|tarvitsee|etsi)\s+(?:tieto)?läh(?:teen|dettä|de)" + WbR +
        @"|" + WbL + @"lähdeaineisto\s+on\s+puutteelli\w*" + WbR +
        @"|" + WbL + @"ei\s+(?:ole\s+|mainita\s+|löydy\s+|sanota\s+|väitetä\s+)?lähtee(?:ssä|stä)" + WbR +
        @"|" + WbL + @"(?:lähteen\s+vastai\w*|vastoin\s+lähdettä)" + WbR +
        @"|" + WbL + @"(?:ei\s+(?:ole\s+|vastaa\s+)?lähteen\s*mukai\w*|ei\s+vastaa\s+(?:\w+\s+)?lähdettä)" + WbR +
        @"|" + WbL + @"(?:vastaakohan\s+(?:\w+\s+)?lähdettä|onkohan\s+lähteen\s*mukai\w*)" + WbR +
        @"|" + WbL + @"(?:kelvoton|epäluotettava|huono|heikko)\s+(?:tieto)?lähde" + WbR +
        @"|" + WbL + @"lähteistetyn\s+tiedon\s+poisto" + WbR +
        @"|" + WbL + @"(?:citation\s+needed|no\s+sources?|unreliable\s+sources?)" + WbR, I);

    private static readonly Regex NeutralityPovRe = new(
        WbL + @"(?:" +
        @"n?pov" +
        @"|pr[oå]pagand\w*" +
        @"|(?:epä)?puolueelli\w*" +
        @"|(?:epä)?neutraal\w*|neutraloi\w*" +
        @"|mielipi(?:de|te)\w*" +
        @"|spekul(?:aatio|oint|oi)\w*" +
        @"|asenteelli\w*" +
        @"|näkökulm\w*" +
        @"|väritty\w*|kantaaottav\w*|arvottav\w*|leimaav\w*|tendenssi\w*|pamflett\w*" +
        @"|bias(?:ed|ia)?|non-neutral|one-sided" +
        @")" + WbR, I);

    private static readonly Regex FactualErrorRe = new(
        WbL + @"(?:" +
        @"väär(?:ä\w*|i\w*|in)" +
        @"|(?:asia|fakta)?virhe\w*" +
        @"|valhe\w*" +
        @"|paikkansapitämät\w*" +
        @"|perätön\w*|perättöm\w*" +
        @"|epätos\w*|epätot\w*" +
        @"|pöty\w*|puppu\w*|huuhaa\w*" +
        @"|fakta(?:t|a|n)?" +
        @"|false|untrue|inaccurate|incorrect|hoax|fabricated" +
        @")" + WbR +
        @"|" + WbL + @"ei(?:vät)?\s+pidä\s+paikkaansa" + WbR +
        @"|" + WbL + @"vastoin\s+tosiasioita" + WbR +
        @"|" + WbL + @"factual\s+errors?" + WbR, I);
    private static readonly Regex GrammarVaarin = new(WbL + @"kielellisesti\s+väärin" + WbR, I);

    private static readonly Regex OriginalResearchRe = new(
        WbL + @"oma\w*\s+tutkimu\w*" + WbR +
        @"|" + WbL + @"(?:uutta|uusi)\s+tutkimu\w*" + WbR +
        @"|" + WbL + @"oma\w*\s+(?:päätelm\w*|tulkint\w*|pohdint\w*)" + WbR +
        @"|" + WbL + @"(?:wp:n?or|original\s+research)" + WbR, I);

    private static readonly Regex BlockEvasionRe = new(
        WbL + @"(?:" +
        @"eston\s*kier\w*" +
        @"|estet(?:ty|yn)\s+käyttäjä\w*" +
        @"|sukkanukk\w*" +
        @"|sockpuppet\w*" +
        @"|block\s+evasion" +
        @")" + WbR +
        @"|" + WbL + @"(?:rv|palautus)[,\s–\-]+wp:?k" + WbR, I);

    private static readonly Regex CopyrightRe = new(
        WbL + @"(?:" +
        @"tekijänoikeu\w*" +
        @"|copyvio\w*|copyright\w*" +
        @"|kopio(?:itu|intia|inti)\w*" +
        @"|plagiaat\w*|plagioi\w*" +
        @")" + WbR +
        @"|" + WbL + @"(?:suora\w*|luvaton\w*)\s+kopio\w*" + WbR, I);
    private static readonly Regex CopyTemplateOk = new(@"tekijänoikeus(?:tsekkaus)?malline\s+pois|\{\{tekijänoikeus_", I);

    private static readonly Regex MaintenanceRe = new(
        WbL + @"(?:" +
        @"format\w*|muotoil\w*|ulkoasu\w*|asettel\w*|tyyliopa\w*" +
        @"|kielenhuol\w*|kh|oikolu\w*|kieliasu\w*|oikeinkirjoitu\w*|kielellisesti" +
        @"|typo\w*|(?:kirjoitus|lyönti|paino|näppäily|pilkku|kieli(?:oppi)?)virhe\w*" +
        @"|(?:wiki|kieli)?link(?:ki|it|kejä|kien|itys|itystä|in)\w*|interwiki\w*|wikity\w*" +
        @"|(?:pää|ala|ylä|tupla|paikkakunta|roska)?luok(?:ka|at|itus|ittelu\w*|asta|kaan|kia)\w*|hotcat\w*" +
        @"|malline\w*|tietolaatik\w*|navigaatio\w*|täsmennyssiv\w*|defaultsort" +
        @"|wl|lk|iw|fix|cleanup" +
        @")" + WbR +
        @"|(?<!\S)(?:-?w|-?l)(?!\S)", I);

    private static readonly Regex ConsensusRe = new(
        WbL + @"(?:" +
        @"konsensu\w*" +
        @"|keskustelusivu\w*" +
        @"|kahvihuone\w*" +
        @"|poistoäänesty\w*" +
        @"|consensus" +
        @")" + WbR +
        @"|" + WbL + @"(?:per\.?|ks\.?|kts\.?|katso)\s+keskustelu\w*" + WbR +
        @"|" + WbL + @"keskustelun\s+(?:perusteella|mukaan)" + WbR +
        @"|" + WbL + @"ilman\s+keskustelua" + WbR +
        @"|" + WbL + @"per\s+talk" + WbR, I);

    /// <summary>lexicon.py <c>reasons(text)</c>: zero or more of the 9 categories for human-written summary text.</summary>
    public static IReadOnlySet<string> Categories(string? text)
    {
        var t = StripSection(text);
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (t.Length == 0 || IaBot.IsMatch(t)) return result;

        if (Vandalism.IsMatch(t) || (Roska.IsMatch(t) && !EiRoskaa.IsMatch(t))) result.Add(VandalismTest);
        if (SourcingRe.IsMatch(t)) result.Add(Sourcing);
        if (NeutralityPovRe.IsMatch(t)) result.Add(NeutralityPov);
        if (FactualErrorRe.IsMatch(GrammarVaarin.Replace(t, ""))) result.Add(FactualError);
        if (OriginalResearchRe.IsMatch(t)) result.Add(OriginalResearch);
        if (BlockEvasionRe.IsMatch(t)) result.Add(BlockEvasion);
        if (CopyrightRe.IsMatch(CopyTemplateOk.Replace(t, ""))) result.Add(Copyright);
        if (MaintenanceRe.IsMatch(t) || CopyTemplateOk.IsMatch(t)) result.Add(Maintenance);
        if (ConsensusRe.IsMatch(t)) result.Add(Consensus);
        return result;
    }
}
