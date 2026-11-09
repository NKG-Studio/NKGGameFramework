using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BuffInstance
{
    private BuffInstance()
    {
        Definition = null!;
        StackKey = string.Empty;
    }

    internal BuffInstance(
        BuffDefinition definition,
        string stackKey,
        EntityRef source,
        EntityRef target,
        int level,
        int stacks)
    {
        Definition = definition;
        StackKey = stackKey;
        Source = source;
        Target = target;
        Level = level;
        Stacks = Math.Clamp(stacks, 1, definition.MaxStacks);
        RemainingDuration = definition.Duration;
    }

    public BuffDefinition Definition { get; }

    public string StackKey { get; }

    public EntityRef Source { get; }

    public EntityRef Target { get; }

    public int Level { get; }

    public int Stacks { get; private set; }

    public TimeSpan? RemainingDuration { get; private set; }

    public BuffState State { get; internal set; } = BuffState.Waiting;

    public bool IsActive { get; internal set; }

    public bool IsSuppressed { get; private set; }

    // Suppression is reconciled by BuffUpdateSystem; the lifetime clock keeps running.
    public void SetSuppressed(bool suppressed) => IsSuppressed = suppressed;

    [field: NonSerialized]
    public object? UserState { get; set; }

    internal BehaviorTreeInstance? ExecutionTreeInstance { get; set; }

    internal bool PendingRefresh { get; private set; }

    internal void Refresh(int stacks)
    {
        Stacks = (int)Math.Clamp((long)Stacks + stacks, 1, Definition.MaxStacks);

        if (Definition.RefreshPolicy == BuffStackRefreshPolicy.RefreshDuration)
        {
            RemainingDuration = Definition.Duration;
        }

        PendingRefresh = true;
    }

    internal void ClearPendingRefresh()
    {
        PendingRefresh = false;
    }

    internal void Tick(TimeSpan deltaTime)
    {
        if (State != BuffState.Running || RemainingDuration is null)
        {
            return;
        }

        RemainingDuration -= deltaTime;
        if (RemainingDuration <= TimeSpan.Zero)
        {
            RemainingDuration = TimeSpan.Zero;
            State = BuffState.Finished;
        }
    }
}
