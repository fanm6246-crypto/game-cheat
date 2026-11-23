#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
repack.py —— 用改好的文件替换 apk 内同名条目，产出新 apk（不破坏其它内容）
用法:
    python repack.py base.apk <apk内路径> <本地新文件> out.apk
例:
    python repack.py base.apk assets/bin/Data/Managed/Assembly-CSharp.dll patched/Assembly-CSharp.dll base_patched.apk
注意: 只换单个文件; 换完必须重新签名(sign_install.ps1), 否则装不上。
"""
import sys, zipfile, os, shutil

def main():
    if len(sys.argv) != 5:
        print(__doc__); sys.exit(1)
    apk, inner, local, out = sys.argv[1:5]
    inner = inner.replace("\\", "/").lstrip("/")
    if not os.path.exists(local):
        print("[-] 本地文件不存在:", local); sys.exit(2)

    zin = zipfile.ZipFile(apk, "r")
    names = zin.namelist()
    if inner not in names:
        print("[-] apk 内没有该条目。相近的候选:")
        for n in names:
            if os.path.basename(inner).lower() in n.lower():
                print("   ", n)
        sys.exit(3)

    tmp = out + ".tmp"
    with zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == inner:
                data = open(local, "rb").read()
                print(f"[*] 替换 {inner}  ({len(data)} bytes)")
            zi = zipfile.ZipInfo(item.filename, date_time=item.date_time)
            zi.compress_type = item.compress_type
            zi.external_attr = item.external_attr
            zout.writestr(zi, data)
    zin.close()
    if os.path.exists(out): os.remove(out)
    shutil.move(tmp, out)
    print("[+] 写出:", out)
    print("[!] 下一步: .\\android\\sign_install.ps1 -Apk", out, "-Install")

if __name__ == "__main__":
    main()
