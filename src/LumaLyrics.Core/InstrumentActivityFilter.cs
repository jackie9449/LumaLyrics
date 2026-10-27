namespace LumaLyrics.Core;

/// <summary>One instrument's causal hysteresis. Call once per distinct observation.</summary>
public sealed class InstrumentActivityFilter
{
    readonly ActivityParameters parameters;
    int attack;
    bool active;
    double? previous;
    double lastSupported;

    public InstrumentActivityFilter(ActivityParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!float.IsFinite(parameters.On) || !float.IsFinite(parameters.Off)
            || parameters.Off < 0 || parameters.On > 1 || parameters.Off >= parameters.On
            || parameters.AttackFrames < 1 || !double.IsFinite(parameters.ReleaseMs) || parameters.ReleaseMs < 0)
            throw new ArgumentException("Invalid instrument activity thresholds.", nameof(parameters));
        this.parameters = parameters;
    }

    public bool Update(float score, double nowMs)
    {
        if (!float.IsFinite(score) || score is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(score));
        if (!double.IsFinite(nowMs) || nowMs < 0 || previous.HasValue && nowMs <= previous.Value)
            throw new ArgumentOutOfRangeException(nameof(nowMs), "Observations must have distinct increasing times.");
        previous = nowMs;
        if (!active)
        {
            attack = score >= parameters.On ? attack + 1 : 0;
            if (attack >= parameters.AttackFrames) { active = true; lastSupported = nowMs; }
        }
        else if (score >= parameters.Off) lastSupported = nowMs;
        else if (nowMs - lastSupported >= parameters.ReleaseMs) { active = false; attack = 0; }
        return active;
    }

    public void Reset()
    {
        attack = 0; active = false; previous = null; lastSupported = 0;
    }
}
