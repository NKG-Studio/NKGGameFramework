namespace NKGGameFramework.Gameplay;

public enum AttributeModifierOperation
{
    Add,
    Multiply,
    Override,
}

// A single modifier applied to an attribute. `Source` tags the grantor (e.g. a
// BuffInstance); removing all modifiers for a source undoes its contribution.
public sealed class AttributeModifier
{
    public required GameplayAttribute Attribute { get; init; }

    public AttributeModifierOperation Operation { get; init; } = AttributeModifierOperation.Add;

    public double Magnitude { get; init; }

    public int Channel { get; init; }

    public AttributeCapture? Capture { get; init; }

    public double Coefficient { get; init; } = 1;

    internal double EvaluateMagnitude() => Magnitude + (Capture?.Value ?? 0) * Coefficient;

    internal void Validate()
    {
        if (!Attribute.IsValid || !Enum.IsDefined(Operation) || Channel < 0 ||
            !double.IsFinite(Magnitude) || !double.IsFinite(Coefficient))
            throw new ArgumentException("A modifier requires a valid attribute, operation, channel and finite magnitude.");
    }

    public object? Source { get; init; }

    public override string ToString() => $"{Operation} {Magnitude} on {Attribute}";
}

// Declares a modifier that a buff grants, with optional per-level magnitude.
public sealed class AttributeModifierSpec
{
    public required GameplayAttribute Attribute { get; init; }

    public AttributeModifierOperation Operation { get; init; } = AttributeModifierOperation.Add;

    public Dictionary<int, double> ValuesByLevel { get; init; } = [];

    public double Magnitude { get; init; }

    public int Channel { get; init; }

    public bool ScaleWithStacks { get; init; }

    public bool UseStacksAsLevel { get; init; }

    public double GetMagnitude(int level)
    {
        if (ValuesByLevel.Count == 0)
        {
            return Magnitude;
        }

        if (ValuesByLevel.TryGetValue(level, out var exact))
        {
            return exact;
        }

        var bestLevel = int.MinValue;
        var bestValue = Magnitude;
        foreach (var (configuredLevel, value) in ValuesByLevel)
        {
            if (configuredLevel <= level && configuredLevel > bestLevel)
            {
                bestLevel = configuredLevel;
                bestValue = value;
            }
        }

        return bestLevel == int.MinValue ? Magnitude : bestValue;
    }
}

// A snapshot is captured once; a live capture reads the dependency on evaluation.
public sealed class AttributeCapture
{
    private readonly GameplayAttributeInstance? _attribute;
    private readonly double _snapshot;

    public AttributeCapture(GameplayAttributeInstance attribute, bool snapshot = false)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        if (snapshot) _snapshot = attribute.CurrentValue;
        else _attribute = attribute;
    }

    public double Value => _attribute?.CurrentValue ?? _snapshot;
}
