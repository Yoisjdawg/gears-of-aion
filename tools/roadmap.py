"""Marks roadmap items done: python -I tools/roadmap.py done 7 8 12   |   python -I tools/roadmap.py todo [section]
Works on ROADMAP100.md (the current one); put 500 first for the finished ROADMAP500.md: tools/roadmap.py 500 todo"""
import re, sys, pathlib
argv = sys.argv[1:]
name = "ROADMAP500.md" if argv and argv[0] == "500" else "ROADMAP100.md"
if argv and argv[0] in ("100", "500"): argv = argv[1:]
p = pathlib.Path(__file__).resolve().parent.parent / name
lines = p.read_text(encoding="utf-8").splitlines()
cmd, args = argv[0], argv[1:]
if cmd == "done":
    want = {int(a) for a in args}
    for i, l in enumerate(lines):
        m = re.match(r"(\d+)\. \[ \] (.*)", l)
        if m and int(m.group(1)) in want: lines[i] = f"{m.group(1)}. [x] {m.group(2)}"
done = sum(1 for l in lines if re.match(r"\d+\. \[x\]", l)); total = sum(1 for l in lines if re.match(r"\d+\. \[", l))
lines = [f"Progress: {done} of {total} done." if l.startswith("Progress:") else l for l in lines]
open(p, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")
if cmd == "todo":
    sec = None
    for l in lines:
        if l.startswith("## "): sec = l[3:]
        if re.match(r"\d+\. \[ \]", l) and (not args or args[0].lower() in sec.lower()): print(sec[:14].ljust(14), l)
print(f"{done} of {total} done")
