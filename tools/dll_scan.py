#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""dll_scan.py —— 用 dnfile 扫 .NET 程序集，导出类型/字段/方法清单
用法: python dll_scan.py <dll> [out.txt] [关键字1 关键字2 ...]
"""
import sys, dnfile

KW = ["money", "coin", "gold", "cash", "score", "point", "stamina", "energy",
      "patience", "stress", "mood", "level", "save", "teacher", "student",
      "class", "health", "hp", "damage", "speed", "cheat", "debug", "gm",
      "admin", "player", "item", "inventory", "reward", "price", "unlock",
      "progress", "rank", "round", "grade", "anger", "boss", "skill", "buff",
      "sync", "command", "rpc", "host", "client", "rule", "state"]


def main():
    if len(sys.argv) < 2:
        print(__doc__); sys.exit(1)
    path = sys.argv[1]
    out = sys.argv[2] if len(sys.argv) > 2 else "dll_inventory.txt"
    kws = [k.lower() for k in (sys.argv[3:] or KW)]

    pe = dnfile.dnPE(path)
    md = pe.net.mdtables
    trows, frows, mrows = md.TypeDef.rows, md.Field.rows, md.MethodDef.rows

    def idx(v):
        if v is None:
            return None
        try:
            seq = list(v)
            v = seq[0] if seq else None
        except TypeError:
            pass
        if v is None:
            return None
        try:
            return int(v)
        except Exception:
            return getattr(v, "row_index", None)

    types = []
    for t in trows:
        types.append({"ns": str(t.TypeNamespace or ""), "nm": str(t.TypeName or ""),
                      "f": idx(t.FieldList), "m": idx(t.MethodList)})

    def nxt(i, key, total):
        for j in range(i + 1, len(types)):
            if types[j][key] is not None:
                return types[j][key]
        return total + 1

    lines, hits = [], []
    for i, t in enumerate(types):
        if t["nm"] == "<Module>":
            continue
        fs, ms = t["f"] or 1, t["m"] or 1
        fe = nxt(i, "f", len(frows))
        me = nxt(i, "m", len(mrows))
        fields = [str(f.Name) for f in frows[fs - 1:max(fs - 1, fe - 1)]]
        methods = [str(m.Name) for m in mrows[ms - 1:max(ms - 1, me - 1)]]
        full = (t["ns"] + "." + t["nm"]) if t["ns"] else t["nm"]
        lines.append(f"{full}\t[{len(fields)}f {len(methods)}m]")
        blob = (full + " " + " ".join(fields) + " " + " ".join(methods)).lower()
        if any(k in blob for k in kws):
            hits.append((full, fields, methods))

    with open(out, "w", encoding="utf-8") as f:
        f.write(f"# {path}\n# types={len(lines)} hits={len(hits)}\n\n## ALL TYPES\n")
        f.write("\n".join(lines))
        f.write("\n\n## KEY TYPES (fields / methods)\n")
        for full, fields, methods in hits:
            f.write(f"\n### {full}\n")
            if fields:
                f.write("  fields: " + ", ".join(fields) + "\n")
            if methods:
                f.write("  methods: " + ", ".join(methods) + "\n")
    print(f"[+] {len(types)} types, {len(hits)} key types -> {out}")


if __name__ == "__main__":
    main()
