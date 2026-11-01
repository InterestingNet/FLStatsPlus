#!/usr/bin/env python3
"""Generate a sample FLStats Plus report (fake data) from the HTML template.

usage: python3 make_sample_report.py <report_template.html> <out.html> [matches]

The data follows the export schema in docs/EXPORT_SCHEMA.md, so the output is a
good way to preview template changes without running the game.
"""
import json, math, random, sys
from datetime import datetime, timedelta, timezone

random.seed(42)
TEAMS = ["Maelstrom", "Adders", "Flames"]
MAPS = [("SealRock", "Seal Rock", "Seize"), ("FieldsOfGlory", "The Fields of Glory", "Shatter"),
        ("OnsalHakair", "Onsal Hakair", "Danshig Naadam"), ("BorderlandRuins", "The Borderland Ruins", "Secure"),
        ("WorqorChirteh", "Worqor Chirteh", "Triumph")]
JOBS = [("PLD", "TANK"), ("WAR", "TANK"), ("DRK", "TANK"), ("GNB", "TANK"), ("WHM", "HEALER"), ("SCH", "HEALER"),
        ("AST", "HEALER"), ("SGE", "HEALER"), ("MNK", "MELEE"), ("DRG", "MELEE"), ("NIN", "MELEE"), ("SAM", "MELEE"),
        ("RPR", "MELEE"), ("VPR", "MELEE"), ("BRD", "RANGED"), ("MCH", "RANGED"), ("DNC", "RANGED"), ("BLM", "CASTER"),
        ("SMN", "CASTER"), ("RDM", "CASTER"), ("PCT", "CASTER")]
WORLDS = ["Balmung", "Gilgamesh", "Leviathan", "Ultros", "Sargatanas", "Midgardsormr", "Faerie", "Jenova", "Cactuar", "Excalibur"]
FIRST = ["Aeryn", "Thal'kin", "Mira", "Bram", "Ysolde", "Kae", "Odo", "Sel", "Rhea", "Tov", "Lune", "Ash", "Vex", "Nia", "Corin",
         "Hale", "Iri", "Jun", "Kesh", "Lio", "Mae", "Nox", "Pell", "Quin", "Rook", "Sana", "Tess", "Uma", "Varo", "Wynn"]
LAST = ["Volke", "Rho", "Halcyon", "Ostrid", "Fenn", "Ruun", "Barrow", "Marrow", "Tide", "Ashgrove", "Vale", "Corrin", "Dusk",
        "Ember", "Frost", "Gale", "Hollow", "Ivory", "Jade", "Kestrel"]
ROSTER = [(f"{random.choice(FIRST)} {random.choice(LAST)}", random.choice(WORLDS), random.choice(JOBS)) for _ in range(260)]
YOU = ("You Yourself", "Balmung")
MY_JOBS = [("MCH", "RANGED"), ("MCH", "RANGED"), ("DRK", "TANK"), ("SGE", "HEALER")]
SKILLS = {
    "MCH": [("Blast Charge", 5500, 2.5), ("Heat Blast", 7000, 2.5), ("Drill", 10000, 12), ("Bioblaster", 4500, 12),
            ("Air Anchor", 8500, 12), ("Chain Saw", 11000, 12), ("Wildfire", 10000, 40), ("Full Metal Field", 15000, 30),
            ("Marksman's Spite", 36000, 120)],
    "DRK": [("Hard Slash", 4000, 2.5), ("Syphon Strike", 5000, 2.5), ("Souleater", 6000, 2.5), ("Plunge", 6000, 15),
            ("Salted Earth", 3000, 30), ("Disesteem", 9000, 30), ("Eventide", 0, 120)],
    "SGE": [("Dosis III", 5000, 2.5), ("Phlegma III", 7000, 10), ("Pneuma", 8000, 30), ("Toxikon II", 4000, 10), ("Psyche", 6000, 20)],
}
INCOMING = ["Smite", "Midare Setsugekka", "Fell Cleave", "Foul", "Saber Dance", "Holy III", "Communio", "Verholy",
            "Phantom Rush", "Blizzard IV", "Starfall Dance", "Fated Circle", "Bloodspiller"]


def iso(t):
    return t.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")


def make_match(i, start, with_combat):
    mid, mname, mtype = random.choice(MAPS)
    dur = random.randint(11 * 60, 20 * 60)
    myjob = random.choice(MY_JOBS)
    my_team = random.choice(TEAMS)
    strength = {t: random.gauss(1, .18) for t in TEAMS}
    strength[my_team] += .06
    order = sorted(TEAMS, key=lambda t: -strength[t])
    pts = {t: int(1600 - order.index(t) * random.randint(180, 420) + random.randint(-40, 40)) for t in TEAMS}
    pts[order[0]] = 1600 if mtype != "Triumph" else 1400
    pool = random.sample(ROSTER, 71)
    players = []
    k = 0
    for t in TEAMS:
        for a in range(3):
            for s in range(8):
                if t == my_team and a == 0 and s == 0:
                    name, world, job = YOU[0], YOU[1], myjob
                    you = True
                else:
                    name, world, job = pool[k]; k += 1
                    you = False
                skill = random.gauss(1, .35) * strength[t]
                tank, heal = job[1] == "TANK", job[1] == "HEALER"
                kills = max(0, int(random.gauss(4, 3) * skill + (2 if you else 0)))
                deaths = max(0, int(random.gauss(4, 2) / max(skill, .4)))
                assists = max(0, int(random.gauss(28, 10) * skill))
                dpc = int(max(50000, random.gauss(1.3e6 if not (tank or heal) else 8e5, 4e5) * skill * dur / 900))
                dother = int(max(0, random.gauss(1.5e5, 8e4))) if mtype == "Shatter" else 0
                taken = int(max(80000, random.gauss(1.4e6 if tank else 9e5, 3e5) * dur / 900))
                hp = int(max(10000, random.gauss(1.8e6 if heal else 2.5e5, 3e5) * dur / 900))
                players.append(dict(name=name, world=world, job=job[0], role=job[1], team=t, alliance=a, isYou=you,
                                    kills=kills, deaths=deaths, assists=assists, damageDealt=dpc + dother, damageToPCs=dpc,
                                    damageToOther=dother, damageTaken=taken, hpRestored=hp, special=int(hp * .3),
                                    occupations=0 if mtype == "Triumph" else random.randint(0, 9),
                                    claimTimeSec=random.randint(0, 300) if mtype == "Triumph" else 0,
                                    maxBattleHigh=min(5, max(0, int(kills / 2 + random.random() * 2)))))
    teams = {}
    for t in TEAMS:
        tk = sum(p["kills"] for p in players if p["team"] == t)
        td = sum(p["deaths"] for p in players if p["team"] == t)
        kp, dl = tk * 8, td * 8
        occ = max(0, pts[t] - kp + dl - random.randint(0, 120))
        teams[t] = dict(placement=order.index(t), totalPoints=pts[t], killPoints=kp, deathPointLosses=dl,
                        occupationPoints=occ, targetablePoints=random.randint(0, 200) if mtype in ("Shatter", "Danshig Naadam") else 0,
                        dronePoints=random.randint(0, 80) if mtype == "Seize" else 0)
    # timelines
    tp = {}
    for t in TEAMS:
        seq, v, tt = [[0, 0]], 0, 0
        while tt < dur:
            tt += random.randint(5, 25)
            v = min(pts[t], v + random.randint(0, 30) + (8 if random.random() < .3 else 0))
            seq.append([round(min(tt, dur), 1), v])
        seq[-1] = [dur, pts[t]]
        tp[t] = seq
    bh, v, tt = [[0, 0]], 0, 0
    while tt < dur:
        tt += random.randint(4, 40)
        v = 0 if random.random() < .12 else min(140, v + random.choice([0, 10, 10, 20]))
        bh.append([round(tt, 1), v])
    me = next(p for p in players if p["isYou"])
    m = dict(id=f"6{random.getrandbits(92):023x}", dutyStart=iso(start - timedelta(seconds=90)), start=iso(start), durationSec=dur,
             map=mid, mapName=mname, mapType=mtype, dataCenter="Crystal", gameVersion="2026.10.21.0000.0000", pluginVersion="0.1.0.0",
             localPlayer=f"{YOU[0]} {YOU[1]}", favorite=random.random() < .08, tags=random.choice(["", "", "", "premade", "practice"]),
             wolfMarks=[1500, 1250, 1000][teams[my_team]["placement"]], seriesXP=[1500, 1250, 1000][teams[my_team]["placement"]],
             jobXP=0, teams=teams, players=players, teamPoints=tp, battleHigh=bh)
    if with_combat:
        enemies = [p for p in players if p["team"] != my_team]
        skills = SKILLS[myjob[0]]
        combat = []
        t = 3.0
        while t < dur:
            name, pot, cd = random.choice(skills[:3] if random.random() < .6 else skills)
            n = 1 + (random.random() < .25) * random.randint(1, 3)
            for _ in range(n):
                e = random.choice(enemies[:30])
                crit, dh = random.random() < .12, random.random() < .08
                amt = pot * random.uniform(.85, 1.15) * (1.5 if crit else 1) * (1.25 if dh else 1)
                row = dict(t=round(t, 1), dir="out", kind="action", id=29400 + skills.index((name, pot, cd)), name=name,
                           other=f'{e["name"]} {e["world"]}', otherPlayer=True, otherJob=e["job"], otherTeam=e["team"],
                           otherHp=random.randint(5000, 52000), otherMaxHp=52000)
                if amt > 0:
                    row["dmg"] = int(amt)
                if crit: row["crit"] = True
                if dh: row["dh"] = True
                combat.append(row)
            if random.random() < .1:
                combat.append(dict(t=round(t + .4, 1), dir="self", kind="hot", id=1302, name="Recuperate", other="(self)", otherPlayer=False, heal=15000))
            t += random.uniform(2.4, 4.5)
        t = 5.0
        while t < dur:
            e = random.choice(enemies[:30])
            row = dict(t=round(t, 1), dir="in", kind=random.choice(["action"] * 6 + ["dot"]), id=1, name=random.choice(INCOMING),
                       other=f'{e["name"]} {e["world"]}', otherPlayer=True, otherJob=e["job"], otherTeam=e["team"],
                       dmg=random.randint(2500, 12000), otherHp=40000, otherMaxHp=52000)
            if random.random() < .1: row["crit"] = True
            combat.append(row)
            t += random.uniform(3, 9)
        combat.sort(key=lambda c: c["t"])
        # kills & deaths
        outs = [c for c in combat if c["dir"] == "out" and c.get("dmg")]
        for c in random.sample(outs, min(me["kills"], len(outs))):
            c["kb"] = True
            combat.append(dict(t=round(c["t"] + .1, 1), dir="out", kind="death", id=c["id"], name=c["name"], other=c["other"],
                               otherPlayer=True, otherJob=c["otherJob"], otherTeam=c["otherTeam"]))
        ins = [c for c in combat if c["dir"] == "in" and c.get("dmg")]
        for c in random.sample(ins, min(me["deaths"], len(ins))):
            c["kb"] = True
            combat.append(dict(t=round(c["t"] + .1, 1), dir="in", kind="death", id=c["id"], name=c["name"], other=c["other"],
                               otherPlayer=True, otherJob=c["otherJob"], otherTeam=c["otherTeam"]))
        combat.sort(key=lambda c: c["t"])
        m["combat"] = combat
        m["combatSummary"] = summarize(combat)
    return m


def summarize(rows):
    def skills(src):
        g = {}
        for r in src:
            if r["kind"] == "death":
                continue
            k = (r["name"], r["kind"])
            x = g.setdefault(k, dict(name=r["name"], kind=r["kind"], hits=0, damage=0, heal=0, crits=0, directHits=0, maxHit=0, killingBlows=0))
            x["hits"] += 1; x["damage"] += r.get("dmg", 0); x["heal"] += r.get("heal", 0)
            x["crits"] += 1 if r.get("crit") else 0; x["directHits"] += 1 if r.get("dh") else 0
            x["maxHit"] = max(x["maxHit"], r.get("dmg", 0)); x["killingBlows"] += 1 if r.get("kb") else 0
        return sorted(g.values(), key=lambda x: -(x["damage"] + x["heal"]))

    def others(src):
        g = {}
        for r in src:
            if r["kind"] == "death":
                continue
            x = g.setdefault(r["other"], dict(name=r["other"], job=r.get("otherJob"), team=r.get("otherTeam"), hits=0, damage=0, heal=0, killingBlows=0))
            x["hits"] += 1; x["damage"] += r.get("dmg", 0); x["heal"] += r.get("heal", 0); x["killingBlows"] += 1 if r.get("kb") else 0
        return sorted(g.values(), key=lambda x: -x["damage"])

    out = [r for r in rows if r["dir"] == "out"]
    inc = [r for r in rows if r["dir"] == "in"]
    return dict(dealt=skills(out), taken=skills(inc), selfSkills=skills([r for r in rows if r["dir"] == "self"]),
                targets=others(out), sources=others(inc),
                kills=sum(1 for r in out if r["kind"] == "death"), deaths=sum(1 for r in inc if r["kind"] == "death"))


def main():
    tpl, out = sys.argv[1], sys.argv[2]
    n = int(sys.argv[3]) if len(sys.argv) > 3 else 60
    now = datetime(2026, 10, 31, 22, 0, tzinfo=timezone.utc)
    starts = sorted(now - timedelta(hours=random.uniform(0, 24 * 45)) for _ in range(n))
    matches = [make_match(i, s, with_combat=i >= n - 6) for i, s in enumerate(starts)]
    report = dict(schema=1, plugin="FLStats Plus 0.1.0.0 (SAMPLE DATA)", generated=iso(now), mode="history", matches=matches)
    data = json.dumps(report, separators=(",", ":")).replace("<", "\\u003c").replace(">", "\\u003e").replace("&", "\\u0026")
    html = open(tpl, encoding="utf-8").read()
    assert "/*__FLSTATS_DATA__*/null" in html
    open(out, "w", encoding="utf-8").write(html.replace("/*__FLSTATS_DATA__*/null", data))
    print(f"wrote {out}: {n} matches, {len(data)//1024} KB of data")


if __name__ == "__main__":
    main()
