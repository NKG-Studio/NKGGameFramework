# 属性与 Buff 运行时

设计参考：[由 GAS 启发的属性与 Buff 系统](https://zhuanlan.zhihu.com/p/2102514265550866406)。这里扩展既有 `GameplayAttribute`、`AttributeSetComponent`、`BuffManager`，不引入引擎资产或第二套 Gameplay 框架。

## 计算规则

属性保留 `BaseValue`，持续修饰器不直接覆盖它。`Channel` 从小到大计算；同一通道先加法、再乘法，该通道有 Override 时采用最后加入的 Override，然后继续后续通道，最后限制上下限。乘法 magnitude 是倍率（1.2 表示增加20%），不是百分比点数。相同通道的加法/乘法结果不依赖装备顺序。

`ApplyModifier` 返回的对象就是该次修饰器的句柄；`RemoveModifier` 精确移除一个，`RemoveModifiers(source)` 按来源对象引用移除一组。刷新 Buff 重新申请该来源的修饰器，因此同通道 Override 的刷新相当于新的加入。

`AttributeCapture` 默认实时读取另一个属性，`snapshot: true` 在创建捕获时冻结值。修饰量为 `Magnitude + Capture.Value * Coefficient`。属性的 `Minimum` / `Maximum` 同样接受捕获。当前实现按读取求值，不维护第二份缓存及订阅图；循环依赖抛异常且可在移除循环后恢复。它适合小型战斗属性集，大规模属性网络若实测读取开销过高，再加入依赖失效缓存。

`ApplyInstant` 对 BaseValue 执行加、乘、覆盖，裁剪后返回实际 CurrentValue 差值，适用于资源消耗等即时变化。非有限数、非法操作和错误目标会拒绝。动态上限只约束 CurrentValue；资源上限下降时，业务应将裁剪后的 CurrentValue 通过一次瞬时 Override 写回 BaseValue，避免上限恢复后返还先前截掉的资源。最大值增加是否补血属于业务联动，不会自动发生。

配置仍使用现有属性标识和 `AddAttribute` 初始化；没有强制替换为 GameplayTag，也没有新增配表解析器。现有标识、存档与调用兼容优先。属性基础值可序列化；修饰器、动态捕获和求值状态是临时数据，加载时由玩法定义重新建立。

## Buff 管理

`AttributeModifierSpec.Channel` 指定计算通道。`ScaleWithStacks` 使每层重复一次运算：加法叠加，乘法连乘，覆盖维持同一值；默认关闭以兼容原有定义。构筑等级等离散数值可以启用 `UseStacksAsLevel`，以叠层数查 `ValuesByLevel`。不要同时开启两种选项，除非确实需要把等级取值再次按层重复。

属性效果仍使用 `BuffEffectKeys.AttributeModifier`。升级时先校验全部新修饰器，再替换同来源旧修饰器；移除或到期只撤销该 Buff 的贡献。

生命周期区分添加/移除 (`OnApply` / `OnRemove`) 和生效/失效 (`OnActivate` / `OnDeactivate`)。`BuffInstance.SetSuppressed` 请求抑制，在下一次 `BuffUpdateSystem` 执行时撤销属性效果并暂停 OnUpdate/行为树推进，但继续消耗剩余时长。解除抑制按最新层数恢复；已到期的 Buff 不会复活。需要立即响应时使用宿主已有的零时长刷新入口。自定义持续效果应在生效/失效钩子中成对管理句柄；既有添加/移除回调保留原用途。标签与技能的免疫门禁仍由已有 `GameplayTagUtility` 负责。

伤害类型、防御公式、吸血与反伤、定时触发策略和空间条件属于玩法。现有上下文/效果接口可承载它们，本次没有新增通用公式语言、Status 编辑器或把同步结算变为无序事件。
