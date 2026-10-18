"""Skill balance sheet: python -I tools/balance.py  ->  build/skill-balance.csv (open it in any spreadsheet).
Damage per second here is a rough guide: power over cooldown (or cast time when that's longer)."""
import csv, json, pathlib
root = pathlib.Path(__file__).resolve().parent.parent
skills = json.load(open(root / "sheets" / "skills.json", encoding="utf-8"))["rows"]
classes = {c["id"]: c for c in json.load(open(root / "sheets" / "classes.json", encoding="utf-8"))["rows"]}
out = root / "build" / "skill-balance.csv"
out.parent.mkdir(exist_ok=True)
with open(out, "w", newline="", encoding="utf-8") as f:
    w = csv.writer(f)
    w.writerow(["class", "slot", "skill", "kind", "element", "unlock", "power r1", "power max", "cooldown", "cast", "mp", "dps r1", "dps max", "cc", "chain from"])
    for s in sorted(skills, key=lambda s: (s["class"], s["slot"], s["unlockLevel"])):
        top = s["power"] + s["powerPerRank"] * len(s["rankLevels"])
        mult = classes[s["class"]].get("skillPower", 1)
        busy = max(s["cooldown"], s["castTime"], 0.6)
        w.writerow([s["class"], s["slot"], s["name"], s["kind"], s["element"], s["unlockLevel"], s["power"], top, s["cooldown"], s["castTime"], s["mpCost"],
                    round(s["power"] * mult / busy, 1), round(top * mult / busy, 1), s["cc"], s["chainFrom"]])
print("wrote", out)
