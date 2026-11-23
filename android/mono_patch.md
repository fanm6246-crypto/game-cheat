# 路线 A：改 Assembly-CSharp.dll（Mono 游戏，最暴力、永久生效）

> 前提：`python tools/probe.py base.apk` 输出 `runtime: Mono`，并且 `key_files` 里有
> `assets/bin/Data/Managed/Assembly-CSharp.dll`。这是小团队 Unity 游戏最常见的形态。

## 0. 拿到 APK（还没装到手机就直接跳过这步）

```powershell
$ADB = "C:\Users\26033\dsh-ws\alpha\tools\platform-tools\adb.exe"
& $ADB devices
& $ADB shell pm list packages | Select-String -Pattern "class|worst|teacher|banji" -CaseSensitive:$false
# 假设包名 com.xxx.worstclass
& $ADB shell pm path com.xxx.worstclass
& $ADB pull /data/app/~~xxxx/com.xxx.worstclass-xxxx/base.apk .\base.apk
```

## 1. 抽出 dll

```powershell
python tools\probe.py .\base.apk          # 确认有 Managed\Assembly-CSharp.dll
# 解出来（只为取 dll，不用整包解）
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::ExtractToDirectory("$PWD\base.apk", "$PWD\unpacked")
```

得到 `unpacked\assets\bin\Data\Managed\Assembly-CSharp.dll`。

## 2. 用 dnSpyEx 改（推荐）

1. 下载 dnSpyEx（`dnSpy-net-win64.zip`），拖入 `Assembly-CSharp.dll`。
2. 展开程序集 → 找这些名字（`Ctrl+Shift+K` 搜）：
   - `GameManager` / `GameController` / `LevelManager` / `GameData` / `SaveData` / `PlayerData`
   - `Teacher` / `Player` / `PlayerStats` / `Salary` / `Budget`
   - `Student` / `StudentData` / `Mood` / `Score` / `ClassScore`
   - 字段名猜：`money` `coin` `gold` `cash` `salary` `budget` `stamina` `energy` `patience` `stress` `mood` `score` `level` `time`
3. 两种改法，任选：

   **(a) 改初始化常量** —— 找到字段声明，把默认值改大：
   ```csharp
   public int money = 999999;        // 原来是 100
   public float patience = 99999f;
   ```
   注意：如果构造函数里又赋值了一次，要同时改构造函数里的那个数。

   **(b) 改 getter / 判断逻辑** —— 右键方法 → 编辑方法体（Edit Method (C#)）：
   ```csharp
   // 改前: public int get_Money() { return this.money; }
   // 改后: public int get_Money() { return 999999; }
   ```
   或者给「扣钱」的函数直接 `return;`（让消耗无效）：
   ```csharp
   public void SpendMoney(int n) { }     // 原本 this.money -= n;
   ```
   或者把「失败/被开除」判断改成永远 false：
   ```csharp
   if (score < 60) { Fail(); }   →   if (false) { Fail(); }
   ```
4. `File → Save Module`，覆盖原 dll。

## 3. 塞回去

```powershell
copy unpacked\assets\bin\Data\Managed\Assembly-CSharp.dll .\patched_Assembly-CSharp.dll
python tools\repack.py .\base.apk assets/bin/Data/Managed/Assembly-CSharp.dll .\patched_Assembly-CSharp.dll .\base_patched.apk
```

## 4. 签名并安装

```powershell
.\android\sign_install.ps1 -Apk .\base_patched.apk -Install
# 若提示 INSTALL_FAILED_UPDATE_INCOMPATIBLE：签名不同，先卸载原版
& $ADB uninstall com.xxx.worstclass
.\android\sign_install.ps1 -Apk .\base_patched.apk -Install
```

> 卸载会清存档。**先把存档备份出来**（见下节），否则进度白打。

## 5. 存档备份（改包前必做）

```powershell
$PKG = "com.xxx.worstclass"
& $ADB shell run-as $PKG ls -R /data/data/$PKG/
# 常见位置
& $ADB shell run-as $PKG cat /data/data/$PKG/shared_prefs/${PKG}_prefs.xml  > prefs.xml
& $ADB pull /sdcard/Android/data/$PKG/ .
```
非 debuggable 包 `run-as` 会报错，那就需要 root 或先用 `adb backup`。

## 6. 改完可能遇到的坑

| 现象 | 原因 | 处理 |
| --- | --- | --- |
| 打开闪退 | dll 版本与 `global-metadata` / `ScriptingAssemblies.json` 不匹配，或 IL 改坏 | 用 dnSpy 的「Edit IL Instructions」小改几条指令，别整段重写；保留原 dll 备份 |
| 改的数值不生效 | 该值从服务器/本地 JSON 读，或每次进场景重建 | 改读取处，别改字段初始值 |
| 进商店/看广告后数值被冲掉 | 有本地存档覆盖 | 改存档里的键（第 5 节），或改存档加载逻辑 |
| 装不上 | 签名冲突 / 未对齐 | `sign_install.ps1` 已带对齐；冲突就先卸载 |
