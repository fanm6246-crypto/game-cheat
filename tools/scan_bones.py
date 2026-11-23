import os, sys, re

ROOTS = [
    r"C:\Program Files (x86)\Steam\steamapps\common\Worst Class Ever\Worst Class Ever_Data",
]
NEEDLES = [b"mixamorig", b"Bip01", b"bip01", b"UpperArm", b"Hips", b"LeftForeArm",
           b"LeftUpLeg", b"Spine1", b"Pelvis", b"LeftThigh", b"LeftCalf"]

def scan(path):
    try:
        with open(path, "rb") as f:
            data = f.read()
    except Exception:
        return None
    hits = {}
    for n in NEEDLES:
        c = data.count(n)
        if c:
            hits[n.decode()] = c
    return hits if hits else None

def main():
    results = []
    for root in ROOTS:
        for dirpath, dirs, files in os.walk(root):
            for fn in files:
                if not fn.lower().endswith((".assets", ".unity3d", ".resS", ".bundle", ".dat")):
                    continue
                p = os.path.join(dirpath, fn)
                try:
                    if os.path.getsize(p) > 400 * 1024 * 1024:
                        continue
                except Exception:
                    continue
                h = scan(p)
                if h:
                    results.append((p, os.path.getsize(p), h))
                    print(os.path.relpath(p, root), h, flush=True)
    print("---- files with hits:", len(results))

if __name__ == "__main__":
    main()
