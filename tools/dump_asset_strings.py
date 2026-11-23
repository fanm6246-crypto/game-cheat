import re, sys, os

p = sys.argv[1]
data = open(p, "rb").read()
print("size:", len(data))
strs = re.findall(rb"[\x20-\x7e]{3,48}", data)
seen = set()
kw = ("hips", "spine", "neck", "head", "arm", "leg", "foot", "hand", "bone",
      "pelvis", "thigh", "calf", "shoulder", "elbow", "knee", "toe", "root",
      "chest", "finger", "thumb")
out = []
for s in strs:
    t = s.decode("ascii", "ignore")
    low = t.lower()
    if any(k in low for k in kw) and t not in seen:
        seen.add(t)
        out.append(t)
print("candidate bone-ish strings:", len(out))
for t in out[:120]:
    print("  ", t)
