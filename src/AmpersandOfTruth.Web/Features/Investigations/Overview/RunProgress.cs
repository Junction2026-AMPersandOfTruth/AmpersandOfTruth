using AmpersandOfTruth.Web.Features.Investigations.Pipeline;

namespace AmpersandOfTruth.Web.Features.Investigations.Overview;

public enum StageState { Pending, Active, Done, Failed }

public sealed class RunProgress
{
    private static readonly TimeSpan SlowThreshold = TimeSpan.FromSeconds(20);

    private readonly TimeProvider _time;
    private readonly StageState[] _states = new StageState[4];
    private readonly long?[] _activatedAt = new long?[4];
    private readonly List<IncidentRecord> _incidents = [];

    private long? _startedAt;
    private TimeSpan? _finalElapsed;
    private int? _articlesCount;
    private int? _revisionsCount;
    private bool _readStarted;
    private IReadOnlyList<TopicArticle> _articles = [];
    private IReadOnlyList<RemovedEdit> _candidates = [];

    public RunProgress(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
    }

    public int Scored { get; private set; }

    public int Of { get; private set; }

    public IReadOnlyList<TopicArticle> Articles => _articles;

    public IReadOnlyList<RemovedEdit> Candidates => _candidates;

    public IReadOnlyList<IncidentRecord> Incidents => _incidents;

    public StageFailed? Failure { get; private set; }

    public bool IsRunning { get; private set; }

    public bool IsComplete { get; private set; }

    public bool IsCancelled { get; private set; }

    public bool FromCache { get; private set; }

    public bool HasAssessmentUnavailable { get; private set; }

    public InvestigationStage? ActiveStage
    {
        get
        {
            for (var i = 0; i < _states.Length; i++)
            {
                if (_states[i] == StageState.Active)
                {
                    return (InvestigationStage)i;
                }
            }

            return null;
        }
    }

    public TimeSpan Elapsed => _finalElapsed
        ?? (_startedAt is { } started ? _time.GetElapsedTime(started) : TimeSpan.Zero);

    public void Start()
    {
        Array.Fill(_states, StageState.Pending);
        Array.Clear(_activatedAt);
        _incidents.Clear();
        _articles = [];
        _candidates = [];
        _articlesCount = null;
        _revisionsCount = null;
        _readStarted = false;
        Scored = 0;
        Of = 0;
        Failure = null;
        IsComplete = false;
        IsCancelled = false;
        FromCache = false;
        HasAssessmentUnavailable = false;
        _finalElapsed = null;

        var now = _time.GetTimestamp();
        _startedAt = now;
        IsRunning = true;
        Activate(InvestigationStage.FindArticles, now);
    }

    public void MarkCachedFallback(IReadOnlyList<IncidentRecord> cachedRecords)
    {
        ArgumentNullException.ThrowIfNull(cachedRecords);
        _incidents.Clear();
        _incidents.AddRange(cachedRecords);
        Failure = null;
        IsRunning = false;
        IsComplete = true;
        IsCancelled = false;
        FromCache = true;
        _finalElapsed ??= _startedAt is { } start ? _time.GetElapsedTime(start) : TimeSpan.Zero;
        for (var i = 0; i < _states.Length; i++)
        {
            _states[i] = StageState.Done;
            _activatedAt[i] = null;
        }
    }

    public void Cancel()
    {
        if (!IsRunning) return;
        IsRunning = false;
        IsCancelled = true;
        _finalElapsed = _startedAt is { } start ? _time.GetElapsedTime(start) : TimeSpan.Zero;
        for (var i = 0; i < _states.Length; i++)
        {
            if (_states[i] == StageState.Active)
            {
                _states[i] = StageState.Pending;
                _activatedAt[i] = null;
            }
        }
    }

    public void Apply(InvestigationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);

        var now = _time.GetTimestamp();
        _startedAt ??= now;

        switch (e)
        {
            case ArticlesFound a:
                _articles = a.Items;
                _articlesCount = a.Count;
                MarkDone(InvestigationStage.FindArticles);
                Activate(InvestigationStage.ScanHistory, now);
                break;

            case RevisionsScanned s:
                _revisionsCount = s.Count;
                _candidates = s.Candidates;
                Of = s.Candidates.Count;
                Scored = 0;
                _readStarted = true;
                MarkDone(InvestigationStage.ScanHistory);
                if (Of == 0)
                {
                    MarkDone(InvestigationStage.ReadEdits);
                    Activate(InvestigationStage.AssembleIncidents, now);
                }
                else
                {
                    Activate(InvestigationStage.ReadEdits, now);
                }
                break;

            case CandidateScored c:
                _readStarted = true;
                Scored = c.Index;
                Of = c.Of;
                if (c.AssessmentUnavailable)
                {
                    HasAssessmentUnavailable = true;
                }

                if (c.Incident is not null)
                {
                    _incidents.Add(c.Incident);
                }

                if (c.Of > 0 && c.Index >= c.Of)
                {
                    MarkDone(InvestigationStage.ReadEdits);
                    Activate(InvestigationStage.AssembleIncidents, now);
                }
                else if (_states[(int)InvestigationStage.ReadEdits] == StageState.Pending)
                {
                    Activate(InvestigationStage.ReadEdits, now);
                }
                break;

            case StageFailed f:
                Failure = f;
                IsRunning = false;
                IsComplete = false;
                IsCancelled = false;
                _finalElapsed = _startedAt is { } start ? _time.GetElapsedTime(start) : TimeSpan.Zero;
                SetFailed(f.Stage);
                break;

            case Completed done:
                FromCache = done.FromCache;
                _finalElapsed = done.Elapsed;
                _incidents.Clear();
                _incidents.AddRange(done.Incidents);
                Failure = null;
                IsRunning = false;
                IsComplete = true;
                IsCancelled = false;
                for (var i = 0; i < _states.Length; i++)
                {
                    _states[i] = StageState.Done;
                    _activatedAt[i] = null;
                }
                break;
        }
    }

    public StageState State(InvestigationStage s) => _states[IndexOf(s)];

    public int? Count(InvestigationStage s) => s switch
    {
        InvestigationStage.FindArticles => _articlesCount,
        InvestigationStage.ScanHistory => _revisionsCount,
        InvestigationStage.ReadEdits => _readStarted ? Scored : null,
        InvestigationStage.AssembleIncidents =>
            _states[(int)InvestigationStage.AssembleIncidents] is StageState.Active or StageState.Done
                ? _incidents.Count
                : null,
        _ => throw new ArgumentOutOfRangeException(nameof(s), s, null),
    };

    public bool IsSlow(InvestigationStage s)
    {
        var idx = IndexOf(s);
        return _states[idx] == StageState.Active
            && _activatedAt[idx] is { } activated
            && _time.GetElapsedTime(activated) > SlowThreshold;
    }

    public IReadOnlyList<RemovedEdit> Unflagged()
    {
        if (_candidates.Count == 0) return [];
        if (_incidents.Count == 0) return _candidates;

        var flaggedRevIds = new HashSet<long>();
        foreach (var incident in _incidents)
        {
            foreach (var revId in incident.InsertingRevIds)
            {
                flaggedRevIds.Add(revId);
            }
        }

        return _candidates
            .Where(c => !flaggedRevIds.Contains(c.Revert.RevertedRevId))
            .ToList();
    }

    private void Activate(InvestigationStage stage, long now)
    {
        var idx = IndexOf(stage);
        _states[idx] = StageState.Active;
        _activatedAt[idx] = now;
    }

    private void MarkDone(InvestigationStage stage)
    {
        var idx = IndexOf(stage);
        _states[idx] = StageState.Done;
        _activatedAt[idx] = null;
    }

    private void SetFailed(InvestigationStage stage)
    {
        var idx = IndexOf(stage);
        _states[idx] = StageState.Failed;
        _activatedAt[idx] = null;

        for (var i = 0; i < _states.Length; i++)
        {
            if (i == idx) continue;
            if (i > idx || _states[i] == StageState.Active)
            {
                _states[i] = i < idx && _states[i] == StageState.Done ? StageState.Done : StageState.Pending;
                _activatedAt[i] = null;
            }
        }
    }

    private static int IndexOf(InvestigationStage s)
    {
        var idx = (int)s;
        if ((uint)idx >= 4u) throw new ArgumentOutOfRangeException(nameof(s), s, null);
        return idx;
    }
}
