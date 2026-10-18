"""Gears of Aion sheets: preflight and code generation.

    python -I tools/sheets.py preflight [--gears-files files.txt] [--gears DIR] [--aion DIR]
    python -I tools/sheets.py generate

preflight lays every sheet's rows over its columns and lists unfilled cells, unknown cells and
references between sheets that don't resolve. generate writes src/Daevas.Game/Generated/Sheets.g.cs,
one C# record per row, and refuses to run while preflight has problems.
"""
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SHEETS = os.path.join(ROOT, "sheets")
OUT = os.path.join(ROOT, "src", "Daevas.Game", "Generated", "Sheets.g.cs")
NAMES = ["classes", "skills", "levels", "weapons", "characters", "enemies", "waves", "arena", "sounds", "sources", "systems"]
SKILL_KINDS = {"strike", "shot", "nova", "blast", "dot", "stun", "dash", "heal", "buff", "shield", "aura", "leap", "pull", "drain", "pet", "stealth", "trap", "blink", "guard", "haste", "regen", "mana", "salvation", "delayed", "zone"}
CC_KINDS = {"none", "stun", "knockback", "knockdown", "airborne", "root", "slow", "silence", "disarm", "taunt"}
ELEMENTS = {"physical", "fire", "ice", "lightning", "holy", "shadow", "wind", "earth", "spirit", "poison", "arcane"}
GEARS_WEAPONS = "FairlightConcept/Content/Fairlight/Weapons/"
ROW_TYPES = {"classes": "ClassRow", "skills": "SkillRow", "levels": "LevelRow", "weapons": "WeaponRow",
             "enemies": "EnemyRow", "characters": "CharacterRow", "arena": "ArenaRow", "sounds": "SoundRow", "waves": "WaveRow", "sources": "SourceRow"}


def load():
    out = {}
    for n in NAMES:
        with open(os.path.join(SHEETS, n + ".json"), encoding="utf-8") as f:
            out[n] = json.load(f)
    return out


def unfilled(v):
    return v is None or v == "" or v == [] or v == {}


def preflight(s, gears_files=None, gears_dir=None, aion_dir=None):
    problems = []
    cells = 0
    for n in NAMES:
        cols = list(s[n]["columns"].keys())
        key = cols[0]
        seen = set()
        for i, row in enumerate(s[n]["rows"]):
            rid = row.get(key, "#%d" % i)
            if rid in seen:
                problems.append("%s: duplicate key %r" % (n, rid))
            seen.add(rid)
            for c in cols:
                cells += 1
                if c not in row or unfilled(row[c]):
                    problems.append("%s[%s].%s is unfilled" % (n, rid, c))
            for c in row:
                if c not in cols:
                    problems.append("%s[%s].%s is not a column" % (n, rid, c))

    ids = {n: {r[list(s[n]["columns"])[0]] for r in s[n]["rows"]} for n in NAMES}
    levels = sorted(ids["levels"])
    max_level = levels[-1] if levels else 0
    if levels != list(range(1, max_level + 1)):
        problems.append("levels: must run 1..%d without gaps" % max_level)
    for r in s["levels"]["rows"]:
        if (r["xpToNext"] == 0) != (r["level"] == max_level):
            problems.append("levels[%s].xpToNext: only the last level has 0" % r["level"])

    for c in s["classes"]["rows"]:
        if c["weapon"] not in ids["weapons"]:
            problems.append("classes[%s].weapon -> weapons.%s does not exist" % (c["id"], c["weapon"]))
        for k in c["skills"]:
            if k not in ids["skills"]:
                problems.append("classes[%s].skills -> skills.%s does not exist" % (c["id"], k))
        if c.get("basicElement") not in ELEMENTS:
            problems.append("classes[%s].basicElement %r is not an element" % (c["id"], c.get("basicElement")))
        if len(c["skills"]) != 8:
            problems.append("classes[%s].skills: needs 8 hotbar skills" % c["id"])
        if c.get("basicKind") not in ("melee", "bolt", "arrow"):
            problems.append("classes[%s].basicKind must be melee, bolt or arrow" % c["id"])
    by_id = {r["id"]: r for r in s["skills"]["rows"]}
    for c in s["classes"]["rows"]:
        for slot, k in enumerate(c["skills"], 1):
            sk = by_id.get(k)
            if sk and (sk["class"] != c["id"] or sk["slot"] != slot):
                problems.append("skills[%s]: class/slot don't match classes[%s].skills position %d" % (k, c["id"], slot))
    for sk in s["skills"]["rows"]:
        frm = sk.get("chainFrom", "none")
        if frm != "none":
            parent = by_id.get(frm)
            if parent is None:
                problems.append("skills[%s].chainFrom -> skills.%s does not exist" % (sk["id"], frm))
            elif parent["class"] != sk["class"] or parent["slot"] != sk["slot"]:
                problems.append("skills[%s]: a chain skill must share its parent's class and slot" % sk["id"])
            elif sk["unlockLevel"] < parent["unlockLevel"]:
                problems.append("skills[%s]: unlocks before the skill that opens it" % sk["id"])
            if any(sk["id"] in c["skills"] for c in s["classes"]["rows"]):
                problems.append("skills[%s]: chain skills don't go on the hotbar list" % sk["id"])
        if sk["class"] not in ids["classes"]:
            problems.append("skills[%s].class -> classes.%s does not exist" % (sk["id"], sk["class"]))
        if sk["kind"] not in SKILL_KINDS:
            problems.append("skills[%s].kind %r is not a known kind" % (sk["id"], sk["kind"]))
        if not 1 <= sk["unlockLevel"] <= max_level:
            problems.append("skills[%s].unlockLevel outside 1..%d" % (sk["id"], max_level))
        for lv in sk["rankLevels"]:
            if not sk["unlockLevel"] < lv <= max_level:
                problems.append("skills[%s].rankLevels %d must be after unlock and <= %d" % (sk["id"], lv, max_level))
        if sk.get("element") not in ELEMENTS:
            problems.append("skills[%s].element %r is not a known element" % (sk["id"], sk.get("element")))
        if sk.get("cc", "none") not in CC_KINDS:
            problems.append("skills[%s].cc %r is not a known crowd control" % (sk["id"], sk.get("cc")))
        if sk.get("cc", "none") != "none" and sk.get("ccTime", 0) <= 0:
            problems.append("skills[%s].ccTime must be > 0 when cc is set" % sk["id"])
        needs_range = sk["kind"] in {"strike", "shot", "blast", "dot", "stun", "dash", "leap", "pull", "drain", "trap"}
        needs_radius = sk["kind"] in {"strike", "nova", "blast", "aura", "dash", "leap", "trap"}
        needs_duration = sk["kind"] in {"dot", "stun", "buff", "shield", "aura", "pet", "stealth", "trap", "guard", "haste", "regen", "salvation"}
        if needs_range and sk["range"] <= 0:
            problems.append("skills[%s].range must be > 0 for %s" % (sk["id"], sk["kind"]))
        if needs_radius and sk["radius"] <= 0:
            problems.append("skills[%s].radius must be > 0 for %s" % (sk["id"], sk["kind"]))
        if needs_duration and sk["duration"] <= 0:
            problems.append("skills[%s].duration must be > 0 for %s" % (sk["id"], sk["kind"]))
    for cid in ids["classes"]:
        if not any(by_id[k]["unlockLevel"] == 1 for k in next(c for c in s["classes"]["rows"] if c["id"] == cid)["skills"] if k in by_id):
            problems.append("classes[%s]: no skill unlocks at level 1" % cid)

    for c in s["classes"]["rows"]:
        if c["character"] not in ids["characters"]:
            problems.append("classes[%s].character -> characters.%s does not exist" % (c["id"], c["character"]))
    for e in s["enemies"]["rows"]:
        if e["character"] not in ids["characters"]:
            problems.append("enemies[%s].character -> characters.%s does not exist" % (e["id"], e["character"]))
        if e["ranged"] != (e["projectileSpeed"] > 0):
            problems.append("enemies[%s]: ranged and projectileSpeed disagree" % e["id"])
        for col in ("weak", "resist"):
            if e.get(col) != "none" and e.get(col) not in ELEMENTS:
                problems.append("enemies[%s].%s %r is not an element" % (e["id"], col, e.get(col)))
        if not 0 <= e.get("armour", 0) < 90:
            problems.append("enemies[%s].armour must be 0..89" % e["id"])
    for ch in s["characters"]["rows"]:
        if ch["gait"] not in ("upright", "hunched"):
            problems.append("characters[%s].gait must be upright or hunched" % ch["id"])
        if ch["heldGun"] != "none" and ch["handBone"] == "none":
            problems.append("characters[%s]: heldGun needs a handBone" % ch["id"])
        if ch["attackArm"] not in ("R", "L"):
            problems.append("characters[%s].attackArm must be R or L" % ch["id"])
    enemy_cols = [c for c in s["waves"]["columns"] if c not in ("wave", "name", "hpScale", "extra", "boss")]
    for c in enemy_cols:
        if c not in ids["enemies"]:
            problems.append("waves column %s -> enemies.%s does not exist" % (c, c))
    seen = set(enemy_cols)
    by_enemy = {e["id"]: e for e in s["enemies"]["rows"]}
    for w in s["waves"]["rows"]:
        for pair in ([] if w.get("extra", "none") == "none" else w["extra"].split()):
            eid, _, n = pair.partition(":")
            if eid not in ids["enemies"] or not n.isdigit():
                problems.append("waves[%s].extra %r: needs enemy:count with a real enemy" % (w["wave"], pair))
            seen.add(eid)
        b_id = w.get("boss", "none")
        if b_id != "none":
            if by_enemy.get(b_id, {}).get("special") != "boss":
                problems.append("waves[%s].boss %r is not an enemy with special boss" % (w["wave"], b_id))
            seen.add(b_id)
    seen.add("nemacyst")  # Seeders launch these
    seen.add("dummy")     # the practice range's targets
    for e in ids["enemies"]:
        if e not in seen:
            problems.append("enemies[%s] never appears in waves" % e)
    for e in s["enemies"]["rows"]:
        if e.get("special", "none") not in ("none", "heal", "seed", "snipe", "grenade", "beam", "acid", "boss"):
            problems.append("enemies[%s].special %r is not known" % (e["id"], e.get("special")))

    keys = [w["key"] for w in s["weapons"]["rows"]]
    if len(set(keys)) != len(keys):
        problems.append("weapons.key: two guns share a key")
    if gears_files:
        with open(gears_files, encoding="utf-8") as f:
            have = {l.strip().lower() for l in f if l.strip()}
        for w in s["weapons"]["rows"]:
            for p in w["parts"]:
                path = "%s%s/%s_SM01.uasset" % (GEARS_WEAPONS, w["partsFolder"], p)
                if path.lower() not in have:
                    problems.append("weapons[%s].parts %s not in the Gears install (%s)" % (w["id"], p, path))
            if w["model"] != "parts" and ("%s%s.uasset" % (GEARS_WEAPONS, w["model"])).lower() not in have:
                problems.append("weapons[%s].model not in the Gears install" % w["id"])
            tex = "%s%s.uasset" % (GEARS_WEAPONS, w["texture"])
            if tex.lower() not in have:
                problems.append("weapons[%s].texture not in the Gears install (%s)" % (w["id"], tex))
    if gears_files:
        for ch in s["characters"]["rows"]:
            for part in ch["parts"]:
                path = "FairlightConcept/Content/Fairlight/%s%s.uasset" % ("" if part.startswith("AnimProps/") else "Characters/", part)
                if path.lower() not in have:
                    problems.append("characters[%s].parts %s not in the Gears install" % (ch["id"], part))
            if ("FairlightConcept/Content/Fairlight/%s%s.uasset" % ("" if ch["animSkeleton"].startswith("AnimProps/") else "Characters/", ch["animSkeleton"])).lower() not in have:
                problems.append("characters[%s].animSkeleton %s not in the Gears install" % (ch["id"], ch["animSkeleton"]))
            for col in ("idleAnim", "walkAnim", "attackAnim", "spawnAnim", "deathAnim", "evadeAnim"):
                if ch[col] != "none" and ("FairlightConcept/Content/Fairlight/Animation/Gameplay/%s.uasset" % ch[col]).lower() not in have:
                    problems.append("characters[%s].%s %s not in the Gears install" % (ch["id"], col, ch[col]))
            if ch["heldGun"] != "none" and ("FairlightConcept/Content/Fairlight/Weapons/%s.uasset" % ch["heldGun"]).lower() not in have:
                problems.append("characters[%s].heldGun %s not in the Gears install" % (ch["id"], ch["heldGun"]))
    for a in s["arena"]["rows"]:
        if a["role"] not in ("cover", "wall", "backdrop", "street"):
            problems.append("arena[%s].role must be cover, wall, backdrop or street" % a["id"])
        if not isinstance(a.get("count"), int) or a["count"] < 1:
            problems.append("arena[%s].count must be 1 or more" % a["id"])
        if a["yaw"] % 90 != 0:
            problems.append("arena[%s].yaw must be a multiple of 90" % a["id"])
        if gears_files and ("FairlightConcept/Content/Fairlight/Environments/%s.uasset" % a["mesh"]).lower() not in have:
            problems.append("arena[%s].mesh %s not in the Gears install" % (a["id"], a["mesh"]))
    ground = next((r["params"].get("groundTexture") for r in s["systems"]["rows"] if r["id"] == "arena"), None)
    if not ground:
        problems.append("systems[arena].params.groundTexture is unfilled")
    elif gears_files and ("FairlightConcept/Content/Fairlight/Environments/%s.uasset" % ground).lower() not in have:
        problems.append("systems[arena].groundTexture %s not in the Gears install" % ground)
    for c in s["classes"]["rows"]:
        if gears_files and c.get("handWeapon", "none") != "none" and ("FairlightConcept/Content/Fairlight/Weapons/%s.uasset" % c["handWeapon"]).lower() not in have:
            problems.append("classes[%s].handWeapon %s not in the Gears install" % (c["id"], c["handWeapon"]))
    for so in s["sounds"]["rows"]:
        general = so["owner"] in ("music", "ambience", "hero", "ui", "world") or so["owner"].startswith("voice-")
        if not general and so["owner"] not in ids["weapons"] and so["owner"] not in ids["enemies"]:
            problems.append("sounds[%s].owner %s is not a weapon, an enemy or music/ambience/hero/ui/world" % (so["id"], so["owner"]))
        if not general and so["when"] not in ("fire", "death", "spawn", "reload", "reload-done", "reload-perfect", "reload-jam", "beep", "shout"):
            problems.append("sounds[%s].when must be fire, death, spawn, beep or a reload cue" % so["id"])
        if gears_files and ("FairlightConcept/Content/WwiseAudio/Events/Fairlight_Events/%s.uasset" % so["event"]).lower() not in have:
            problems.append("sounds[%s].event %s not in the Gears install" % (so["id"], so["event"]))
        if so["pick"] not in ("shot", "any"):
            problems.append("sounds[%s].pick must be shot or any" % so["id"])
        if not isinstance(so.get("maxLength"), (int, float)) or so["maxLength"] < 0:
            problems.append("sounds[%s].maxLength must be 0 or more seconds" % so["id"])
        if gears_files and ("FairlightConcept/Content/WwiseAudio/Events/Fairlight_Events/%s.uasset" % so["event"]).lower() not in have:
            problems.append("sounds[%s].event not in the Gears install" % so["id"])
    for src in s["sources"]["rows"]:
        base = {"custom-gears-of-war-e-day": gears_dir, "custom-aion-2": aion_dir}.get(src["game"], "?")
        if base == "?":
            problems.append("sources[%s].game %r is not one of the mashup's games" % (src["id"], src["game"]))
        elif base and not os.path.exists(os.path.join(base, src["path"])):
            problems.append("sources[%s].path missing on this PC: %s" % (src["id"], os.path.join(base, src["path"])))
    return problems, cells


def cs(v):
    if isinstance(v, bool):
        return "true" if v else "false"
    if isinstance(v, int):
        return str(v)
    if isinstance(v, float):
        return repr(v) + "f"
    if isinstance(v, str):
        return json.dumps(v)
    if isinstance(v, list):
        if all(isinstance(x, str) for x in v):
            return "new string[] { %s }" % ", ".join(cs(x) for x in v)
        if all(isinstance(x, int) for x in v):
            return "new int[] { %s }" % ", ".join(cs(x) for x in v)
    raise ValueError("can't emit %r" % (v,))


def cs_type(vals):
    v = vals[0]
    if isinstance(v, bool):
        return "bool"
    if all(isinstance(x, int) and not isinstance(x, bool) for x in vals):
        return "int"
    if all(isinstance(x, (int, float)) and not isinstance(x, bool) for x in vals):
        return "float"
    if isinstance(v, str):
        return "string"
    if isinstance(v, list):
        return "string[]" if v and isinstance(v[0], str) else "int[]"
    raise ValueError(v)


def pascal(s):
    return "".join(p[:1].upper() + p[1:] for p in s.replace("-", "_").split("_"))


def generate(s):
    lines = ["// <auto-generated> from sheets/*.json by tools/sheets.py. Edit the sheets, not this file. </auto-generated>",
             "#nullable enable", "namespace Daevas.Game.Data;", ""]
    for n in NAMES:
        if n == "systems":
            continue
        cols = list(s[n]["columns"].keys())
        rows = s[n]["rows"]
        rec = ROW_TYPES[n]
        types = {c: cs_type([r[c] for r in rows]) for c in cols}
        lines.append("/// %s" % s[n]["about"])
        lines.append("public sealed record %s(%s);" % (rec, ", ".join("%s %s" % (types[c], pascal(c)) for c in cols)))
        lines.append("")
    lines.append("public static class Sheets")
    lines.append("{")
    for n in NAMES:
        if n == "systems":
            continue
        cols = list(s[n]["columns"].keys())
        types = {c: cs_type([r[c] for r in s[n]["rows"]]) for c in cols}
        rec = ROW_TYPES[n]
        # not readonly: sheet mods (SheetMods.cs) swap in their rows at run time
        lines.append("    public static %s[] %s =" % (rec, pascal(n)))
        lines.append("    [")
        for r in s[n]["rows"]:
            vals = []
            for c in cols:
                v = r[c]
                if types[c] == "float" and isinstance(v, int):
                    v = float(v)
                vals.append(cs(v))
            lines.append("        new(%s)," % ", ".join(vals))
        lines.append("    ];")
        lines.append("")
    for sysrow in s["systems"]["rows"]:
        for k, v in sysrow["params"].items():
            name = pascal(sysrow["id"]) + pascal(k)
            if isinstance(v, list):
                lines.append("    public static readonly string[] %s = %s;" % (name, cs(v)))
            elif isinstance(v, bool):
                lines.append("    public const bool %s = %s;" % (name, cs(v)))
            elif isinstance(v, str):
                lines.append("    public const string %s = %s;" % (name, cs(v)))
            elif isinstance(v, int):
                lines.append("    public const int %s = %d;" % (name, v))
            else:
                lines.append("    public const float %s = %sf;" % (name, repr(float(v))))
    lines.append("}")
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def main(argv):
    opts = dict(zip(argv[1::2], argv[2::2])) if len(argv) > 1 else {}
    s = load()
    problems, cells = preflight(s, opts.get("--gears-files"), opts.get("--gears"), opts.get("--aion"))
    rows = sum(len(s[n]["rows"]) for n in NAMES)
    # a validation report next to the build: what each sheet holds and anything wrong
    try:
        rep = os.path.join(ROOT, "build", "sheets-report.txt")
        os.makedirs(os.path.dirname(rep), exist_ok=True)
        with open(rep, "w", encoding="utf-8") as f:
            f.write("Gears of Aion sheet report\n\n")
            for n in NAMES:
                f.write("%-11s %4d rows  %2d columns\n" % (n, len(s[n]["rows"]), len(s[n].get("columns", {}))))
            f.write("\n%d cells checked, %d problems\n" % (cells, len(problems)))
            for p in problems:
                f.write("  - " + p + "\n")
    except OSError:
        pass
    if problems:
        print("PREFLIGHT: %d problems in %d rows / %d cells" % (len(problems), rows, cells))
        for p in problems:
            print("  - " + p)
        return 1
    print("PREFLIGHT clean: %d sheets, %d rows, %d cells checked" % (len(NAMES), rows, cells))
    if argv and argv[0] == "generate":
        generate(s)
        print("wrote " + os.path.relpath(OUT, ROOT))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
