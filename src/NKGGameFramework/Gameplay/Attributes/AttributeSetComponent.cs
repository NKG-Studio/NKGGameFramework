using NKGGameFramework.Ecs;
using OdinSerializer;

namespace NKGGameFramework.Gameplay;

// Runtime state of a single attribute: base value (serialized ground truth)
// plus transient modifiers (re-applied by buffs/skills each load).
public sealed class GameplayAttributeInstance
{
    [NonSerialized] private List<AttributeModifier>? _modifiers;
    [NonSerialized] private bool _evaluating;
    [OdinSerialize] private double _baseValue;
    private List<AttributeModifier> MutableModifiers => _modifiers ??= [];

    public GameplayAttributeInstance(
        GameplayAttribute attribute,
        double baseValue,
        double minValue = double.NegativeInfinity,
        double maxValue = double.PositiveInfinity)
    {
        if (!attribute.IsValid)
        {
            throw new ArgumentException("Attribute must be valid.", nameof(attribute));
        }

        if (double.IsNaN(minValue) || double.IsNaN(maxValue) || maxValue < minValue)
        {
            throw new ArgumentOutOfRangeException(nameof(maxValue), "Max value cannot be less than min value.");
        }

        Attribute = attribute;
        BaseValue = baseValue;
        MinValue = minValue;
        MaxValue = maxValue;
    }

    [OdinSerialize] public GameplayAttribute Attribute { get; }

    public double BaseValue
    {
        get => _baseValue;
        set => _baseValue = double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    [OdinSerialize] public double MinValue { get; set; }

    [OdinSerialize] public double MaxValue { get; set; }

    [field: NonSerialized] public AttributeCapture? Minimum { get; set; }

    [field: NonSerialized] public AttributeCapture? Maximum { get; set; }

    public IReadOnlyList<AttributeModifier> Modifiers => MutableModifiers.AsReadOnly();

    public double CurrentValue => Evaluate();

    // Channels ascend; within each channel: (input + sum(Add)) * product(Multiply),
    // or the last Override. Insertion order only breaks ties between overrides.
    public double Evaluate()
    {
        if (_evaluating) throw new InvalidOperationException($"Cyclic attribute dependency at '{Attribute}'.");
        _evaluating = true;
        try
        {
            var value = BaseValue;
            var modifiers = MutableModifiers;
            for (var i = 0; i < modifiers.Count;)
            {
                var channel = modifiers[i].Channel;
                double add = 0, multiply = 1;
                double? replace = null;
                do
                {
                    var modifier = modifiers[i++];
                    var magnitude = modifier.EvaluateMagnitude();
                    if (!double.IsFinite(magnitude)) throw new InvalidOperationException($"Modifier for '{Attribute}' overflowed.");
                    switch (modifier.Operation)
                    {
                        case AttributeModifierOperation.Add: add += magnitude; break;
                        case AttributeModifierOperation.Multiply: multiply *= magnitude; break;
                        case AttributeModifierOperation.Override: replace = magnitude; break;
                    }
                } while (i < modifiers.Count && modifiers[i].Channel == channel);
                value = replace ?? (value + add) * multiply;
                if (!double.IsFinite(value)) throw new InvalidOperationException($"Attribute '{Attribute}' overflowed.");
            }
            return Clamp(value);
        }
        finally { _evaluating = false; }
    }

    private double Clamp(double value)
    {
        var min = Minimum?.Value ?? MinValue;
        var max = Maximum?.Value ?? MaxValue;
        if (double.IsNaN(min) || double.IsNaN(max) || min > max)
            throw new InvalidOperationException($"Invalid bounds for '{Attribute}'.");
        return Math.Clamp(value, min, max);
    }

    // Instant changes persist in base; removing a buff must not undo damage or healing.
    public double ApplyInstant(AttributeModifierOperation operation, double magnitude)
    {
        if (!double.IsFinite(magnitude)) throw new ArgumentOutOfRangeException(nameof(magnitude));
        var previous = CurrentValue;
        var value = operation switch
        {
            AttributeModifierOperation.Add => BaseValue + magnitude,
            AttributeModifierOperation.Multiply => BaseValue * magnitude,
            AttributeModifierOperation.Override => magnitude,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
        if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(magnitude));
        var previousBase = BaseValue;
        BaseValue = Clamp(value);
        try { return CurrentValue - previous; }
        catch { BaseValue = previousBase; throw; }
    }

    public void ApplyModifier(AttributeModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);
        modifier.Validate();
        if (modifier.Attribute != Attribute) throw new ArgumentException("Modifier targets a different attribute.", nameof(modifier));
        var modifiers = MutableModifiers;
        if (modifiers.Contains(modifier)) throw new InvalidOperationException("Modifier is already applied.");
        var index = modifiers.FindIndex(m => m.Channel > modifier.Channel);
        modifiers.Insert(index < 0 ? modifiers.Count : index, modifier);
    }

    public bool RemoveModifier(AttributeModifier handle) => MutableModifiers.Remove(handle);

    public bool RemoveModifiers(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var removed = false;
        var modifiers = MutableModifiers;
        for (var i = modifiers.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(modifiers[i].Source, source))
            {
                modifiers.RemoveAt(i);
                removed = true;
            }
        }

        return removed;
    }
}

// Per-entity attribute collection. Base values are the serialized ground truth;
// modifiers are runtime-transient and re-applied by buffs/skills.
[ComponentGraph(Group = "Gameplay/Attributes", Order = 10)]
public struct AttributeSetComponent : IComponent
{
    [OdinSerialize]
    private List<GameplayAttributeInstance>? _attributes;

    public AttributeSetComponent()
    {
        _attributes = [];
    }

    private List<GameplayAttributeInstance> MutableAttributes => _attributes ??= [];

    public IReadOnlyList<GameplayAttributeInstance> Attributes => MutableAttributes;

    public bool TryGet(GameplayAttribute attribute, out GameplayAttributeInstance instance)
    {
        foreach (var candidate in MutableAttributes)
        {
            if (candidate.Attribute == attribute)
            {
                instance = candidate;
                return true;
            }
        }

        instance = null!;
        return false;
    }

    public double GetValue(GameplayAttribute attribute, double fallback = 0)
        => TryGet(attribute, out var instance) ? instance.CurrentValue : fallback;

    public double GetBaseValue(GameplayAttribute attribute, double fallback = 0)
        => TryGet(attribute, out var instance) ? instance.BaseValue : fallback;

    public GameplayAttributeInstance AddAttribute(
        GameplayAttribute attribute,
        double baseValue,
        double minValue = double.NegativeInfinity,
        double maxValue = double.PositiveInfinity)
    {
        if (!attribute.IsValid || !double.IsFinite(baseValue) || double.IsNaN(minValue) || double.IsNaN(maxValue) || minValue > maxValue)
            throw new ArgumentException("Invalid attribute definition.");
        if (TryGet(attribute, out var existing))
        {
            existing.BaseValue = baseValue;
            existing.MinValue = minValue;
            existing.MaxValue = maxValue;
            return existing;
        }

        var instance = new GameplayAttributeInstance(attribute, baseValue, minValue, maxValue);
        MutableAttributes.Add(instance);
        return instance;
    }

    public bool SetBaseValue(GameplayAttribute attribute, double baseValue)
    {
        if (!TryGet(attribute, out var instance))
        {
            return false;
        }

        instance.BaseValue = baseValue;
        return true;
    }

    public AttributeModifier ApplyModifier(AttributeModifier modifier)
    {
        ArgumentNullException.ThrowIfNull(modifier);

        if (!TryGet(modifier.Attribute, out var instance))
        {
            throw new KeyNotFoundException($"Attribute '{modifier.Attribute}' is not present; add it before applying modifiers.");
        }

        instance.ApplyModifier(modifier);
        return modifier;
    }

    public bool RemoveModifiers(object source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var removed = false;
        foreach (var instance in MutableAttributes)
        {
            removed |= instance.RemoveModifiers(source);
        }

        return removed;
    }

    public bool RemoveModifier(AttributeModifier handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        return TryGet(handle.Attribute, out var instance) && instance.RemoveModifier(handle);
    }

    public double ApplyInstant(GameplayAttribute attribute, AttributeModifierOperation operation, double magnitude)
    {
        if (!TryGet(attribute, out var instance)) throw new KeyNotFoundException($"Attribute '{attribute}' is not present.");
        return instance.ApplyInstant(operation, magnitude);
    }

    public bool RemoveModifiers(GameplayAttribute attribute, object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return TryGet(attribute, out var instance) && instance.RemoveModifiers(source);
    }
}
