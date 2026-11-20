# 双人前后排 Mod · v0.2.0 实验原型

目标：《杀戮尖塔2》双人联机中，两个角色在画面上真实前后站立；双方通过按钮交换位置，默认前排受到怪物攻击伤害 ×1.5，后排 ×0.5，可在游戏设置中调整。

**当前状态：已编译，29 项自动检查通过；设置界面和真实双端联机仍待游戏内验证。不是已验证的稳定发布版。**

## 1. 本机环境

- 游戏路径：`D:\steam\steamapps\common\Slay the Spire 2`
- `release_info.json`：`v0.113.1`，commit `d09d48e5`，2026-11-14。
- Steam：`public-beta` 分支，build ID `26660232`。
- 游戏使用 .NET 9；本机 SDK 为 .NET 10.0.402，可以编译 net9.0。
- 自动检查使用项目内 `.tools/runtime9` 的 .NET 9.0.7，与本机游戏一致。
- 直接引用游戏自带的 `sts2.dll`、`GodotSharp.dll`、`0Harmony.dll`。**玩家无需安装 BaseLib、KitLib、RitsuLib 或其他前置 Mod。** 不需要导出 PCK，也不需要启动 Godot 编辑器。双方需要相同版本的游戏和本 Mod。

社区模板可以参考，但接口以本机程序集核对结果为准。升级游戏后必须重新编译和测试。

## 2. 已落实的规则

- 双方都能点击，换位次数不限；限玩家可出牌阶段操作。怪物行动、战斗结束和任一玩家死亡时禁用换位。
- 怪物原本攻击谁保持原样；前排不负责自动承接后排的攻击。
- 每场战斗初始前排为稳定玩家 ID 中较小的一位，与谁是本机玩家无关。每场战斗重新初始化。
- 换位提交到原版 `ActionQueueSynchronizer`；各端只在动作实际执行时改变站位。两次有效点击代表换两次，最终回到原位。
- 只对怪物造成、原始目标为玩家、带 `Move` 标记的正常攻击生效。自伤、毒等生命流失、非攻击效果及直接打向宠物的伤害保持原版。
- 先执行原版加算和乘算，再乘站位倍率，再执行原版伤害上限，最后由游戏处理格挡、重定向和取整。已计算后仅执行 Cap 的伤害不会重复乘算。
- 示例：怪物正常攻击 20、玩家有 8 点格挡，前排扣血 `30-8=22`，后排扣血 `10-8=2`。无格挡时，后排受到 5 点攻击，游戏最终扣 2 点血。
- UI 移动整个 `NCreature`，不只移动立绘；使用原版计算的左右落点并增加纵向错位，宠物跟随所属玩家的位移。

## 3. 按文件逐步读 C#

1. `src/FrontBack/Formation.cs`：纯规则。`ulong` 是玩家编号，`decimal` 用来精确表示 1.5 和 0.5；`FrontId` 记录谁在前排，`Swap()` 把它改成另一位玩家。
2. `DamagePatch.cs`：Harmony 向游戏原伤害方法插入一次倍率计算。保留小数交给原版结算，避免提前取整。
3. `SwapAction.cs`：按钮不直接改状态，而是把“请求换位”包装成 `GameAction`。`NetSwapAction.Serialize` 写入回合信息，另一端 `Deserialize` 读出，随后由游戏动作队列执行。旧回合请求会被拒绝。
4. `CombatOverlay.cs`：使用 Godot 的 `Button`、`Label`、`VBoxContainer` 添加 UI，定时刷新可用状态。
5. `FormationVisuals.cs`：在原版布局完成后记下落点，按身份重新分配；自己的换位动画可取消，不重复叠加坐标。
6. `ModEntry.cs`：游戏加载入口与每场战斗的状态存储。`ConditionalWeakTable` 让旧战斗对象释放时，相应状态也能释放。

阅读顺序按上面来；先理解 `Formation.cs` 的两个 ID 和一个 `Swap()` 即可。

## 4. 配置和重新编译

游戏内打开 **设置 → 常规 → 前后排伤害设置 / FrontBack**（位于常规设置列表底部），分别输入前排、后排百分比后点击“保存”。支持 0–10000%，精确到 0.1%；100% 为原版伤害，0% 为免除此类怪物攻击伤害。可恢复默认 150% / 50%。

每场双人战斗开始前，使用游戏原生玩家选择通道同步房主设置，双方接收同一份战斗倍率后才继续原版开战效果。保存新数值从下一场战斗开始生效，当前战斗的倍率保持不变。客机保存自己的偏好，供以后自己当房主使用。战斗标签和状态文字显示当前战斗实际倍率。

伤害设置保存在游戏用户数据目录的 `mod_data/FrontBack/damage-settings.json`，不放在 Mod 安装目录，替换 Mod 文件时会保留。`FrontBack.config.json` 仍只控制本地画面：`RowOffsetY` 是上下错位距离，`SwapAnimationSeconds` 是动画秒数，`ButtonX/Y` 是按钮位置；修改后重启。

需要重新编译时，在项目目录的 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Check.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

首次还原依赖需要联网。如果换电脑，需安装 .NET SDK 与 .NET 9 运行环境，或重新准备项目内运行环境。`Check.ps1` 优先使用 `.tools/runtime9/dotnet.exe`；不要用 .NET 10 执行游戏的旧 Harmony。游戏路径不同时，给 Build/Check 传 `-GameDir '你的游戏目录'`。

输出包不含游戏程序集、游戏资源或测试工具。`local.props.example` 可复制为 `local.props` 后设置自己的路径。

## 5. 下一阶段和发布条件

必须先完成双端实测，重点覆盖倍率同步、同时点击、攻击/受击动画、宠物与复活、窗口缩放、易伤/虚弱、多段与群体攻击。当前没有增加站位专用存档字段或校验和字段；战斗重建会重置站位，断线重连、战斗回放和其他 Mod 的兼容性还未验证。

完成这些测试后，再补联机诊断、必要的状态恢复、UI 适配与版本支持说明，制作创意工坊预览图。随后使用 Mega Crit 官方 `sts2-mod-uploader` 准备上传工作区、填写 `workshop.json` 并上传；本原型尚未上传 Steam 创意工坊。

参考：[社区 Mod 模板](https://github.com/Alchyr/ModTemplate-StS2)、[Mega Crit 官方上传工具](https://github.com/megacrit/sts2-mod-uploader)。
