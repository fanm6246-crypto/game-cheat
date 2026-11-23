# Worst Class Ever —— 自制外挂 mod（wcehack）

走游戏**官方 Mod 系统 + 自带 Harmony**，不碰游戏文件、不改内存、不需要 BepInEx、不会被 Steam 校验。

---

## 最新：v2.1（2026-11-21）

**强制变老师已移除**，换成了**投掷物穿墙**。

### 穿墙：粉笔 / 黑板擦 / 剑气 无视墙体

游戏里三套投掷物的碰撞层是分开的，得一个一个改：

| 投掷物 | 用的 mask | 原始含义 | 改成 |
|---|---|---|---|
| 粉笔、黑板擦 | `ConstantDefine.ChalkColliderMask` | `~(Interaction\|Player\|PlayerSelf)` —— **Wall / Ground / Obstacle / Door 全在里面**，所以撞墙就停 | `Interaction\|Player\|PlayerSelf\|HitBox` |
| 剑气 | `TeacherSlashProjectile.EnvironmentMask` | 撞环境就消失 | `0`（不吃环境） |
| 黑板擦 | `TeacherSlashProjectile.EraserEnvironmentMask` | 撞环境就消失 | `0` |
| 黑板擦（找目标） | `TeacherSlashProjectile.EraserCollisionMask` | 只认角色 | 换成角色层 mask |
| 黑板擦/剑气飞行检测 | `ProjectileFlightQuery` 的构造参数 `mask` | 由调用方给 | prefix 改成角色层 |

这几个字段全是 `static initonly`，实测**在 Mono 下反射 `SetValue` 是能写进去的**
（和之前 `cfg.Unit.SubType` 那个坑不一样——那个是实例只读字段）。

运行时日志：
```
[WceHack] patched ProjectileFlightQuery..ctor
[WceHack] [WceHack] 穿墙开 mask=35008
```
`35008 = Interaction|Player|PlayerSelf|HitBox` 四个层的位掩码。

### 按键

- **数字键 `1`** 开关穿墙（原来的 `\` 已废弃）
- 关的时候会把原始 mask 全部写回去，不会残留

实测切换日志：
```
[WceHack] 穿墙 关
[WceHack] 穿墙已恢复原 mask
[WceHack] 穿墙 开（粉笔/黑板擦/剑气）
[WceHack] 穿墙开 mask=35008
```

---

## v1.9.1（已移除：强制变老师）

**强制变老师（按 `\`）**

`Unit.IsTeacher` 的唯一判据是 `RoleData.UnitTable.SubType == 101`。所以做法是把
`RoleData.UnitTable` 换成一份额配置。

一开始想的是「克隆自己那份 + 反射改 `SubType`」——**这条路在 Mono 下走不通**：
`cfg.Unit.SubType` / `cfg.Unit.Type` 都是 `initonly`，`FieldInfo.SetValue` 在 Mono 运行时
写不进去，抛异常被 catch 掉，而且当时失败分支没打日志，所以表现就是「按了没反应」。

v1.9.1 改成**直接借配置表里现成的老师单位**：

```csharp
cfg.TbUnit tb = cfg.Tables.TbUnit;          // public static 字段
List<cfg.Unit> list = tb.DataList;          // public 属性
// 找第一个 (int)u.SubType == 101 → 直接 rd.UnitTable = u
```

不写任何只读字段，零反射。然后补 5 个老师专属组件（池子里取，`Unit.AddComponent` 会自动 `Init(owner)`）：

| 组件 | 作用 |
|---|---|
| `SkillComponent` | 粉笔 / 黑板擦技能 |
| `TeacherSprintComponent` | 冲刺 |
| `TeacherStunComponent` | 老师眩晕态 |
| `TeacherExhaustionComponent` | 老师体力/疲劳 |
| `TeacherMeleeComponent` | 剑气（近战挥砍） |

实测日志：
```
[WceHack] add SkillComponent
[WceHack] add TeacherSprintComponent
[WceHack] add TeacherStunComponent
[WceHack] add TeacherExhaustionComponent
[WceHack] add TeacherMeleeComponent
[WceHack] 已变老师(表 id=1001) IsTeacher=True 补组件+5
```
变完之后屏幕**右下角立刻出现「扔粉笔 / 黑板擦 / LEFT SHIFT 奔跑」**（老师专属 HUD），
菜单「身份」也从「学生」变「老师」。

**热键 / 备用触发**

- 按 `\`（反斜杠）→ 变老师
- 键盘不灵时用文件触发：往 `游戏目录\Mods\wcehack\cmd.txt` 写一行，0.5 秒内执行并自动删文件

| cmd.txt 内容 | 效果 |
|---|---|
| `teacher` | 强制变老师 |
| `who` | 打印自己的身份 / SubType / UID |
| `esp` | 开关透视 |
| `ghost` | 开关完全隐形 |
| `escape` | 立即脱困 |
| `speed` | 开关加速 |

> 注意：这是**本地伪装**。服务端（房主）眼里你还是学生，别人的画面里你也是学生；
> 但你自己这边 UI、技能、移动、碰撞判定全都按老师走。

---

## v1.8.0（2026-11-21）

**① 加速 x2 之前真的没生效，现在修好了**

以前挂在 `MovementSetting.runningFactor` 上——可那个属性只在**跑步**分支里被读（走路那支直接取 `1f`），
所以站着走的你根本没感觉。真正的速度函数是 `InputComponent.GetCurrentMoveSpeed()`：

```
V_0 = get_BaseMoveSpeed() + speedBoost.ActiveBonus     // 基础速度 + 速度药水
V_1 = inputDir.z < 0 ? Clamp01(backwardFactor) : 1f    // 后退减速
… 匍匐 / 灭火 / 身体尺寸 等系数 …
```

`get_BaseMoveSpeed()` 老师读 `RoomRuleDataManager.GetCurrentFloatValue(ERoomRuleId=8)`、学生读 `ERoomRuleId=14`。
现在直接 postfix 乘系数，并且**只对自己**（`__instance.owner.isLocalPlayer`）。

实测（游戏内按 F8 后走路，Player.log）：
```
[WceHack] speed 3.00 -> 6.00
[WceHack] speed 2.40 -> 4.80
```
3.0 走速变 6.0、2.4（后退/匍匐）变 4.8，正好 ×2。

**② 自动脱困不再把人甩出地图**

以前 `DoEscape()` 每次解完状态都无条件 `SetPosition(BornPosition)`——而 `BornPosition` 在某些场景是
没落到地面的坐标，于是人直接被丢到地图外。现在改成：**只清状态、不挪人**；
只有当 `cur.y < -8f || cur.y > 300f`（真的掉出地图了）才救回来，而且先朝下打一条
`Physics.Raycast(…, 80f)` 找地面、落点用 `hit.point + up*0.3`，命中不到就再拿当前位置当锚点找一遍。

---

## 0. exe 版（推荐，双击即用）

```
WCE-Hack.exe        59,392 字节，单文件，绿色，无需安装（内嵌 v1.8.0）
```

| 键 | 功能 | 默认 |
|---|---|---|
| F1 | 透视 ESP（含名字/距离/状态） | 开 |
| F2 | 中文菜单面板 | 开 |
| F3 | 粉笔散射（霰弹） | 开，7 颗 |
| F4 | 速射（无视技能/近战冷却） | 开 |
| F5 / F6 | 传送到最近老师 / 学生 | — |
| F7 | 无限冲刺 | 开 |
| F8 | 移动加速 ×2 | 关 |
| F9 | 头顶显示真实名字 | — |
| F10 | 显示 NPC 学生 | 关 |
| F11 | 模型染色（学生绿 / 老师蓝 / NPC 灰） | 开 |
| **End** | 染色框**透墙**（被挡住的用亮边+高透明度） | 开 |
| **Home** | 无限体力（技能能量 9999 + 冲刺 + 老师疲劳清零，**只对自己**） | 开 |
| Ins | 自动脱困（解绑惩罚柱 / 挣脱抓捕 / 打断眩晕 / 回出生点） | 开 |
| Del | 完全隐形（网络字段 + 渲染全关，连影子都没有） | 关 |
| `[` / `]` | 散射颗数 2~25 | 7 |

染色走 URP：`MaterialPropertyBlock` 写 `_BaseColor` + `material.color` 兜底，
日志实测 `paint shader=Universal Render Pipeline/Lit ... prop-block+material`。
ESP 的染色框是 GUI 层绘制，永远画在最前面 → 天然透墙。

---

## 0. exe 版（推荐，双击即用）

```
WCE-Hack.exe        58,880 字节，单文件，绿色，无需安装（内嵌 v1.7.0）
```

双击 → 点「启动游戏（带挂）」完事。它自己完成这些：

1. 从自身资源里释放 `WceHack.dll` + `mod.json` 到 `游戏安装目录\Mods\wcehack\`
2. 修补 `load-order.json`（把 wcehack 设成 `enabled=true` / `missing=false`，**保留你已有的其它 mod**）
3. 通过 Steam `-applaunch 5175310` 拉起游戏

**注意**：游戏运行时 `WceHack.dll` 被 `Assembly.LoadFrom` 锁住，点部署会提示
「游戏正在运行，WceHack.dll 被占用 —— 请先完全退出游戏再点部署。」
状态行会显示橙色「已部署 vX → 可升级 vY」提醒你该升级了。

界面里的状态灯含义：

| 行 | 绿色 | 橙色 / 红色 |
|---|---|---|
| 游戏进程 | 未运行（可以启动） | 橙色「运行中」= 需要先退出 |
| 外挂包 | 已部署 v1.7.0 | 红「未部署」= 点 ① |
| 启用状态 | 已启用 | 红「未启用」= 点 ① |

其它按钮：`看日志`（定位 Player.log）、`打开 Mods 目录`、`重新部署外挂包`、`退出`。

自检开关（排障用）：
```
WCE-Hack.exe --selftest     → 同目录生成 selftest.txt，打印路径/部署/enabled 结果
```

实现说明：`launcher/src/Launcher.cs`（C#5 / WinForms / .NET Framework 4.0，系统自带），
`launcher/app.manifest` 为 `asInvoker`（不弹 UAC，因为 Steam 游戏目录对当前用户可写）。
重新编译：
```
cd launcher
csc /target:winexe /langversion:5 /out:..\WCE-Hack.exe /win32manifest:app.manifest ^
    /resource:payload\WceHack.dll,payload.dll /resource:payload\mod.json,payload.json ^
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ^
    /r:System.Web.Extensions.dll src\Launcher.cs
```

> `启动带挂.bat` 是纯脚本版（bat + ps1），功能同上，留着当备份。

---

## 1. 现在什么状态

| 项目 | 状态 |
|---|---|
| mod 编译 | ✅ `WceHack.dll` 39,936 字节（v1.7.0） |
| 契约校验（public / IMod / 无参构造 / OnLoad） | ✅ Cecil 验证通过 |
| 包被游戏发现（ModCatalog.Discover） | ✅ packageKey = `local:wcehack:wcehack` |
| **运行时加载（Assembly.LoadFrom + OnLoad）** | ✅ 日志实测 `[Mods] Loaded wcehack 1.7.0` |
| Harmony patch | ✅ 17 个全部 `patched ...`，无 `no postfix` 报错 |
| 粉笔散射 | ✅ 日志实测 `chalk hook scatter=True result=True id=4002 pellets=7` |
| 模型染色 | ✅ 日志实测 `paint shader=Universal Render Pipeline/Lit ... prop-block+material` |
| ESP / 传送 / 菜单的实际画面 | ✅ 截图见 `out\v7_scatter.png`（左上角中文面板） |

实测日志（旁路实例，游戏开着第二个进程验证的，没打断你那一局）：

```
[WceHack] OnLoad begin
[WceHack] behaviour attached
[WceHack] patched TeacherSprintComponent.EnergyBlocksSprint
[WceHack] patched TeacherSprintComponent.CanSprint
[WceHack] patched TeacherSprintComponent.ShouldDrainSprintEnergy
[WceHack] patched MovementSetting.runningFactor
[WceHack] patched MovementSetting.teacherRunwayFactor
[WceHack] patched MovementSetting.studentRunwayFactor
[WceHack] harmony patches installed
[Mods] Loaded wcehack 1.0.0
```

---

## 2. 怎么用

1. **退出当前这局游戏**（你是房主，重启会把房里的人踢掉，自己想好时机）。
2. 如果你直接重启，通常就生效了；万一菜单里没反应，先跑一次：
   ```
   powershell -NoProfile -ExecutionPolicy Bypass -File mod\enable_mod.ps1
   ```
   再启动游戏。（游戏退出时可能用自己的缓存重写 `load-order.json`，脚本会把它修回 `enabled=true`。）
3. 进游戏后确认 `Player.log` 有 `[Mods] Loaded wcehack`（或直接按 F2 看面板）。

### 热键（v1.5.0）

| 键 | 功能 |
|---|---|
| F1 | 透视 ESP（`[师]` 红 / `[生]` 绿 / `[NPC]` 灰） |
| F2 | 中文菜单面板 |
| F3 | **散射（霰弹）**（默认开）—— 一次粉笔打出 7 颗，锥形散布 |
| F4 | **速射（无冷却）**（默认开）—— 技能冷却一直为 0，按住就连发 |
| **`[`** / **`]`** | 散射颗数 −2 / +2（范围 2~25） |
| F5 | 传送到最近的老师身边 |
| F6 | 传送到最近的学生身边 |
| F7 | 无限冲刺（默认开） |
| F8 | 移动速度 ×2 |
| F9 | 打开 `debug_showName`（头顶调试名） |
| F10 | 显示 NPC 学生 |
| F11 | **模型染色**（默认开）—— 学生整体变绿、老师变蓝、NPC 灰（整个模型换色，不是画框） |
| F12 | 把场上每个单位的模型结构写进 `Player.log`（骨骼没出来时排障用） |
| **Insert** | **自动脱困**（默认开）—— 被抓 / 绑柱 / 眩晕自动挣脱并脱身 |
| **Delete** | **完全隐形**（默认关）—— 网段标记 + 本地渲染全关（连剪影影子都没有） |

### 模型染色（v1.5.0）

不再是画骨骼线，而是**直接把模型整体换色**：

| 身份 | 颜色 | RGB |
|---|---|---|
| 学生 | 绿 | `(0.10, 1.00, 0.30)` |
| 老师 | 蓝 | `(0.10, 0.55, 1.00)` |
| NPC 学生 | 灰 | `(0.80, 0.80, 0.80)` |

每 0.25 秒扫一遍 `UnitManager.Instance.unitsDict`，对每个单位 `model.transform.GetComponentsInChildren<Renderer>(true)` 拿全部渲染器（身体/衣服/头发都算），然后两条路一起上：

1. `MaterialPropertyBlock` 设 `_BaseColor` / `_Color` / `_TintColor`（不动共享材质，最省）
2. `renderer.material.color = c`（材质兜底 —— 实测游戏用的是 **`Universal Render Pipeline/Lit`**，`color` 直接映射 `_BaseColor`，稳）

第一次染色会往 `Player.log` 写一行 `[WceHack] paint shader=Universal Render Pipeline/Lit mat=body1` 方便排障喵。
按 F11 关掉会**把原色写回**（`_orig` 里存了每个 renderer 的原始 `_BaseColor`），不会把人染成永久绿喵。

### 无限体力：只对自己生效（v1.5.0）

之前 `TryUseAttackEnergy` 是全局掐掉的，等于帮对手也省体力。现在改成先判断归属：

```csharp
var o = __instance.owner;
if (o == null || !o.isLocalPlayer) return true;   // 别人的照常扣，直接放行原方法
__result = true;  return false;                    // 只有自己直接返回 true
```

冲刺那三条（`EnergyBlocksSprint` / `CanSprint` / `ShouldDrainSprintEnergy`）也一起加了 `owner.isLocalPlayer` 判断 —— 只给自己的冲刺解锁，别人该累还是累喵。

### 散射 / 速射（v1.4.0）

自动瞄准和追踪在 v1.4 里**默认关掉了**（代码还在，没删，只是不启用），改成两把更直接的武器：

**散射**：`SkillComponent.ShootChalk(int fuID, float speed, float duration)` 是你每次扔粉笔都会走的方法。本喵在它**跑完之后**再补 `Pellets-1` 颗：

```csharp
UnitManager.RefAction act = delegate(ref UnitDataNet arg) {
    arg.CreaterUID   = 你的 UID;
    arg.ShootForward = 原方向 + 锥形随机偏移;   // 均匀圆盘采样，半角 7°
    arg.ShootSpeed   = 原来的弹速;
    arg.EffectDuration = 原来的存活时间;
    arg.IgnoreSync   = true;
};
UnitManager.Instance.CreateCommonUnitDirectly(4002, 相机位置, Vector3.zero, act, true);
```

写着跟游戏自己的 `OnRefAction` 一模一样，只把方向换成散布方向 —— 所以每颗粉笔的飞行、命中、伤害判定全是原版逻辑，服务器看不出异常喵。

**速射**：`DealSkillEffect()` 第一句就是 `if (skillCoolDown > 0) return;`。所以只要每 50ms 把 `skillCoolDown`（**public 字段**）压成 0，这个判断就永远放行。同时把 `TeacherMeleeComponent.attackCooldownRemaining`（剑气冷却，private，反射写）和 `debugNoCooldown`/`debugRightSkillNoCooldown` 一并打开。

**无限体力**：`TryUseAttackEnergy(ERoomRuleId)` 直接 prefix 返回 true —— 粉笔和技能都不消耗攻击能量，不然散射 7 颗会把能量瞬间抽干喵。

### 骨骼透视（v1.2.0）

### 自动脱困（v1.3.0）

每 0.2 秒检查一次自己，命中任一情况就强制解开：

| 状态 | 处理 |
|---|---|
| 眩晕 | `StudentStunComponent.InterruptStun()` 打断眩晕计时 |
| 被老师抓住 | `Unit.ClearMountingPos(true, true)` 解除挂载 + `BeCatchedComponent.OnGetReleased(null, null)` 本地打断抓取演出 |
| 被绑惩罚柱 | 遍历场上所有 `PunishPillarComponent`，找到 `BoundStudentUID == 自己` 的那根 → `ReleaseStudent()` 发解绑请求 + `OnResponseReleaseStudent()` 本地立即清绑定 |

脱身之后再 `SetPosition(BornPosition 或 当前位置往后 1.2m)`，直接闪出老师的攻击范围。
`SkillMessageHandler.OnStudentReleaseMessageRequest` 会拒掉「学生自己请求解柱」，所以本地强制 + 服务器确认两条一起走。

### 完全隐形（v1.3.0）

三管齐下，客户端和别人的客户端都看不到：

1. **网段标记**：`RoleData.NetData.IsInvisible = true`（游戏自己的隐形字段，别人拿到的同步数据就是这个）
2. **网络广播**：每 0.5 秒调一次 `PlayerInvisibilityComponent.SetInvisible(true)`，走 `PlayerInvisibilityRequest_C2S`
3. **本地渲染**：把自己身上所有 `Renderer.enabled = false`，连隐形药水的剪影/影子都不留

关掉（再按一次 Delete）会恢复所有 Renderer，并把 `IsInvisible` 写回 false。

### 自动瞄准 / 追踪是怎么做的（v1.3.0 加了提前量 + 平滑转向）


### 骨骼透视（v1.2.0）

三级回退，谁有骨骼用谁：

1. **Humanoid**：`Animator.isHuman == true` → `GetBoneTransform()` 拿 16 个标准骨点（Head/Neck/Chest/Hips/双臂三段/双腿三段），画连线 + 关节点
2. **蒙皮骨骼链**：遍历 `SkinnedMeshRenderer.bones`，凡是父子都在骨骼集合里的就画一条线（这条最通用，Rig 长什么样就画成什么样）
3. **挂点兜底**：`ObjectModel.Parts` 的 `HeadPosition` / `HitBox_Body` / `HandPosition_L` / `HandPosition` / `HitBox_Head` 连线

手指/脚趾/眼睛/头发/扭转骨会自动跳过（名字含 `finger` `thumb` `toe` `eye` `hair` `twist` 等），不会糊成一团毛。
骨架颜色跟随身份：老师红、学生绿、NPC 灰。


ESP 画的是：方框 + 头部点 + 到屏幕底部的追踪线 + 距离 + `晕/坐/床/死` 状态，射程上限 250m。
菜单在左上角，蓝色=开、灰色=关，直接看得到每一项状态。

### 自动瞄准 / 追踪是怎么做的（v1.3.0 加了提前量 + 平滑转向）

**提前量**：目标在跑，直着打会打空。所以按 `t = 距离 / 弹速` 迭代两次预测落点（目标位置 + 速度 × t），最多预判 2.5 秒；目标速度用 0.1s 差分算，再做 Lerp 平滑，避免抖动。

**锁定保持**：一旦锁上某个学生，1.5 秒内不会换目标（除非他死了/走远了），防止两个学生并排时弹道来回摆。

**平滑转向**：飞行中每帧最多转 720°/秒（`Vector3.RotateTowards`），不会瞬间 180° 掉头导致弹道看起来像瞬移。

不改内存、不外挂驱动，全部是 Harmony 打在游戏的**发射函数**上：

| patch 目标 | 干什么 |
|---|---|
| `TeacherMeleeComponent.CreateShot(TeacherSlashFire_C2S)` | 剑气出手瞬间，把方向改成指向最近学生 |
| `TeacherSlashProjectile.ApplyFire(TeacherSlashFired_S2C)` | 服务端/客户端确认那一帧再纠一次方向 |
| `SkillComponent.TryCreateEraserShot(...)` | 黑板擦出手方向锁人 |
| `TeacherSlashProjectile.AdvanceOwned(float)` | 剑气飞行途中每帧转向（**拐弯追踪**） |
| `SkillComponent.OnRefAction(UnitDataNet&)` | 粉笔：把 `ShootForward` 改成锁人方向 |
| `ProjectileComponent.OnFixedUpdate(float)` | 粉笔/抛掷物飞行途中每帧转向（**拐弯追踪**） |

---

## 2.5 升级到最新版

dll 被游戏进程锁着，**运行时无法覆盖**。做法：

1. 完全退出游戏（Steam 里显示「已停止」）
2. 双击 `WCE-Hack.exe` → 「外挂包」那行会显示 `已部署 vX → 可升级 vY`
3. 点「① 部署 / 修复」→ 变成绿色 `已部署 vY`
4. 点「② 启动游戏（带挂）」

> 游戏运行中点部署会明确提示「游戏正在运行，WceHack.dll 被占用」，不会静默失败。

版本历史：

| 版本 | 内容 |
|---|---|
| 1.0.0 | ESP 方框 + 传送 + 无限冲刺 + 加速（英文菜单） |
| 1.1.0 | 中文菜单/中文标签；剑气、黑板擦、粉笔 自动瞄准 + 飞行追踪 |
| 1.2.0 | 骨骼透视（三级回退：Humanoid → 蒙皮骨链 → 挂点）+ F12 模型结构 dump |
| 1.3.0 | 追踪优化（提前量预测 + 锁定保持 + 平滑转向）；**Insert 自动脱困**（眩晕/被抓/绑柱）；**Delete 完全隐形**（网段标记 + 广播 + 渲染全关） |
| 1.4.0 | 自动瞄准/追踪默认关闭；**F3 散射**（一次 7 颗锥形霰弹，`[`/`]` 调数量）+ **F4 速射**（冷却压 0）+ 无限攻击体力 |
| 1.5.0 | **F11 模型染色**取代骨骼线（学生绿 / 老师蓝 / NPC 灰，整模型换色 + 可还原）；无限体力与无限冲刺**改为只对自己生效** |

---


## 3. 文件在哪

```
C:\Users\26033\dsh-ws\alpha\worst_class\
├─ mod\
│  ├─ src\WceHack.cs            源码（C#5，单文件）
│  ├─ WceHack.dll               编译产物
│  ├─ build.ps1                 重新编译
│  ├─ enable_mod.ps1            开游戏前修 load-order.json
│  └─ restart_and_verify.ps1    一键重启 + 抓日志确认（会关掉正在跑的游戏）
├─ tools\                       侦察/反汇编工具（dump_exact.ps1 / verify_mod.ps1 / dump_il.ps1 ...）
└─ out\                         IL dump 与日志
```

**安装位置（两处都是必需的）**

- 包本体（游戏真正扫描的目录）：
  `C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Mods\wcehack\` → `mod.json` + `WceHack.dll`
- 状态/配置（`Application.persistentDataPath`）：
  `C:\Users\26033\AppData\LocalLow\Funcore Games\Worst Class Ever\Mods\load-order.json`

> 这是踩过的坑：`ModRuntime.ModsDirectory` = `Path.Combine(Application.dataPath, "..", "Mods")`，
> 也就是**安装目录**下的 Mods，不是 LocalLow。放错地方会表现为 `missing: true` 且静默不加载。

---

## 4. 技术要点（复现/迭代用）

- 引擎：Unity 2022.3.62f3c1，**Mono**（非 IL2CPP），`Assembly-CSharp.dll` 可直接 Cecil 反编译。
- mod 入口：`WorstClassEver.ModAPI.IMod.OnLoad(ModContext)`；`ModContext` 给 `Id / ModDirectory / ConfigDirectory / Log / Settings`。
- manifest：`mod.json`，`apiVersion` 必须是 **1**，`id` 必须匹配 `\A[a-z0-9][a-z0-9_.-]{1,127}\z`，`version` 要过 `Version.TryParse`，`entryType` 对应 public 非抽象 + 实现 IMod + 无参构造。
- 程序集名不能是保留名（`0Harmony`、`Mono.Cecil`、`Assembly-CSharp*`、`Unity*`、`System*` …），所以 Harmony 只能 `new HarmonyLib.Harmony("wce.hack")` 复用游戏已加载的那份。
- 编译：`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，`/langversion:5 /nostdlib+`，引用游戏 Managed 目录里的 `netstandard.dll`、`mscorlib.dll`、`UnityEngine.*Module.dll`、`Assembly-CSharp.dll`、`WorstClassEver.ModAPI.dll`、`0Harmony.dll`、`Mirror.dll`。
- 关键运行时 API：`UnitManager.Instance.unitsDict`（`Dictionary<ulong, Unit>`）、`UnitManager.Instance.localPlayer`、`Unit.Position`（= transform.position）、`Unit.UnitSubType`（101 老师 / 201 学生 / 202 NPC）、`LocomotionComponent.SetPosition(Vector3)`、`Mirror.NetworkServer.active`。
- 联机性质：Mirror，位置走 `SyncPlayerPose_C2S` 由**客户端上报**（所以瞬移/加速在联机里有效）；但伤害、抓捕、道具等多数判定在房主端，客户端改数值不改房主结果。

---

## 5. 已知边界

- 你现在是**房主**，这套东西权威性最高；当纯客户端时，ESP / 传送 / 加速有效，改分和抓人要看房主。
- `debug_showName` 的 setter 是 private，代码里用反射写 `<debug_showName>k__BackingField`。
- ESP 用 `OnGUI` 画，若游戏自己叠了 UI 层，可以调 `GUI.depth`（现在是 -100）。
