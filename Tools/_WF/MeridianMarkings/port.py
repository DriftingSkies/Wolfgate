"""Ports Meridian Rift's character markings into Wolfgate.

Meridian Rift (https://github.com/Aphelion-Moon/Meridian-Rift) is an SS13 server on the NovaSector
codebase. Its tails, ears, snouts, horns, frills, wings, hair and the rest are sprite accessory datums
drawn from DMI sheets, and its body markings are datums that draw one state per body part. This reads
both out of a Meridian Rift checkout, drops every marking Wolfgate already has, and writes what is left
as RSIs, marking prototypes and names under the MeridianMarkings module.

What the SS13 art becomes here:

    layers      An accessory draws on up to three SS13 layers. Everything above the body is merged
                into one sprite per colour, keeping the game's own stacking; art below the body
                becomes a second sprite on TailBehind that follows the first one's colour.
    tails       Keep that split, so a tail is in front of the mob facing north and behind it facing
                south as it was drawn. South-facing art that could cover anatomy is moved behind the
                body with the zone Tools/_WF/Genitals/tails measures, which is what the tests check.
    wagging     A tail with wagging states gets an <id>Animated variant with the same sprites.
    body        One marking per body part, like the markings already here. Art the foot or hand
                sprite would cover is cut out of the limb; a leg's foot art is its own marking.
    hair        One sprite per style, with the ponytails and braids SS13 layers around headwear
                laid over it. The human hair here was redrawn from the same SS13 styles, so a style is
                also dropped when one of the same name or state is here and sits in the same place.
    species     Accessories go to the species the anthro parts already here are offered to, hair to
                everyone as the human hair here is. A body marking goes to every species whose body
                part has the shape of the human one, which is the body all of this art was drawn on.

Left out: what nobody can pick in the source game, hair gradients, underwear and anatomy (not
markings), taur bodies (no such body here), digitigrade leg art (drawn for a leg no species here has),
IPC screens (drawn for another head, and the same set is here already), Teshari parts (Resomi carry
that set) and Vox body markings, beaks and hair (drawn for the SS13 Vox, whose body the Vox here do
not share).

Run from the repository root (requires Pillow and PyYAML):
    python Tools/_WF/MeridianMarkings/port.py --source <Meridian-Rift checkout>
    python Tools/_WF/MeridianMarkings/port.py --source <Meridian-Rift checkout> --review <folder>
    python Tools/_WF/MeridianMarkings/port.py --source <Meridian-Rift checkout> --write
Then: python Tools/_WF/Ci/modules.py --write
"""
import argparse
import json
import math
import os
import re
import shutil
import subprocess
import sys

import yaml
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dedupe  # noqa: E402
import review  # noqa: E402
import source  # noqa: E402
import wolfgate as wg  # noqa: E402

MODULE = "MeridianMarkings"
ID_PREFIX = "WFMeridian"
SOURCE_URL = "https://github.com/Aphelion-Moon/Meridian-Rift"
TEXTURE_ROOT = "_WF/%s" % MODULE
OUT_TEXTURES = os.path.join(wg.TEXTURES, "_WF", MODULE)
OUT_PROTOTYPES = os.path.join(wg.PROTOTYPES, "_WF", MODULE)
OUT_LOCALE = os.path.join(wg.ROOT, "Resources", "Locale", "en-US", "_WF", MODULE)

BEHIND_LAYER = "TailBehind"
# A marking already here that carries the species list of the anthro parts.
ANTHRO_EXAMPLE = "TailFox"
# The body the SS13 art was drawn on, and how closely a species' body part has to overlap its one
# for a body marking to be offered to that species.
DRAWN_ON = "Human"
SAME_BODY = 0.90
# Two SS13 markings with the same art are one marking only if this close in how much of it is below the body.
SAME_LAYERS = 0.25

# The hair here that was taken from /tg/'s sheet state by state, so its state names are that sheet's:
# a style from the same sheet under the same state is that style, however far the redraw went.
SS13_HAIR = ("Mobs/Customization/human_hair.rsi", "Mobs/Customization/human_facial_hair.rsi")
TG_HAIR = "icons/mob/human/human_face.dmi"
# A style from any other sheet that only shares a name with one here is that style if they overlap this
# much. NovaSector reuses a few /tg/ names for drawings of its own.
SAME_STYLE = 0.40

VOX = {"SPECIES_VOX", "SPECIES_VOX_PRIMALIS"}
TESHARI = {"SPECIES_TESHARI"}
EVERYONE = "everyone"


class Kind:
    """One sprite accessory category: where its markings go and how they are named."""

    def __init__(self, key, file, token, noun, says, body_part, category, species=None, hair=False):
        self.key = key              # SS13 feature key, or the datum family for hair
        self.file = file            # prototype, locale and RSI file stem
        self.token = token          # id part
        self.noun = noun            # appended to the SS13 name; "(X)" is added as a qualifier, "" nothing
        self.says = says            # words that mean the SS13 name already says what it is
        self.body_part = body_part
        self.category = category
        self.species = species      # None for the anthro list, EVERYONE for no restriction
        self.hair = hair            # a hairstyle or facial hair rather than a layered accessory


KINDS = [
    Kind("tail", "tails", "Tail", "Tail", ("tail",), "Tail", "Tail"),
    Kind("ears", "ears", "Ears", "Ears", ("ear", "antenna"), "HeadTop", "HeadTop"),
    Kind("snout", "snouts", "Snout", "Snout", ("snout", "beak", "mandible"), "Snout", "Snout"),
    Kind("horns", "horns", "Horns", "Horns", ("horn", "antler", "antenna"), "HeadTop", "HeadTop"),
    Kind("frills", "frills", "Frills", "Frills", ("frill",), "HeadSide", "HeadSide"),
    # Wings sit on the tail layers and are picked under Chest, as the wings already here are.
    Kind("wings", "wings", "Wings", "Wings", ("wing", "legs"), "Tail", "Chest"),
    Kind("spines", "spines", "Spines", "Spines", ("spine",), "Chest", "Chest"),
    Kind("fluff", "fluff", "Fluff", "Fluff", ("fluff", "mane"), "HeadSide", "HeadSide"),
    Kind("neck_acc", "accessories", "Neck", "(Neck)", (), "HeadSide", "HeadSide"),
    Kind("head_acc", "accessories", "Head", "(Head)", ("pom", "halo"), "HeadTop", "HeadTop"),
    Kind("moth_antennae", "antennae", "Antennae", "Antennae", ("antenna",), "HeadTop", "HeadTop"),
    Kind("ipc_antenna", "antennae", "SynthAntenna", "Antenna", ("antenna",), "HeadTop", "HeadTop", ("IPC", "Synth")),
    Kind("skrell_hair", "skrell", "SkrellHair", "Skrell Hair", (), "HeadTop", "HeadTop", ("Skrell",)),
    Kind("xenodorsal", "xeno", "XenoDorsal", "Xeno Dorsal Tubes", (), "Chest", "Chest"),
    Kind("xenohead", "xeno", "XenoHead", "Xeno Crest", (), "HeadTop", "HeadTop"),
    Kind("caps", "accessories", "MushroomCap", "Mushroom Cap", (), "HeadTop", "HeadTop"),
    Kind("pod_hair", "accessories", "PodHair", "Pod Hair", (), "HeadTop", "HeadTop"),
    Kind("hair", "hair", "Hair", "", (), "Hair", "Hair", EVERYONE, hair=True),
    Kind("facial_hair", "facial_hair", "FacialHair", "", (), "FacialHair", "FacialHair", EVERYONE, hair=True),
]


class Part:
    """One body part a body marking can sit on."""

    def __init__(self, layer, category, label, family, covered_by=(), spill=None):
        self.layer = layer            # HumanoidVisualLayers name, also the id part
        self.category = category
        self.label = label
        self.family = family          # layers whose shape decides which species the marking fits
        self.covered_by = covered_by  # layers drawn over this one, whose pixels the marking cannot show on
        self.spill = spill            # the part that art under those layers becomes


PARTS = {
    "head": Part("Head", "Head", "Head", ("Head",)),
    "chest": Part("Chest", "Chest", "Chest", ("Chest",)),
    "l_arm": Part("LArm", "Arms", "Left Arm", ("LArm", "RArm"), ("LHand",)),
    "r_arm": Part("RArm", "Arms", "Right Arm", ("LArm", "RArm"), ("RHand",)),
    "l_hand": Part("LHand", "Arms", "Left Hand", ("LHand", "RHand")),
    "r_hand": Part("RHand", "Arms", "Right Hand", ("LHand", "RHand")),
    "l_leg": Part("LLeg", "Legs", "Left Leg", ("LLeg", "RLeg"), ("LFoot",), "l_foot"),
    "r_leg": Part("RLeg", "Legs", "Right Leg", ("LLeg", "RLeg"), ("RFoot",), "r_foot"),
    "l_foot": Part("LFoot", "Legs", "Left Foot", ("LFoot", "RFoot")),
    "r_foot": Part("RFoot", "Legs", "Right Foot", ("LFoot", "RFoot")),
}


def slug(text):
    return re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")


def camel(text):
    words = re.findall(r"[A-Za-z0-9]+", text.replace("'", ""))
    return "".join(word[:1].upper() + word[1:] for word in words)


def display_name(kind, name):
    """The SS13 name with what it is added: "Fox (Alt 2)" under tails is "Fox Tail (Alt 2)"."""
    if not kind.noun or any(word in name.lower() for word in kind.says):
        return name
    if kind.noun.startswith("("):
        return "%s %s" % (name, kind.noun)
    base, bracket, rest = name.partition(" (")
    return "%s %s%s%s" % (base, kind.noun, bracket, rest)


class Planned:
    """A candidate the port keeps, with everything the prototype and the locale need."""

    def __init__(self, candidate, marking_id, name, body_part, category, species, file):
        self.candidate = candidate
        self.id = marking_id
        self.name = name
        self.body_part = body_part
        self.category = category
        self.species = species      # list, [] for a variant nobody picks, None for everyone
        self.file = file
        self.rsi = None
        self.sex = None
        self.also = []              # SS13 names of markings with the same art that this one stands for


class Outcome:
    """What became of one candidate, for the report."""

    def __init__(self, candidate, verdict, detail="", near=()):
        self.candidate = candidate
        self.verdict = verdict      # ported / existing / alias / skipped
        self.detail = detail
        self.near = list(near)


# ------------------------------------------------------------------------------- the port


class Port:
    def __init__(self, source_root):
        self.src = source.Source(source_root)
        self.source_root = source_root
        self.bodies = wg.Bodies()
        wg.set_occluder([self.bodies.portrait(DRAWN_ON, d, size=wg.CANVAS) for d in range(4)])
        self.existing = wg.existing_markings(skip_under=("_WF/%s" % MODULE,))
        self.existing_ids = {marking.id for marking in self.existing}
        with open(os.path.join(HERE, "decisions.yml"), encoding="utf-8") as f:
            decisions = yaml.safe_load(f) or {}
        self.same = decisions.get("same") or {}
        self.new = set(decisions.get("new") or [])
        anthro = next((m.species for m in self.existing if m.id == ANTHRO_EXAMPLE), None)
        if not anthro:
            raise SystemExit("%s is gone; the anthro species list has to come from somewhere else" % ANTHRO_EXAMPLE)
        self.anthro = list(anthro)
        self._taken_names = {}   # display names in use, per marking category
        self._styles = self._hair_here()
        self.outcomes = []
        self.planned = []
        # Shared by every category: SS13 files the same art under two slots, an antenna as ears and as horns.
        self._accepted = []   # kept; a later candidate with the same art stands behind one of these
        self._dropped = []    # (candidate the repository already has, the id it has it under)
        self._prints = {}
        self._zone = None
        self._share_cache = {}
        self._ids = set()
        self._states = {}

    # ------------------------------------------------------------------------- collecting

    def run(self):
        for kind in KINDS:
            if kind.hair:
                candidates = self.src.hair_candidates(kind.key)
            else:
                candidates = self.src.accessory_candidates(kind.key)
            candidates = [c for c in candidates if c.wag_of is None]
            # Of look-alikes the first one met is kept, so the one with the most colours to set goes first.
            candidates.sort(key=lambda c: -sum(1 for s in c.sprites if s.parent is None and s.tinted))
            self._keep(kind, candidates)
        self._keep(None, self._body_candidates())

    def _hair_here(self):
        """Hair already in the repository by what it is called: {category: {name or SS13 state: [marking]}}."""
        names = wg.marking_names()
        styles = {}
        for marking in self.existing:
            if marking.id in names:
                self._taken_names.setdefault(marking.category, set()).add(_plain(names[marking.id]))
            if marking.category not in ("Hair", "FacialHair") or not marking.sprites:
                continue
            keys = styles.setdefault(marking.category, {})
            if marking.id in names:
                keys.setdefault(("name", _plain(names[marking.id])), []).append(marking)
            rsi, state = marking.sprites[0]
            if rsi.strip("/") in SS13_HAIR:
                keys.setdefault(("state", _plain(state)), []).append(marking)
        return styles

    def _same_style(self, kind, candidate, subject):
        """A hairstyle here that is the same style redrawn: called the same, or kept under the same SS13 state."""
        keys = self._styles.get(kind.category, {})
        if candidate.icon == TG_HAIR:
            state = re.sub(r"^(hair|facial)_", "", candidate.icon_state)
            for marking in keys.get(("state", _plain(state)), ()):
                return marking
        for marking in keys.get(("name", _plain(candidate.name)), ()):
            if wg.overlap(subject.union, marking.union) >= SAME_STYLE:
                return marking
        return None

    def _free_name(self, category, name):
        """A display name nothing in the category has yet: one that is taken gets a number after it."""
        taken = self._taken_names.setdefault(category, set())
        free, n = name, 2
        while _plain(free) in taken:
            free = "%s %d" % (name, n)
            n += 1
        taken.add(_plain(free))
        return free

    def _body_candidates(self):
        """Body markings with the art other body layers would cover cut out, and a leg's foot art split off."""
        out = []
        for candidate in self.src.body_candidates():
            part = PARTS[candidate.zone]
            if not part.covered_by:
                out.append(candidate)
                continue
            sprite = candidate.sprites[0]
            # Markings already here were taken from the same sheets uncut, so the uncut art is what
            # tells whether this one is among them.
            whole = source.Candidate("body", candidate.typepath, candidate.name)
            whole.zone, whole.feminine, whole.size = candidate.zone, candidate.feminine, candidate.size
            whole.sprites.append(source.Sprite("", sprite.frames, None))
            kept, spilt = [], []
            for direction, frame in enumerate(sprite.frames[0]):
                cover = self.bodies.cover(DRAWN_ON, part.covered_by, direction, frame.size)
                kept.append(source.erase(frame, _as_alpha(cover)))
                spilt.append(source.keep(frame, cover))
            if not source.is_blank([kept]):
                sprite.frames = [kept]
                candidate.whole = whole
                out.append(candidate)
            if part.spill and sum(_pixels(image) for image in spilt) >= 2:
                foot = source.Candidate("body", candidate.typepath, candidate.name)
                foot.icon, foot.icon_state = candidate.icon, candidate.icon_state
                foot.zone, foot.species = part.spill, candidate.species
                foot.default_color, foot.size = candidate.default_color, candidate.size
                foot.sprites.append(source.Sprite("", [spilt], None))
                foot.whole = whole
                out.append(foot)
        return out

    # ---------------------------------------------------------------------------- deciding

    def _keep(self, kind, candidates):
        accepted, dropped, prints = self._accepted, self._dropped, self._prints
        wholes = {}
        for candidate in candidates:
            reason = self._left_out(kind, candidate)
            if reason:
                self.outcomes.append(Outcome(candidate, "skipped", reason))
                continue

            patch = kind is None

            def same_part(other):
                # The torso art drawn for the female body is compared with the plain one too: where the
                # two barely differ, one marking serves both.
                return other.candidate.zone == candidate.zone

            def here(found):
                """The marking a candidate is, by art or by look, out of what it was compared with."""
                return next((match for match in found if match.same or match.looks), None)

            if candidate.key in self.same:
                existing_id = self.same[candidate.key]
                dropped.append((dedupe.Accepted(candidate, dedupe.Subject(candidate, patch)), existing_id))
                self.outcomes.append(Outcome(candidate, "existing", "%s (decisions.yml)" % existing_id))
                continue
            whole = candidate.whole
            if whole is not None and candidate.key not in self.new:
                if id(whole) not in wholes:
                    wholes[id(whole)] = here(dedupe.Subject(whole, patch).matches(self.existing))
                if wholes[id(whole)] is not None:
                    self.outcomes.append(Outcome(candidate, "existing", wholes[id(whole)].describe()))
                    continue
            print_ = (candidate.zone, dedupe.fingerprint(candidate))
            if print_ in prints:
                prints[print_].also.append(candidate.name)
                self.outcomes.append(Outcome(candidate, "alias", prints[print_].id))
                continue
            subject = dedupe.Subject(candidate, patch)
            near = []
            if candidate.key not in self.new:
                style = self._same_style(kind, candidate, subject) if kind is not None and kind.hair else None
                if style is not None:
                    dropped.append((dedupe.Accepted(candidate, subject), style.id))
                    self.outcomes.append(Outcome(candidate, "existing", "%s (the same style by name)" % style.id))
                    continue
                found = subject.matches(self.existing)
                match = here(found)
                if match is not None:
                    dropped.append((dedupe.Accepted(candidate, subject), match.existing.id))
                    self.outcomes.append(Outcome(candidate, "existing", match.describe()))
                    continue
                near = [match.describe() for match in found[:2]]
                # A second SS13 marking that looks like one already dealt with goes the way that one
                # went: a snout and its "(Top)" twin, the same ears with the inside as a second colour.
                # Art below the body and the same art above it are told apart, as the wings here are.
                def twin_of(others):
                    return next((match for match in subject.matches(others)
                                 if (match.same or match.looks)
                                 and abs(match.existing.behind_share - subject.behind_share) <= SAME_LAYERS), None)

                gone = twin_of([a for a, _ in dropped if same_part(a)])
                if gone is not None:
                    existing_id = next(eid for a, eid in dropped if a is gone.existing)
                    self.outcomes.append(Outcome(candidate, "existing", "%s (same art as %s)" % (
                        existing_id, gone.existing.candidate.name)))
                    continue
                twin = twin_of([a for a in accepted if same_part(a)])
                if twin is not None:
                    planned = next(p for p in self.planned if p.candidate is twin.existing.candidate)
                    planned.also.append(candidate.name)
                    self.outcomes.append(Outcome(candidate, "alias", planned.id))
                    continue
            planned = self._plan(kind, candidate)
            accepted.append(dedupe.Accepted(candidate, subject))
            prints[print_] = planned
            self.outcomes.append(Outcome(candidate, "ported", planned.id, near))

    def _left_out(self, kind, candidate):
        species = set(candidate.species or ())
        if self.src.datums.value(candidate.typepath, "locked", False) is True:
            return "locked in the source game: nobody can pick it there"
        if species == TESHARI:
            return "Teshari part; Resomi carry that set already"
        if species and species <= VOX:
            if kind is None:
                return "drawn for the SS13 Vox body, which the Vox here do not share"
            if kind.key == "snout":
                return "the Vox head here has its beak drawn in"
            if kind.hair:
                return "drawn for the SS13 Vox head; the Vox here have hair of their own"
        return None

    # ----------------------------------------------------------------------------- planning

    def _plan(self, kind, candidate):
        if kind is None:
            planned = self._plan_body(candidate)
        else:
            planned = self._plan_accessory(kind, candidate)
        self.planned.append(planned)
        return planned

    def _unique_id(self, wanted):
        marking_id, n = wanted, 2
        while marking_id in self._ids or marking_id in self.existing_ids:
            marking_id = "%s%d" % (wanted, n)
            n += 1
        self._ids.add(marking_id)
        return marking_id

    def _name_states(self, planned, stem):
        """Gives every sprite its RSI state: <stem>[_wag]<_channel>[_BEHIND], unique within the RSI."""
        candidate = planned.candidate
        size = candidate.size
        planned.rsi = planned.file if size == (source.TILE, source.TILE) else "%s_%dx%d" % (planned.file, size[0], size[1])
        taken = self._states.setdefault(planned.rsi, set())

        def names(base):
            return [base + sprite.suffix + ("_BEHIND" if sprite.behind else "") for sprite in candidate.sprites]

        base, n = stem, 2
        while taken.intersection(names(base)):
            base = "%s_%d" % (stem, n)
            n += 1
        for sprite, state in zip(candidate.sprites, names(base)):
            sprite.state = state
            taken.add(state)

    def _plan_accessory(self, kind, candidate):
        if kind.species == EVERYONE:
            species = None
        elif kind.species is not None:
            species = list(kind.species)
        elif candidate.species and set(candidate.species) <= VOX:
            species = ["Vox"]
        else:
            species = list(self.anthro)
        marking_id = self._unique_id(ID_PREFIX + kind.token + camel(candidate.name))
        planned = Planned(candidate, marking_id, self._free_name(kind.category, display_name(kind, candidate.name)),
                          kind.body_part, kind.category, species, kind.file)
        wag = candidate.wag
        if kind.body_part == "Tail":
            self._off_anatomy(candidate)
            if wag is not None:
                self._off_anatomy(wag)
                source.match_structure(candidate, wag)
        for sprite in candidate.sprites + (wag.sprites if wag else []):
            if sprite.behind:
                sprite.layer = BEHIND_LAYER
        self._name_states(planned, slug(candidate.name))
        if wag is not None:
            # Wagging swaps a tail for <id>Animated; nobody picks that one, so no species may.
            self._ids.add(marking_id + "Animated")
            animated = Planned(wag, marking_id + "Animated", "%s (Animated)" % planned.name,
                               kind.body_part, kind.category, [], kind.file)
            self._name_states(animated, slug(candidate.name) + "_wag")
            self.planned.append(animated)
        return planned

    def _plan_body(self, candidate):
        part = PARTS[candidate.zone]
        sex = "Female" if candidate.feminine else "Male"
        species = self._shares(part.family, sex)
        label = part.label + (", Feminine" if candidate.feminine else "")
        marking_id = self._unique_id(ID_PREFIX + "Marking" + camel(candidate.name) + part.layer
                                     + ("Feminine" if candidate.feminine else ""))
        planned = Planned(candidate, marking_id, self._free_name(part.category, "%s (%s)" % (candidate.name, label)),
                          part.layer, part.category, species, "body_" + part.category.lower())
        if candidate.feminine:
            # The art follows the female torso, which only a female character has.
            planned.sex = "Female"
        self._name_states(planned, "%s_%s%s" % (slug(candidate.name), candidate.zone, "_f" if candidate.feminine else ""))
        return planned

    def _shares(self, family, sex):
        """Species whose body part has the shape of the one the art was drawn on."""
        key = (family, sex)
        if key not in self._share_cache:
            want = self.bodies.part(DRAWN_ON, family, sex)
            self._share_cache[key] = sorted(
                species for species in self.bodies.species
                if wg.overlap(self.bodies.part(species, family, sex), want) >= SAME_BODY)
        return self._share_cache[key]

    # ------------------------------------------------------------------------- tail layers

    def _zone_mask(self, size):
        """South-facing pixels anatomy can occupy on any species, as the tail tests measure them."""
        if self._zone is None:
            sys.path.insert(0, os.path.join(wg.ROOT, "Tools", "_WF", "Genitals", "tails"))
            import split_tails_batch as tails
            self._zone = tails.AnatomyZone(tails.Prototypes())
        return self._zone.mask(None, size)

    def _off_anatomy(self, candidate):
        """
        Makes a marking on the tail layers fit the rules those layers have here: nothing above the body
        may cover anatomy facing south, and every sprite below the body follows the colour of a sprite
        above it that comes first.
        """
        mask = self._zone_mask(candidate.size)
        clear = _as_alpha(mask)
        for top in [s for s in candidate.sprites if not s.behind]:
            for index, frame in enumerate(top.frames):
                inside = source.keep(frame[0], mask)
                if source.alpha_bbox(inside) is None:
                    continue
                behind = next((s for s in candidate.sprites if s.behind and s.channel == top.channel), None)
                if behind is None:
                    behind = source.Sprite(top.suffix, [[source.blank(candidate.size) for _ in range(4)]
                                                        for _ in top.frames], top.delays, True, top.channel, top.tinted)
                    candidate.sprites.append(behind)
                if len(behind.frames) < len(top.frames):
                    behind.frames = [[image.copy() for image in behind.frames[i % len(behind.frames)]]
                                     for i in range(len(top.frames))]
                    behind.delays = top.delays
                behind.frames[index][0].alpha_composite(inside)
                frame[0] = source.erase(frame[0], clear)
        tops = {s.channel for s in candidate.sprites if not s.behind}
        for behind in [s for s in candidate.sprites if s.behind and s.channel not in tops]:
            candidate.sprites.append(source.Sprite(behind.suffix, [[source.blank(candidate.size) for _ in range(4)]],
                                                   None, False, behind.channel, behind.tinted))
        source.order_sprites(candidate)

    # ------------------------------------------------------------------------------ writing

    def write(self):
        for folder in (OUT_TEXTURES, OUT_PROTOTYPES, OUT_LOCALE):
            if os.path.isdir(folder):
                shutil.rmtree(folder)
            os.makedirs(folder)
        commit = _git(self.source_root, "rev-parse", "HEAD")
        by_rsi = {}
        by_file = {}
        for planned in self.planned:
            by_rsi.setdefault(planned.rsi, []).append(planned)
            by_file.setdefault(planned.file, []).append(planned)
        for rsi, group in sorted(by_rsi.items()):
            self._write_rsi(rsi, group, commit)
        for file, group in sorted(by_file.items()):
            group.sort(key=lambda p: (_base_name(p).lower(), p.id))
            _write_text(os.path.join(OUT_PROTOTYPES, file + ".yml"), "\n".join(self._prototype(p) for p in group))
            _write_text(os.path.join(OUT_LOCALE, file + ".ftl"), "\n".join(self._names(p) for p in group))

    def _write_rsi(self, rsi, group, commit):
        folder = os.path.join(OUT_TEXTURES, rsi + ".rsi")
        os.makedirs(folder)
        size = group[0].candidate.size
        states = []
        icons = set()
        for planned in group:
            icons.add(planned.candidate.icon)
            for sprite in planned.candidate.sprites:
                frames = len(sprite.frames)
                columns = max(1, math.ceil(math.sqrt(4 * frames)))
                rows = math.ceil(4 * frames / columns)
                sheet = Image.new("RGBA", (columns * size[0], rows * size[1]), (0, 0, 0, 0))
                # An RSI sheet runs facing by facing, each facing's frames in order.
                for direction in range(4):
                    for frame in range(frames):
                        index = direction * frames + frame
                        sheet.paste(sprite.frames[frame][direction],
                                    ((index % columns) * size[0], (index // columns) * size[1]))
                sheet.save(os.path.join(folder, sprite.state + ".png"), optimize=True)
                state = {"name": sprite.state, "directions": 4}
                if frames > 1:
                    delays = [round(d, 3) for d in (sprite.delays or [0.1] * frames)]
                    state["delays"] = [delays] * 4
                states.append(state)
        states.sort(key=lambda s: s["name"])
        meta = {
            "version": 1,
            "license": "CC-BY-SA-3.0",
            "copyright": "Taken from Meridian Rift at %s/tree/%s (%s), which carries the art of NovaSector, "
                         "Skyrat-tg and tgstation. Layers merged per colour and centred for Wolfgate by "
                         "Tools/_WF/MeridianMarkings/port.py." % (SOURCE_URL, commit, ", ".join(sorted(icons))),
            "size": {"x": size[0], "y": size[1]},
            "states": states,
        }
        _write_text(os.path.join(folder, "meta.json"), json.dumps(meta, indent=2) + "\n")

    def _prototype(self, planned):
        candidate = planned.candidate
        sprite_path = "%s/%s.rsi" % (TEXTURE_ROOT, planned.rsi)
        lines = [
            "- type: marking",
            "  id: %s" % planned.id,
            "  bodyPart: %s" % planned.body_part,
            "  markingCategory: %s" % planned.category,
        ]
        if planned.species is not None:
            lines.append("  speciesRestriction: [%s]" % ", ".join(planned.species))
        if planned.sex:
            lines.append("  sexRestriction: [%s]" % planned.sex)
        if planned.category not in ("Hair", "FacialHair"):
            # Hair takes the character's hair colour, whatever a marking says about its own.
            lines += _coloring(candidate)
        layered = [s for s in candidate.sprites if s.layer]
        if layered:
            lines.append("  layering:")
            lines += ["    %s: %s" % (s.state, s.layer) for s in layered]
        linked = [s for s in candidate.sprites if s.parent is not None]
        if linked:
            lines.append("  colorLinks:")
            lines += ["    %s: %s" % (s.state, s.parent.state) for s in linked]
        lines.append("  sprites:")
        for sprite in candidate.sprites:
            lines.append("  - sprite: %s" % sprite_path)
            lines.append("    state: %s" % sprite.state)
        return "\n".join(lines) + "\n"

    def _names(self, planned):
        candidate = planned.candidate
        lines = ["marking-%s = %s" % (planned.id, planned.name)]
        boxes = [s for s in candidate.sprites if s.parent is None]
        for sprite in candidate.sprites:
            owner = sprite.parent or sprite
            if len(boxes) > 1:
                text = "%s (%s)" % (planned.name, owner.channel.capitalize())
            else:
                text = planned.name
            lines.append("marking-%s-%s = %s" % (planned.id, sprite.state, text))
        return "\n".join(lines) + "\n"

    # ---------------------------------------------------------------------------- reporting

    def summary(self):
        lines = []
        groups = [(kind.key, kind.key) for kind in KINDS] + [("body", "body")]
        total = {}
        for key, label in groups:
            tally = {}
            for outcome in self.outcomes:
                if outcome.candidate.kind == key:
                    tally[outcome.verdict] = tally.get(outcome.verdict, 0) + 1
                    total[outcome.verdict] = total.get(outcome.verdict, 0) + 1
            if tally:
                lines.append("%-14s ported %3d   already here %3d   same art as another %3d   left out %3d" % (
                    label, tally.get("ported", 0), tally.get("existing", 0), tally.get("alias", 0),
                    tally.get("skipped", 0)))
        lines.append("%-14s ported %3d   already here %3d   same art as another %3d   left out %3d" % (
            "TOTAL", total.get("ported", 0), total.get("existing", 0), total.get("alias", 0), total.get("skipped", 0)))
        animated = sum(1 for p in self.planned if p.candidate.wag_of is not None)
        lines.append("%d marking prototypes (%d of them wagging variants) in %d RSIs" % (
            len(self.planned), animated, len({p.rsi for p in self.planned})))
        if self.src.problems:
            lines.append("%d datums the source game cannot draw itself (see --report)" % len(self.src.problems))
        return "\n".join(lines)

    def report(self):
        return {
            "outcomes": [{
                "kind": o.candidate.kind, "name": o.candidate.name, "key": o.candidate.key,
                "verdict": o.verdict, "detail": o.detail, "near": o.near,
            } for o in self.outcomes],
            "markings": [{
                "id": p.id, "name": p.name, "bodyPart": p.body_part, "category": p.category,
                "species": p.species, "from": p.candidate.key, "alsoStandsFor": p.also,
            } for p in self.planned],
            "undrawable": self.src.problems,
        }

    def review(self, folder):
        """Contact sheets: every kept marking on a body, and every dropped one beside what it duplicates."""
        by_id = {marking.id: marking for marking in self.existing}
        written = []
        for key in [kind.key for kind in KINDS] + ["body"]:
            kept, dropped = [], []
            for planned in self.planned:
                candidate = planned.candidate
                if candidate.kind != key or candidate.wag_of is not None:
                    continue
                species = planned.species[0] if key == "body" and "Human" not in planned.species else "Human"
                kept.append(("%s  [%s]" % (planned.name, planned.id),
                             review.on_body(candidate, self.bodies, species, planned.sex or "Male"),
                             dedupe.candidate_picture(candidate)))
            for outcome in self.outcomes:
                if outcome.candidate.kind != key or outcome.verdict != "existing":
                    continue
                other = by_id.get(outcome.detail.split(" ")[0])
                if other is None:
                    continue
                dropped.append(("%s = %s" % (outcome.candidate.name, outcome.detail),
                                dedupe.candidate_picture(outcome.candidate), dedupe.picture_of(other)))
            written += review.sheets(kept, os.path.join(folder, "kept_%s_%%02d.png" % key))
            written += review.sheets(dropped, os.path.join(folder, "already_here_%s_%%02d.png" % key))
        return written


# ------------------------------------------------------------------------------- helpers


def _plain(text):
    """A name or state with everything but its letters and digits dropped, for comparing the two."""
    return re.sub(r"[^a-z0-9]", "", text.lower())


def _as_alpha(mask):
    """An 'L' mask as an image whose alpha is the mask, for source.erase."""
    image = Image.new("RGBA", mask.size, (0, 0, 0, 0))
    image.putalpha(mask)
    return image


def _pixels(image):
    return sum(1 for a in image.getchannel("A").tobytes() if a >= wg.ALPHA_MIN)


def _base_name(planned):
    """Sort key that keeps a wagging variant beside its tail."""
    return planned.name[:-len(" (Animated)")] if planned.candidate.wag_of is not None else planned.name


def _simple(color, indent):
    return ["%stype:" % indent, "%s  !type:SimpleColoring" % indent, '%s    color: "%s"' % (indent, color)]


def _hair(indent):
    # A second colour starts as the hair colour, the nearest thing here to SS13's secondary colour.
    return ["%stype:" % indent, "%s  !type:CategoryColoring" % indent, "%s    category: Hair" % indent,
            "%sfallbackTypes:" % indent, "%s  - !type:SkinColoring" % indent]


def _coloring(candidate):
    """The coloring block: what each colour box starts as before the player picks."""
    default = None
    layers = []
    boxes = [s for s in candidate.sprites if s.parent is None]
    # Art that carries its own colours starts untinted, whatever the source game let players do to it.
    own = {id(s) for s in boxes if not s.tinted or dedupe.carries_colour(s.frames[0])}
    own_colours = len(own) == len(boxes)
    if own_colours:
        default = _simple("#FFFFFF", "      ")
    elif candidate.default_color in ("secondary", "tertiary"):
        default = _hair("      ")
    elif candidate.default_color:
        default = _simple(candidate.default_color, "      ")
    for sprite in boxes:
        if own_colours:
            break
        if id(sprite) in own:
            # Art with its own colours beside art that is tinted: an ear's inside, say.
            layers.append(["      %s:" % sprite.state] + _simple("#FFFFFF", "        "))
        elif default is None and sprite.channel in ("secondary", "tertiary"):
            layers.append(["      %s:" % sprite.state] + _hair("        "))
    if default is None and not layers:
        return []
    lines = ["  coloring:"]
    if default is not None:
        lines += ["    default:"] + default
    if layers:
        lines.append("    layers:")
        for layer in layers:
            lines += layer
    return lines


def _write_text(path, text):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def _git(root, *args):
    try:
        return subprocess.run(["git", "-C", root] + list(args), capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--source", required=True, help="a Meridian Rift checkout")
    parser.add_argument("--write", action="store_true", help="write the RSIs, prototypes and names")
    parser.add_argument("--review", metavar="FOLDER", help="write contact sheets of what is kept and dropped")
    parser.add_argument("--report", metavar="FILE", help="write what became of every source marking, as JSON")
    args = parser.parse_args()

    if not os.path.isfile(os.path.join(args.source, "tgstation.dme")):
        raise SystemExit("%s is not an SS13 checkout (no tgstation.dme)" % args.source)
    port = Port(os.path.abspath(args.source))
    port.run()
    print(port.summary())
    if args.report:
        _write_text(args.report, json.dumps(port.report(), indent=1))
    if args.review:
        print("%d review sheets in %s" % (len(port.review(args.review)), args.review))
    if args.write:
        port.write()
        print("written; now run python Tools/_WF/Ci/modules.py --write")


if __name__ == "__main__":
    main()
