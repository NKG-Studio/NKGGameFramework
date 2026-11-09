using NKGGameFramework.Ecs;

namespace NKGGameFramework.Gameplay;

public sealed class BuffUpdateSystem : EcsSystem
{
    private readonly BuffEffectRegistry _effects;
    private readonly BehaviorActionRegistry _behaviorActions;
    private readonly List<BehaviorTreeInstance> _behaviorTreesToStart = [];
    private readonly List<BehaviorTreeInstance> _behaviorTreesToUpdate = [];
    private readonly List<BehaviorTreeInstance> _behaviorTreesToCancel = [];

    public BuffUpdateSystem(BuffEffectRegistry? effects, int order)
        : this(effects, null, order)
    {
    }

    public BuffUpdateSystem(BuffEffectRegistry? effects = null, BehaviorActionRegistry? behaviorActions = null, int order = 0)
        : base(order)
    {
        _effects = effects ?? BuffEffectRegistry.CreateDefault();
        _behaviorActions = behaviorActions ?? BehaviorActionRegistry.CreateDefault();
    }

    public override void Update(Scene scene, in SystemUpdateContext context)
    {
        var time = context.Time;
        _behaviorTreesToStart.Clear();
        _behaviorTreesToUpdate.Clear();
        _behaviorTreesToCancel.Clear();
        try
        {
            scene.Query<BuffCollectionComponent>().ForEach((ref BuffCollectionComponent collection, Entity entity) =>
            {
                var buffs = collection.MutableBuffs;
                for (var i = buffs.Count - 1; i >= 0; i--)
                {
                    var buff = buffs[i];
                    var effect = _effects.Resolve(buff.Definition.EffectKey);
                    var effectContext = new BuffEffectContext(scene, entity, buff, in time);

                    ProcessBuff(
                        scene,
                        effect,
                        effectContext,
                        _behaviorActions,
                        _behaviorTreesToStart,
                        _behaviorTreesToUpdate);

                    if (buff.State == BuffState.Finished)
                    {
                        if (buff.IsActive)
                        {
                            buff.IsActive = false;
                            effect.OnDeactivate(effectContext);
                        }
                        effect.OnRemove(effectContext);
                        if (buff.ExecutionTreeInstance is { } behaviorTree)
                        {
                            _behaviorTreesToStart.Remove(behaviorTree);
                            _behaviorTreesToUpdate.Remove(behaviorTree);
                            _behaviorTreesToCancel.Add(behaviorTree);
                        }

                        buff.ExecutionTreeInstance = null;
                        scene.Events.Publish(new BuffRemoved(
                            entity.ToRef(),
                            buff.Source,
                            buff.Definition.Id,
                            buff.Level,
                            buff.Stacks));
                        buffs.RemoveAt(i);
                    }
                }
            });

            foreach (var behaviorTree in _behaviorTreesToStart)
            {
                behaviorTree.Start();
            }

            foreach (var behaviorTree in _behaviorTreesToUpdate)
            {
                behaviorTree.Update(in time);
            }

            foreach (var behaviorTree in _behaviorTreesToCancel)
            {
                behaviorTree.Cancel();
            }
        }
        finally
        {
            _behaviorTreesToStart.Clear();
            _behaviorTreesToUpdate.Clear();
            _behaviorTreesToCancel.Clear();
        }
    }

    private static void ProcessBuff(
        Scene scene,
        IBuffEffect effect,
        BuffEffectContext context,
        BehaviorActionRegistry behaviorActions,
        List<BehaviorTreeInstance> behaviorTreesToStart,
        List<BehaviorTreeInstance> behaviorTreesToUpdate)
    {
        var buff = context.Buff;

        if (buff.State == BuffState.Waiting)
        {
            buff.ClearPendingRefresh();
            effect.OnApply(context);
            if (buff.State != BuffState.Finished && !buff.IsSuppressed)
            {
                buff.IsActive = true;
                effect.OnActivate(context);
                if (buff.State != BuffState.Finished)
                    StartExecutionTree(scene, context, behaviorActions, behaviorTreesToStart);
            }
            scene.Events.Publish(new BuffApplied(
                context.Target.ToRef(),
                buff.Source,
                buff.Definition.Id,
                buff.Level,
                buff.Stacks));

            if (buff.State == BuffState.Waiting) buff.State = buff.Definition.Duration switch
            {
                null => BuffState.Forever,
                { } duration when duration == TimeSpan.Zero => BuffState.Finished,
                _ => BuffState.Running,
            };
        }
        else if (buff.State != BuffState.Finished && buff.PendingRefresh)
        {
            buff.ClearPendingRefresh();
            effect.OnRefresh(context);
            scene.Events.Publish(new BuffRefreshed(
                context.Target.ToRef(),
                buff.Source,
                buff.Definition.Id,
                buff.Level,
                buff.Stacks));
        }

        if (buff.State is BuffState.Running or BuffState.Forever)
        {
            if (buff.IsActive == buff.IsSuppressed)
            {
                buff.IsActive = !buff.IsSuppressed;
                if (buff.IsActive)
                {
                    effect.OnActivate(context);
                    if (buff.State != BuffState.Finished && buff.ExecutionTreeInstance is null)
                        StartExecutionTree(scene, context, behaviorActions, behaviorTreesToStart);
                }
                else effect.OnDeactivate(context);
            }
            if (buff.IsActive && buff.State != BuffState.Finished) effect.OnUpdate(context);
            buff.Tick(context.DeltaTime);

            if (buff.ExecutionTreeInstance is { } behaviorTree)
            {
                if (buff.IsActive && buff.State is BuffState.Running or BuffState.Forever)
                {
                    behaviorTreesToUpdate.Add(behaviorTree);
                }
            }
        }
    }

    private static void StartExecutionTree(
        Scene scene,
        BuffEffectContext context,
        BehaviorActionRegistry behaviorActions,
        List<BehaviorTreeInstance> behaviorTreesToStart)
    {
        var definition = context.Buff.Definition.ExecutionTree;
        if (definition is null)
        {
            return;
        }

        if (!definition.TryValidate(behaviorActions, out var missingActionKey))
        {
            throw new KeyNotFoundException($"Behavior action '{missingActionKey}' is not registered.");
        }

        var source = context.Target;
        if (context.Buff.Source.TryGet(out var resolvedSource))
        {
            source = resolvedSource;
        }

        var behaviorContext = new BehaviorTreeContext(scene, source, context.Target, buffInstance: context.Buff);
        var instance = definition.CreateInstance(behaviorActions, behaviorContext);
        context.Buff.ExecutionTreeInstance = instance;
        behaviorTreesToStart.Add(instance);
    }
}
