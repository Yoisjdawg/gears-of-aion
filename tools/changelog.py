"""Changelog from the 500-item roadmap: python -I tools/changelog.py <version>
Lists every item checked off since the last run and adds them to CHANGELOG.md (newest first)."""
import re, sys, pathlib, json
root = pathlib.Path(__file__).resolve().parent.parent
version = sys.argv[1] if len(sys.argv) > 1 else "next"
done = {}
section = ""
for line in open(root / "ROADMAP500.md", encoding="utf-8"):
    if line.startswith("## "): section = line[3:].strip()
    m = re.match(r"(\d+)\. \[x\] (.*)", line)
    if m: done[int(m.group(1))] = (section, m.group(2).strip())
snap = root / "build" / "changelog-snapshot.json"
before = set(json.load(open(snap))) if snap.exists() else set()
new = [n for n in sorted(done) if n not in before]
lines = [f"## {version}", ""]
for sec in dict.fromkeys(done[n][0] for n in new):
    lines.append(f"### {sec}")
    lines += [f"- {done[n][1]}" for n in new if done[n][0] == sec]
    lines.append("")
log = root / "CHANGELOG.md"
old = log.read_text(encoding="utf-8") if log.exists() else "# Changelog\n\n"
head, _, rest = old.partition("\n\n")
log.write_text(head + "\n\n" + "\n".join(lines) + "\n" + rest, encoding="utf-8")
snap.parent.mkdir(exist_ok=True)
json.dump(sorted(done), open(snap, "w"))
print(f"{len(new)} new items under {version}")
