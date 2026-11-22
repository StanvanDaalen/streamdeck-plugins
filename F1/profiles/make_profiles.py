"""Builds the profiles the F1 Race Control plugin brings along (manifest "Profiles"), one per Stream Deck type.

Stream Deck offers to install them with the plugin, only for the device types that are connected. Each profile
has one page in Stream Deck's own profile format (version 3.0, the one Stream Deck 6.5+ writes itself).

Run: python F1/profiles/make_profiles.py   (writes F1/sdPlugin/profiles/*.streamDeckProfile)
"""
import json
import os
import uuid
import zipfile

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "sdPlugin", "profiles")
NS = uuid.UUID("6f1c2b0e-5d1a-4c55-9a51-2f1e7c0d3a11")  # stable ids: the same file every build

PLUGIN = {"Name": "F1 Race Control", "UUID": "com.stan.f1"}

# key name per action (as in the plugin's manifest)
NAMES = {
    "back": "Back", "race": "Race", "quali": "Quali", "session": "Session", "close": "Start / end session",
    "hide": "Hide", "sync": "Party", "fullscreen": "Fullscreen", "replay": "Replay", "invite": "Invite",
    "start": "Start together", "live": "Go live", "pause": "Pause / play", "flags": "Flags",
    "latency": "Live delay", "volume": "Volume", "zone": "Flag lights",
}

# (file name, profile name, device model, keys {"col,row": action}, dials {"col,0": action})
PROFILES = [
    ("F1 Race Control Plus", "F1 Race Control", "20GBD9901",  # Stream Deck +: 4 x 2 keys, 4 dials
     {"0,0": "back", "1,0": "race", "2,0": "quali", "3,0": "session",
      "0,1": "hide", "1,1": "sync", "2,1": "fullscreen", "3,1": "close"},
     {"0,0": "latency", "1,0": "volume", "2,0": "flags", "3,0": "zone"}),
    ("F1 Race Control", "F1 Race Control", "20GAA9902",  # Stream Deck: 5 x 3 keys
     {"0,0": "back", "1,0": "race", "2,0": "quali", "3,0": "session", "4,0": "close",
      "0,1": "sync", "1,1": "hide", "2,1": "fullscreen", "3,1": "replay", "4,1": "invite",
      "0,2": "start", "1,2": "live", "2,2": "pause", "3,2": "flags"},
     {}),
    ("F1 Race Control XL", "F1 Race Control", "20GAT9902",  # Stream Deck XL: 8 x 4 keys
     {"0,0": "back", "1,0": "race", "2,0": "quali", "3,0": "session", "4,0": "close", "5,0": "sync", "6,0": "hide", "7,0": "fullscreen",
      "0,1": "replay", "1,1": "invite", "2,1": "start", "3,1": "live", "4,1": "pause", "5,1": "flags"},
     {}),
]


def sid(*parts):
    return str(uuid.uuid5(NS, "/".join(parts)))


def action(profile, pos, act, version):
    return {
        "ActionID": sid(profile, pos, act), "LinkedTitle": True, "Name": NAMES[act],
        "Plugin": {**PLUGIN, "Version": version}, "Resources": None, "Settings": {}, "State": 0,
        "States": [{}], "UUID": "com.stan.f1." + act,
    }


def build(version):
    os.makedirs(OUT, exist_ok=True)
    for file, name, model, keys, dials in PROFILES:
        prof = sid(file, "profile").upper()
        page, default = sid(file, "page"), sid(file, "default")
        top = {"Device": {"Model": model, "UUID": ""}, "Name": name,
               "Pages": {"Current": page, "Default": default, "Pages": [page]}, "Version": "3.0"}
        controllers = [{"Actions": {p: action(file, p, a, version) for p, a in keys.items()}, "Type": "Keypad"}]
        empty = [{"Actions": None, "Type": "Keypad"}]
        if dials:
            controllers.append({"Actions": {p: action(file, "d" + p, a, version) for p, a in dials.items()}, "Type": "Encoder"})
            empty.append({"Actions": None, "Type": "Encoder"})
        root = prof + ".sdProfile/"
        path = os.path.join(OUT, file + ".streamDeckProfile")
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            z.writestr(root + "manifest.json", json.dumps(top, separators=(",", ":")))
            z.writestr(root + "Profiles/" + page.upper() + "/manifest.json",
                       json.dumps({"Controllers": controllers, "Icon": "", "Name": ""}, separators=(",", ":")))
            z.writestr(root + "Profiles/" + default.upper() + "/manifest.json",
                       json.dumps({"Controllers": empty, "Icon": "", "Name": ""}, separators=(",", ":")))
        print("written", os.path.relpath(path, os.path.join(HERE, "..")), f"({len(keys)} keys, {len(dials)} dials)")


if __name__ == "__main__":
    with open(os.path.join(HERE, "..", "sdPlugin", "manifest.json"), encoding="utf-8-sig") as f:
        build(json.load(f)["Version"])
