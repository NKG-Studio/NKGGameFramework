namespace NKGGameFramework.Gameplay;

public interface IBuffEffect
{
    void OnApply(BuffEffectContext context);

    void OnRefresh(BuffEffectContext context);

    void OnUpdate(BuffEffectContext context);

    void OnRemove(BuffEffectContext context);

    void OnActivate(BuffEffectContext context) { }

    void OnDeactivate(BuffEffectContext context) { }
}

public abstract class BuffEffect : IBuffEffect
{
    public virtual void OnActivate(BuffEffectContext context) { }

    public virtual void OnDeactivate(BuffEffectContext context) { }

    public virtual void OnApply(BuffEffectContext context)
    {
    }

    public virtual void OnRefresh(BuffEffectContext context)
    {
    }

    public virtual void OnUpdate(BuffEffectContext context)
    {
    }

    public virtual void OnRemove(BuffEffectContext context)
    {
    }
}

public sealed class DelegateBuffEffect(
    Action<BuffEffectContext>? onApply = null,
    Action<BuffEffectContext>? onRefresh = null,
    Action<BuffEffectContext>? onUpdate = null,
    Action<BuffEffectContext>? onRemove = null) : BuffEffect
{
    public override void OnApply(BuffEffectContext context)
    {
        onApply?.Invoke(context);
    }

    public override void OnRefresh(BuffEffectContext context)
    {
        onRefresh?.Invoke(context);
    }

    public override void OnUpdate(BuffEffectContext context)
    {
        onUpdate?.Invoke(context);
    }

    public override void OnRemove(BuffEffectContext context)
    {
        onRemove?.Invoke(context);
    }
}
