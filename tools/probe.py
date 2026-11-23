#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
probe.py —— 《我带过的最差一届 / Worst Class Ever》外挂工程 · 引擎探针
用法:
    python probe.py path/to/base.apk          # 直接分析 apk
    python probe.py path/to/unpacked_dir      # 或已解包目录
输出: 引擎类型 / 架构 / 是否 IL2CPP / 是否 Mono / 元数据位置 / 下一步该用哪套脚本
"""
import sys, os, zipfile, re, json

UNITY_IL2CPP = ("libil2cpp.so",)
UNITY_MONO = ("libmonobdwgc-2.0.so", "libmono.so", "libmonobdwgc.so")
GODOT = ("libgodot_android.so", "libgodot.so")
COCOS = ("libcocos2djs.so", "libcocos.so", "libcocos2d.so")
UE = ("libUE4.so", "libUnreal.so")
FLUTTER = ("libflutter.so",)
RN = ("libhermes.so", "libjsc.so")


def list_files(root):
    """返回 (相对路径列表, 打开函数)"""
    if os.path.isdir(root):
        out = []
        for d, _, fs in os.walk(root):
            for f in fs:
                p = os.path.join(d, f)
                out.append(os.path.relpath(p, root).replace("\\", "/"))
        return out, lambda n: open(os.path.join(root, n), "rb").read()
    z = zipfile.ZipFile(root)
    return z.namelist(), lambda n: z.read(n)


def detect(root):
    names, read = list_files(root)
    low = [n.lower() for n in names]
    res = {
        "target": os.path.abspath(root),
        "file_count": len(names),
        "abis": sorted({m.group(1) for n in low
                        for m in [re.match(r"lib/([^/]+)/", n)] if m}),
        "engine": "unknown",
        "runtime": "unknown",
        "next": [],
    }

    def has(*keys):
        return any(k in n for n in low for k in keys)

    unity = has("libunity.so") or has(*UNITY_IL2CPP) or has("global-metadata.dat")
    if unity:
        res["engine"] = "Unity"
        if any(k in n for n in low for k in UNITY_IL2CPP) or \
           any("global-metadata.dat" in n for n in low):
            res["runtime"] = "IL2CPP"
            res["next"].append("用 android/frida_il2cpp.js（先 Il2CppDumper 出 dump.cs 拿类名/字段偏移）")
        else:
            res["runtime"] = "Mono"
            res["next"].append("最省事路线: android/mono_patch.md —— 直接改 Assembly-CSharp.dll 重打包")
            res["next"].append("或 android/frida_mono.js 运行时改字段")
    elif has(*GODOT) or any(n.endswith(".pck") for n in low):
        res["engine"], res["runtime"] = "Godot", "GDScript/C#"
        res["next"].append("Godot 路线: 解 .pck 用 gdsdecomp 反编译; 或直接走内存搜索(README 第5节)")
    elif has(*UE):
        res["engine"], res["runtime"] = "Unreal", "C++/BP"
        res["next"].append("UE 路线: 内存搜数值 + Frida 挂 UObject 属性")
    elif has(*COCOS):
        res["engine"], res["runtime"] = "Cocos", "JS/Lua"
        res["next"].append("Cocos 路线: 解 assets 里的 jsc/luac, 或用 frida 挂 cc.js")
    elif has(*FLUTTER):
        res["engine"], res["runtime"] = "Flutter", "Dart AOT"
        res["next"].append("Flutter 路线: 走内存搜索, 定位数值后冻结")
    elif has(*RN):
        res["engine"], res["runtime"] = "ReactNative", "Hermes/JS"
        res["next"].append("RN 路线: 解 assets/index.android.bundle, 搜逻辑改数值")
    else:
        res["next"].append("原生/自研引擎: 走 README 第5节『通用内存搜索』")

    # 关键文件清单
    keep = []
    for n in names:
        ln = n.lower()
        if ln.endswith((".so", ".pck", ".dat", ".dll", ".bundle", ".jsc", ".luac")) \
           or "managed" in ln or "metadata" in ln:
            keep.append(n)
    res["key_files"] = sorted(keep)[:40]

    # Managed dll 清单（Mono 路线直接可用）
    managed = [n for n in names if n.lower().endswith(".dll") and "managed" in n.lower()]
    if managed:
        res["managed_dlls"] = sorted(managed)

    # 顺手查包名 / 版本，方便 adb 定位
    try:
        if os.path.isdir(root):
            man = read("AndroidManifest.xml")
        else:
            man = read("AndroidManifest.xml")
        txt = man.decode("utf-8", "ignore")
        m = re.search(r'package="([^"]+)"', txt)
        if m:
            res["package"] = m.group(1)
    except Exception:
        pass

    return res


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    r = detect(sys.argv[1])
    print(json.dumps(r, ensure_ascii=False, indent=2))
    print("\n=== 结论 ===")
    print(f"引擎: {r['engine']} / 运行时: {r['runtime']} / 架构: {','.join(r['abis']) or '?'}")
    for i, s in enumerate(r["next"], 1):
        print(f"{i}. {s}")
