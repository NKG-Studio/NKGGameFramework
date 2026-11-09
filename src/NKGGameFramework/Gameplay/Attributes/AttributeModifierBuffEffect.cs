namespace NKGGameFramework.Gameplay;

// Built-in buff effect that grants attribute modifiers while the buff is active
// and removes them when the buff ends. Modifiers are keyed by the BuffInstance
// so refresh/reapply never double-counts.
public sealed class AttributeModifierBuffEffect : BuffEffect
{
    public override void OnActivate(BuffEffectContext context) => ApplyModifiers(context);

    public override void OnRefresh(BuffEffectContext context)
    {
        if (context.Buff.IsActive) ApplyModifiers(context);
    }

    public override void OnDeactivate(BuffEffectContext context) => RemoveModifiers(context);

    private static void ApplyModifiers(BuffEffectContext context)
    {
        var target = context.Target;
        if (!target.Has<AttributeSetComponent>())
        {
            return;
        }

        ref var set = ref target.Get<AttributeSetComponent>();
        var modifiers = new List<AttributeModifier>();
        foreach (var spec in context.Buff.Definition.AttributeModifiers)
        {
            var magnitude = spec.GetMagnitude(spec.UseStacksAsLevel ? context.Buff.Stacks : context.Buff.Level);
            if (spec.ScaleWithStacks)
                magnitude = spec.Operation switch
                {
                    AttributeModifierOperation.Add => magnitude * context.Buff.Stacks,
                    AttributeModifierOperation.Multiply => Math.Pow(magnitude, context.Buff.Stacks),
                    _ => magnitude
                };
            var modifier = new AttributeModifier
            {
                Attribute = spec.Attribute,
                Operation = spec.Operation,
                Magnitude = magnitude,
                Channel = spec.Channel,
                Source = context.Buff,
            };
            modifier.Validate();
            if (!set.TryGet(modifier.Attribute, out _))
                throw new KeyNotFoundException($"Attribute '{modifier.Attribute}' is not present.");
            modifiers.Add(modifier);
        }
        // Validate the whole grant before replacing the previous one.
        set.RemoveModifiers(context.Buff);
        foreach (var modifier in modifiers) set.ApplyModifier(modifier);
    }

    private static void RemoveModifiers(BuffEffectContext context)
    {
        var target = context.Target;
        if (!target.Has<AttributeSetComponent>())
        {
            return;
        }

        ref var set = ref target.Get<AttributeSetComponent>();
        set.RemoveModifiers(context.Buff);
    }
}
