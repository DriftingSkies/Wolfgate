#!/usr/bin/env python3
"""Checks that the sprites and sounds the Vehicles prototypes name exist.

For every entity under Resources/Prototypes/_WF/Vehicles (or the files given) it follows the parents, then checks that the
Sprite's RSI exists and holds the state it names, that a turret overlay names a real RSI and states, that a hardpoint's
vehicle visual states are in some vehicle RSI, and that every audio path exists. Prints each problem and exits 1 if there are any.

    python Tools/_WF/Vehicles/check_sprite_states.py [--root <repo>] [prototype files...]
"""
import argparse
import glob
import json
import os
import sys

import yaml


class Loader(getattr(yaml, "CSafeLoader", yaml.SafeLoader)):
    """Reads the Robust tags (`!type:...`) as plain data."""


def _any(loader, suffix, node):
    if isinstance(node, yaml.MappingNode):
        return loader.construct_mapping(node)
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node)
    return loader.construct_scalar(node)


Loader.add_multi_constructor("!", _any)


class Checker:
    def __init__(self, root):
        self.root = root
        self.textures = os.path.join(root, "Resources", "Textures")
        self.rsis = {}
        self.protos = {}
        self.vehicle_ids = []
        self._vehicle_states = None
        for path in glob.glob(os.path.join(root, "Resources", "Prototypes", "**", "*.yml"), recursive=True):
            try:
                with open(path, encoding="utf-8") as handle:
                    data = yaml.load(handle, Loader=Loader)
            except yaml.YAMLError:
                continue
            for item in data if isinstance(data, list) else []:
                if isinstance(item, dict) and item.get("type") == "entity" and "id" in item:
                    self.protos[item["id"]] = item
                    if "/_WF/Vehicles/" in path.replace("\\", "/"):
                        self.vehicle_ids.append(item["id"])

    def states(self, rsi):
        if rsi not in self.rsis:
            meta = os.path.join(self.textures, rsi, "meta.json")
            if os.path.exists(meta):
                with open(meta, encoding="utf-8") as handle:
                    self.rsis[rsi] = {s["name"] for s in json.load(handle)["states"]}
            else:
                self.rsis[rsi] = None
        return self.rsis[rsi]

    def components(self, proto_id):
        """The components of a prototype with its parents' fields underneath, field by field."""
        result = {}
        seen = set()

        def collect(pid):
            if pid in seen or pid not in self.protos:
                return
            seen.add(pid)
            parents = self.protos[pid].get("parent")
            for parent in [parents] if isinstance(parents, str) else parents or []:
                collect(parent)
            for comp in self.protos[pid].get("components", []) or []:
                result.setdefault(comp["type"], {}).update({k: v for k, v in comp.items() if k != "type"})

        collect(proto_id)
        return result

    def vehicle_states(self):
        """The states of the vehicle sheets: the RSIs in the Vehicles folder and any other RSI a vehicle's sprite names."""
        if self._vehicle_states is not None:
            return self._vehicle_states

        found = set()
        for rsi in glob.glob(os.path.join(self.textures, "_WF", "Vehicles", "*.rsi")):
            found |= self.states(os.path.relpath(rsi, self.textures).replace("\\", "/")) or set()

        for pid in self.vehicle_ids:
            comps = self.components(pid)
            sprite = comps.get("Sprite")
            if "RMCVehicle" not in comps or not sprite:
                continue

            sheets = [sprite.get("sprite")] + [layer.get("sprite") for layer in sprite.get("layers", []) or [] if isinstance(layer, dict)]
            for sheet in sheets:
                if sheet:
                    found |= self.states(sheet) or set()

        self._vehicle_states = found
        return found

    def check_file(self, path):
        problems = []
        vehicle_states = self.vehicle_states()
        with open(path, encoding="utf-8") as handle:
            entities = yaml.load(handle, Loader=Loader) or []
        for ent in entities:
            if not isinstance(ent, dict) or ent.get("type") != "entity":
                continue
            eid = ent["id"]
            comps = self.components(eid)

            sprite = comps.get("Sprite")
            if sprite and sprite.get("sprite"):
                states = self.states(sprite["sprite"])
                if states is None:
                    problems.append("%s: no RSI %s" % (eid, sprite["sprite"]))
                else:
                    named = [sprite["state"]] if "state" in sprite else []
                    for layer in sprite.get("layers", []) or []:
                        if isinstance(layer, dict) and "state" in layer and not layer.get("sprite"):
                            named.append(layer["state"])
                    problems += ["%s: no state %s in %s" % (eid, n, sprite["sprite"]) for n in named if n not in states]

            turret = comps.get("VehicleTurret")
            if turret and turret.get("showOverlay"):
                states = self.states(turret.get("overlayRsi", ""))
                if states is None:
                    problems.append("%s: no overlay RSI %s" % (eid, turret.get("overlayRsi")))
                else:
                    for key in ("overlayState", "overlayDamagedState"):
                        if turret.get(key) and turret[key] not in states:
                            problems.append("%s: no overlay state %s in %s" % (eid, turret[key], turret["overlayRsi"]))

            visual = comps.get("HardpointVisual")
            if visual:
                for key in ("vehicleState", "damagedVehicleState"):
                    if visual.get(key) and visual[key] not in vehicle_states:
                        problems.append("%s: %s %s is in no vehicle RSI" % (eid, key, visual[key]))

            problems += ["%s: no sound %s" % (eid, s) for s in self.sounds(ent.get("components", []))
                         if not os.path.exists(os.path.join(self.root, "Resources", s.lstrip("/")))]
        return problems

    def sounds(self, node):
        if isinstance(node, dict):
            for value in node.values():
                yield from self.sounds(value)
        elif isinstance(node, list):
            for value in node:
                yield from self.sounds(value)
        elif isinstance(node, str) and node.startswith("/Audio/") and node.endswith(".ogg"):
            yield node


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--root", default=os.getcwd(), help="the repository root")
    parser.add_argument("files", nargs="*", help="prototype files; default is everything under Resources/Prototypes/_WF/Vehicles")
    args = parser.parse_args()

    files = args.files or glob.glob(os.path.join(args.root, "Resources", "Prototypes", "_WF", "Vehicles", "**", "*.yml"), recursive=True)
    checker = Checker(args.root)
    problems = []
    for path in sorted(files):
        problems += checker.check_file(path)

    for problem in problems:
        print(problem)
    print("checked %d files, %d problems" % (len(files), len(problems)))
    return 1 if problems else 0


if __name__ == "__main__":
    sys.exit(main())
