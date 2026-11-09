using NKGGameFramework.Ecs;
using NKGGameFramework.Gameplay;
using NKGGameFramework.Serialization;

namespace NKGGameFramework.Tests.Gameplay;

public sealed class AttributeChannelTests
{
    private static readonly GameplayAttribute Attack = GameplayAttribute.From("Attack");
    private static readonly GameplayAttribute Health = GameplayAttribute.From("Health");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Channels_and_operations_do_not_depend_on_application_order(bool reverse)
    {
        var attack = new GameplayAttributeInstance(Attack, 10);
        AttributeModifier[] modifiers =
        [
            new() { Attribute = Attack, Channel = 0, Magnitude = 5 },
            new() { Attribute = Attack, Channel = 1, Operation = AttributeModifierOperation.Multiply, Magnitude = 2 },
            new() { Attribute = Attack, Channel = 2, Operation = AttributeModifierOperation.Multiply, Magnitude = .9 }
        ];
        foreach (var modifier in reverse ? modifiers.Reverse() : modifiers) attack.ApplyModifier(modifier);
        Assert.Equal(27, attack.CurrentValue);
        attack.BaseValue = 15;
        Assert.Equal(36, attack.CurrentValue);
    }

    [Fact]
    public void Adds_precede_multipliers_within_one_channel_and_override_only_replaces_its_channel()
    {
        var attack = new GameplayAttributeInstance(Attack, 10);
        attack.ApplyModifier(new() { Attribute = Attack, Magnitude = 2, Operation = AttributeModifierOperation.Multiply });
        attack.ApplyModifier(new() { Attribute = Attack, Magnitude = 5 });
        Assert.Equal(30, attack.CurrentValue);
        attack.ApplyModifier(new() { Attribute = Attack, Magnitude = 7, Operation = AttributeModifierOperation.Override });
        attack.ApplyModifier(new() { Attribute = Attack, Magnitude = 3, Channel = 1 });
        Assert.Equal(10, attack.CurrentValue);
    }

    [Fact]
    public void Handles_remove_one_grant_and_sources_remove_only_their_own_grants()
    {
        var set = new AttributeSetComponent();
        set.AddAttribute(Attack, 10);
        var source = new object();
        var handle = set.ApplyModifier(new() { Attribute = Attack, Magnitude = 5, Source = source });
        set.ApplyModifier(new() { Attribute = Attack, Magnitude = 7, Source = source });
        set.ApplyModifier(new() { Attribute = Attack, Magnitude = 3, Source = new object() });
        Assert.True(set.RemoveModifier(handle));
        Assert.False(set.RemoveModifier(handle));
        Assert.Equal(20, set.GetValue(Attack));
        Assert.True(set.RemoveModifiers(source));
        Assert.Equal(13, set.GetValue(Attack));
    }

    [Fact]
    public void Live_derivation_recomputes_but_snapshot_does_not()
    {
        var strength = new GameplayAttributeInstance(GameplayAttribute.From("Strength"), 10);
        var attack = new GameplayAttributeInstance(Attack, 5);
        attack.ApplyModifier(new() { Attribute = Attack, Capture = new(strength), Coefficient = 2 });
        attack.ApplyModifier(new() { Attribute = Attack, Capture = new(strength, snapshot: true) });
        Assert.Equal(35, attack.CurrentValue);
        strength.BaseValue = 15;
        Assert.Equal(45, attack.CurrentValue);
    }

    [Fact]
    public void Dynamic_bounds_and_instant_changes_report_actual_delta()
    {
        var maximum = new GameplayAttributeInstance(GameplayAttribute.From("MaxHealth"), 100);
        var health = new GameplayAttributeInstance(Health, 80, 0) { Maximum = new(maximum) };
        Assert.Equal(-80, health.ApplyInstant(AttributeModifierOperation.Add, -200));
        Assert.Equal(100, health.ApplyInstant(AttributeModifierOperation.Add, 200));
        maximum.BaseValue = 60;
        Assert.Equal(60, health.CurrentValue);
        health.ApplyInstant(AttributeModifierOperation.Override, health.CurrentValue);
        maximum.BaseValue = 100;
        Assert.Equal(60, health.CurrentValue);
    }

    [Fact]
    public void Cycles_are_rejected_and_evaluation_recovers_after_removal()
    {
        var a = new GameplayAttributeInstance(Attack, 10);
        var b = new GameplayAttributeInstance(Health, 20);
        a.ApplyModifier(new() { Attribute = Attack, Capture = new(b) });
        var cycle = new AttributeModifier { Attribute = Health, Capture = new(a) };
        b.ApplyModifier(cycle);
        Assert.Throws<InvalidOperationException>(() => a.CurrentValue);
        b.RemoveModifier(cycle);
        Assert.Equal(30, a.CurrentValue);
    }

    [Fact]
    public void Invalid_inputs_do_not_change_existing_values()
    {
        var set = new AttributeSetComponent();
        var attack = set.AddAttribute(Attack, 10, 0, 100);
        Assert.Throws<ArgumentException>(() => set.AddAttribute(Attack, double.NaN));
        Assert.Throws<ArgumentException>(() => set.ApplyModifier(new() { Attribute = Attack, Magnitude = double.PositiveInfinity }));
        Assert.Throws<ArgumentException>(() => attack.ApplyModifier(new() { Attribute = Health, Magnitude = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => attack.ApplyInstant(AttributeModifierOperation.Add, double.NaN));
        Assert.Equal(10, attack.CurrentValue);
    }

    [Fact]
    public void Serialization_preserves_bases_but_not_runtime_grants()
    {
        var set = new AttributeSetComponent();
        set.AddAttribute(Attack, 10, 0, 100);
        set.ApplyModifier(new() { Attribute = Attack, Magnitude = 5, Source = new object() });
        var serializer = new OdinGameSerializer();
        var restored = serializer.Deserialize<AttributeSetComponent>(serializer.Serialize(set));
        Assert.Equal(10, restored.GetValue(Attack));
        Assert.Empty(Assert.Single(restored.Attributes).Modifiers);
        Assert.Equal(15, set.GetValue(Attack));
    }

    [Theory]
    [InlineData(false, 20)]
    [InlineData(true, 17)]
    public void Buff_stacks_refresh_and_expire_without_leaking_grants(bool useStacksAsLevel, double expected)
    {
        using var scene = new Scene("attributes");
        scene.Systems.Add(new BuffUpdateSystem());
        var owner = scene.CreateEntity().Add(new AttributeSetComponent());
        owner.Get<AttributeSetComponent>().AddAttribute(Attack, 10);
        var definition = new BuffDefinition
        {
            Id = "attack", EffectKey = BuffEffectKeys.AttributeModifier, MaxStacks = 3, Duration = TimeSpan.FromSeconds(2),
            AttributeModifiers = [new() { Attribute = Attack, Magnitude = 5, ValuesByLevel = { [2] = 7 },
                ScaleWithStacks = !useStacksAsLevel, UseStacksAsLevel = useStacksAsLevel }]
        };
        BuffManager.Apply(owner, owner, definition);
        scene.Update(0, 0);
        BuffManager.Apply(owner, owner, definition);
        scene.Update(0, 0);
        Assert.Equal(expected, owner.Get<AttributeSetComponent>().GetValue(Attack));
        Assert.Single(Assert.Single(owner.Get<AttributeSetComponent>().Attributes).Modifiers);
        scene.Update(2, 2);
        Assert.Equal(10, owner.Get<AttributeSetComponent>().GetValue(Attack));
    }

    [Fact]
    public void Removing_a_pending_refresh_cannot_reapply_effects()
    {
        using var scene = new Scene("cancel");
        var refreshed = 0;
        var registry = new BuffEffectRegistry().Register("test", new DelegateBuffEffect(onRefresh: _ => refreshed++));
        scene.Systems.Add(new BuffUpdateSystem(registry));
        var owner = scene.CreateEntity();
        var definition = new BuffDefinition { Id = "test", EffectKey = "test", MaxStacks = int.MaxValue };
        BuffManager.Apply(owner, owner, definition, stacks: int.MaxValue);
        scene.Update(0, 0);
        var result = BuffManager.Apply(owner, owner, definition, stacks: int.MaxValue);
        Assert.Equal(int.MaxValue, result.Instance.Stacks);
        BuffManager.Remove(owner, "test");
        scene.Update(0, 0);
        Assert.Equal(0, refreshed);
        Assert.Empty(owner.Get<BuffCollectionComponent>().Buffs);
    }

    [Fact]
    public void Suppression_removes_grants_but_keeps_time_and_restores_latest_stacks()
    {
        using var scene = new Scene("suppress");
        scene.Systems.Add(new BuffUpdateSystem());
        var owner = scene.CreateEntity().Add(new AttributeSetComponent());
        owner.Get<AttributeSetComponent>().AddAttribute(Attack, 10);
        var definition = new BuffDefinition
        {
            Id = "power", EffectKey = BuffEffectKeys.AttributeModifier, Duration = TimeSpan.FromSeconds(3), MaxStacks = 3,
            RefreshPolicy = BuffStackRefreshPolicy.KeepDuration,
            AttributeModifiers = [new() { Attribute = Attack, Magnitude = 5, ScaleWithStacks = true }]
        };
        var buff = BuffManager.Apply(owner, owner, definition).Instance;
        scene.Update(0, 0);
        Assert.Equal(15, owner.Get<AttributeSetComponent>().GetValue(Attack));
        buff.SetSuppressed(true);
        scene.Update(1, 1);
        Assert.False(buff.IsActive);
        Assert.Equal(TimeSpan.FromSeconds(2), buff.RemainingDuration);
        Assert.Equal(10, owner.Get<AttributeSetComponent>().GetValue(Attack));
        BuffManager.Apply(owner, owner, definition);
        scene.Update(0, 1);
        Assert.Equal(10, owner.Get<AttributeSetComponent>().GetValue(Attack));
        buff.SetSuppressed(false);
        scene.Update(0, 1);
        Assert.Equal(20, owner.Get<AttributeSetComponent>().GetValue(Attack));
        buff.SetSuppressed(true);
        scene.Update(2, 3);
        buff.SetSuppressed(false);
        scene.Update(0, 3);
        Assert.Equal(BuffState.Finished, buff.State);
        Assert.Equal(10, owner.Get<AttributeSetComponent>().GetValue(Attack));
    }

    [Fact]
    public void Removal_during_application_is_not_overwritten_by_lifetime_initialization()
    {
        using var scene = new Scene("remove-on-apply");
        var updates = 0;
        var removes = 0;
        var effects = new BuffEffectRegistry().Register("cancel", new DelegateBuffEffect(
            onApply: c => BuffManager.Remove(c.Target, c.Buff.Definition.Id),
            onUpdate: _ => updates++, onRemove: _ => removes++));
        scene.Systems.Add(new BuffUpdateSystem(effects));
        var target = scene.CreateEntity();
        BuffManager.Apply(target, target, new() { Id = "cancel", EffectKey = "cancel" });
        scene.Update(0, 0);
        Assert.Equal(0, updates);
        Assert.Equal(1, removes);
        Assert.Empty(target.Get<BuffCollectionComponent>().Buffs);
    }
}
