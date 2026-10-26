"""Ports Meridian Rift's clothing into Wolfgate.

Meridian Rift (https://github.com/Aphelion-Moon/Meridian-Rift) is an SS13 server on the NovaSector codebase.
This reads every wearable /obj/item/clothing type out of a Meridian Rift checkout (tgstation's, NovaSector's and
Meridian's own), drops what Wolfgate already has, and writes the rest as RSIs, entity prototypes, names,
loadouts and vending stock under the MeridianClothing module.

What the SS13 art becomes here:

    sprites     Each item gets one RSI: its icon, its worn sprite as the slot's equipped state and its in-hand
                sprites. Art of different sizes is centred on the largest. Species get the human sprite through
                their displacement maps (Vox, Resomi, Avali, the LegStyle legs), never the SS13 species sheets,
                which were drawn for bodies the species here don't have.
    greyscale   A GAGS item is drawn from its configs as one layer per colour, adjacent layers of one colour
                merged. A layer's state ends in -c<N> for colour slot N and starts at the SS13 default; the
                WFGags component (Gags module) recolours them, and the loadout window lets players pick.
                Blends the engine can't layer (multiply, add) are baked in the default colours.
    function    A type whose SS13 job is protection (armour, helmets, EVA suits, gas masks, HUDs, insulated
                gloves, bio and radiation gear) takes the Wolfgate item with that job as its parent. On top of
                that every item gets what its own SS13 stats give it, whatever its typepath: armour values,
                insulation (siemens 0), internals (a mask that takes them), pressure, fire and cold protection,
                flash protection, HUDs, no-slip and slowdown. Protective items are not put in loadouts or
                vending machines: their price and balance are the server's call, so they are admin and
                mapping spawns until someone places them. SS13 traits with no counterpart here (fast cuffing,
                quiet steps, the admin suit's immunities) are left out.
    duplicates  An item is already here when its worn sprite in its default colours matches a worn state of
                any RSI here, or when a plain (single-colour) item shares the name of a clothing item here.
                Meridian's own subtypes that share their sprites are one item.

Left out: abstract types, items with no icon or worn sprite, plasmaman suits (no such species here), MOD and
chameleon gear, magboots, night-vision and thermal glasses, taur, digitigrade and species sheets (displacement
maps fit the human sprite instead), adjusted and toggled states (rolled jumpsuits, open jackets), and hoods
as part of their coat (a hood is its own head item).

Distribution: each item gets a theme from its typepath (security, medical, cargo, costume, ...). A loadout
group gets the items whose theme matches one of the roles using it and whose slot is the group's. Faction
roles (TSFMC, USSP, PDV, MMC, Viper Group, ASF) keep their uniforms. Vending machines get their department's
items; ClothesMate gets the everyday civilian clothes. Adult, admin and antagonist items go nowhere.

Run from the repository root (requires Pillow and PyYAML):
    python Tools/_WF/MeridianClothing/port.py --source <Meridian-Rift checkout> [--report <file>]
    python Tools/_WF/MeridianClothing/port.py --source <Meridian-Rift checkout> --write
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

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)

import dedupe  # noqa: E402
import repo  # noqa: E402
import source  # noqa: E402

MODULE = "MeridianClothing"
ID_PREFIX = "WFMeridian"
SOURCE_URL = "https://github.com/Aphelion-Moon/Meridian-Rift"
TEXTURE_ROOT = "_WF/%s" % MODULE
OUT_TEXTURES = os.path.join(repo.TEXTURES, "_WF", MODULE)
OUT_PROTOTYPES = os.path.join(repo.PROTOTYPES, "_WF", MODULE)
OUT_LOCALE = os.path.join(repo.ROOT, "Resources", "Locale", "en-US", "_WF", MODULE)

# Wolfgate slot -> folder for its RSIs, prototypes and names.
FOLDERS = {
    "jumpsuit": "Uniforms", "outerClothing": "OuterClothing", "head": "Head", "mask": "Masks",
    "shoes": "Shoes", "gloves": "Hands", "eyes": "Eyes", "neck": "Neck", "ears": "Ears",
}

# Wolfgate slot -> parent of a clothing-only item.
PARENTS = {
    "jumpsuit": "ClothingUniformBase", "outerClothing": "ClothingOuterBase", "head": "ClothingHeadBase",
    "mask": "ClothingMaskBase", "shoes": "ClothingShoesBase", "gloves": "ClothingHandsBase",
    "eyes": "ClothingEyesBase", "neck": "ClothingNeckBase", "ears": "Clothing",
}

# Outer clothing with pockets.
POCKETED = re.compile(r"jacket|coat|hoodie|labcoat|/toggle/|vest|duster|parka|blazer|cardigan|poncho")

# Typepath prefix -> the Wolfgate item whose job it does, first match wins. None leaves the type out.
FUNCTION = [
    ("/obj/item/clothing/under/plasmaman", None),
    ("/obj/item/clothing/head/helmet/space/plasmaman", None),
    ("/obj/item/clothing/suit/space/hardsuit", None),
    ("/obj/item/clothing/head/helmet/space/hardsuit", None),
    ("/obj/item/clothing/shoes/magboots", None),
    ("/obj/item/clothing/glasses/thermal", None),
    ("/obj/item/clothing/glasses/night", None),
    ("/obj/item/clothing/suit/space", "ClothingOuterHardsuitEVA"),
    ("/obj/item/clothing/head/helmet/space", "ClothingHeadHelmetEVA"),
    ("/obj/item/clothing/suit/armor/riot", "ClothingOuterArmorRiot"),
    ("/obj/item/clothing/suit/armor/bulletproof", "ClothingOuterArmorBulletproof"),
    ("/obj/item/clothing/suit/armor/laserproof", "ClothingOuterArmorReflective"),
    ("/obj/item/clothing/suit/armor", "ClothingOuterArmorBasic"),
    ("/obj/item/clothing/head/helmet/riot", "ClothingHeadHelmetRiot"),
    ("/obj/item/clothing/head/helmet", "ClothingHeadHelmetBasic"),
    ("/obj/item/clothing/mask/gas", "ClothingMaskGas"),
    ("/obj/item/clothing/mask/breath", "ClothingMaskBreath"),
    ("/obj/item/clothing/glasses/hud/security", "ClothingEyesHudSecurity"),
    ("/obj/item/clothing/glasses/hud/health", "ClothingEyesHudMedical"),
    ("/obj/item/clothing/glasses/hud/diagnostic", "ClothingEyesHudDiagnostic"),
    ("/obj/item/clothing/glasses/meson", "ClothingEyesGlassesMeson"),
    ("/obj/item/clothing/gloves/color/yellow", "ClothingHandsGlovesColorYellow"),
    ("/obj/item/clothing/gloves/combat", "ClothingHandsGlovesCombat"),
    ("/obj/item/clothing/suit/bio_suit", "ClothingOuterBioGeneral"),
    ("/obj/item/clothing/head/bio_hood", "ClothingHeadHatHoodBioGeneral"),
    ("/obj/item/clothing/suit/utility/radiation", "ClothingOuterSuitRad"),
    ("/obj/item/clothing/head/utility/radiation", "ClothingHeadHatHoodRad"),
]
# Parents that only keep warm: their items are distributed like clothing.
MILD = {"ClothingOuterWinterCoat"}
WARM = [("/obj/item/clothing/suit/hooded/wintercoat", "ClothingOuterWinterCoat")]

# Typepath words that leave a type out wherever it sits.
LEFT_OUT = re.compile(r"/chameleon|/mod\b|/debug|/shielded|/space_ninja|/abductor|/changeling|/infiltrator")
# Source files whose types are left out: NovaSector's donator items belong to the players they were made for.
LEFT_OUT_FILES = re.compile(r"donator")
# Source files whose types are adult items, whatever their typepath says.
ADULT_FILES = re.compile(r"lewd")

# HUD glasses by name word -> the Wolfgate HUD with that job; other HUDs are clothing only.
HUDS = [("security", "ClothingEyesHudSecurity"), ("sec", "ClothingEyesHudSecurity"),
        ("medical", "ClothingEyesHudMedical"), ("health", "ClothingEyesHudMedical"),
        ("diagnostic", "ClothingEyesHudDiagnostic"), ("meson", "ClothingEyesGlassesMeson")]

# Theme by typepath (and, for the plainer paths, name) word; the first that matches wins.
THEMES = [
    ("adult", r"lewd|shibari|ball_?gag|bdsm|kink|latex_catsuit|erp_|strapon|/sextoy|/leash|lewd_|chastity"),
    ("admin", r"/admin|centcom_formal|/debug|/ert|deathsquad|/centcom"),
    ("pirate", r"/spacepirate|/pirate"),
    ("antag", r"/cult|/heretic|/wizard|wizrobe|/ninja|/syndicate|/syndi|/nukie|/clockwork"),
    ("command", r"/captain|/caphat|/hop\b|/hopcap|head_of_personnel|nanotrasen_consultant|/blueshield|/bridge|"
                r"/heads_of|/head_of|/ntrep|/representative|/command"),
    ("detective", r"/det\b|detective|/noir|/det_"),
    ("security", r"/security|/sec\b|/sec_|/warden|/hos\b|/hos_|/brig|/police|/officer|security_cap|/peacekeeper"),
    ("chemistry", r"/chemist"),
    ("virology", r"/virolog"),
    ("genetics", r"/genetic"),
    ("medical", r"/medical|/doctor|/paramedic|/cmo\b|/surgeon|/surgical|/nurse|/coroner|/psych|/orderly|/labcoat"),
    ("atmos", r"/atmos"),
    ("engineering", r"/engineering|/engineer|/chief_engineer|/ce\b|/utility/hardhat|/hardhat|/construction"),
    ("robotics", r"/robotic"),
    ("science", r"/science|/rnd|/research|/scientist|/rd\b|/xenobio|/toxins"),
    ("mail", r"/mail"),
    ("cargo", r"/cargo|/supply|/quartermaster|/qm\b|/miner|/mining|/explorer|/shaft|/bitrunner|/salvage"),
    ("bar", r"/bartender|/bar\b|/waiter|/butler"),
    ("chef", r"/chef|/cook"),
    ("botany", r"/botan|/hydro"),
    ("janitor", r"/janitor|/maid"),
    ("chaplain", r"/chaplain|/nun\b|/priest|/bishop|/monk"),
    ("lawyer", r"/lawyer|/attorney"),
    ("curator", r"/curator|/librarian"),
    ("clown", r"/clown|/mime"),
    ("prisoner", r"/prisoner|/prison"),
    ("military", r"/solfed|/military|/imperial|/tarkon|/trek|/colonial|/marine|/nri|/akula|/kaza_ruk|/hc\b|"
                 r"/tactical|/combat|/fedcoat|/padded"),
    ("winter", r"wintercoat|/winter"),
    ("costume", r"/costume|/cosplay|/chicken|/animal|/mascot|/dress/costume|/halloween|/collectable|/playbunny"
                r"|/bunny|/pelt|/cowboy"),
]

# Theme -> vending inventories that stock it.
VENDORS = {
    "civilian": ["ClothesMateInventory"],
    "winter": ["WinterDrobeInventory"],
    "costume": ["AutoDrobeInventory"],
    "clown": ["AutoDrobeInventory"],
    "security": ["SecDrobeInventory"],
    "detective": ["DetDrobeInventory"],
    "medical": ["MediDrobeInventory"],
    "chemistry": ["ChemDrobeInventory"],
    "virology": ["ViroDrobeInventory"],
    "genetics": ["GeneDrobeInventory"],
    "engineering": ["EngiDrobeInventory"],
    "atmos": ["AtmosDrobeInventory"],
    "science": ["SciDrobeInventory"],
    "robotics": ["RoboDrobeInventory"],
    "cargo": ["CargoDrobeInventory"],
    "mail": ["MailDrobeInventory"],
    "bar": ["BarDrobeInventory"],
    "chef": ["ChefDrobeInventory"],
    "botany": ["HyDrobeInventory"],
    "janitor": ["JaniDrobeInventory"],
    "chaplain": ["PietyVendInventory"],
    "pirate": ["YarrrDrobeInventory"],
    "lawyer": ["LawDrobeInventory"],
    "curator": ["CuraDrobeInventory"],
}
STOCK = 2

# Groups that hold survival gear, breathing gear, HUDs or headsets rather than a choice of clothes. Headsets are
# also why ear items go in no loadout: one would take the radio's place.
GROUP_SKIP = re.compile(r"Species|Breath|Tank|Survival|Hud|Ears|PDA")
LOADOUT_SLOTS = {"jumpsuit", "outerClothing", "head", "mask", "shoes", "gloves", "eyes", "neck"}

CIVILIAN = ["civilian", "winter"]
# Role -> themes its loadout groups take. Roles not listed (faction uniforms, borgs) get nothing.
ROLES = {
    "JobContractor": CIVILIAN, "JobPilot": CIVILIAN, "JobPdvCivilian": CIVILIAN, "JobTsfCivilian": CIVILIAN,
    "JobFugitive": CIVILIAN, "JobGodfather": CIVILIAN, "JobPublicAffairsLiaison": CIVILIAN,
    "JobMercenary": CIVILIAN + ["military"],
    "JobCCServiceWorker": ["bar", "chef"],
    "JobNFJanitor": ["janitor"],
    "JobMailCarrier": ["mail", "cargo"],
    "JobCrocusDockworker": ["cargo"],
    "JobSecurityGuard": ["security"],
    "JobDirectorOfCare": ["medical"], "JobMdMedic": ["medical"],
    "JobNFDetective": ["detective"],
    "JobPrisoner": ["prisoner"],
    # Station jobs from upstream.
    "JobCaptain": ["command"], "JobHeadOfPersonnel": ["command"], "JobPassenger": CIVILIAN,
    "JobBartender": ["bar"], "JobServiceWorker": ["bar", "chef"], "JobChef": ["chef"], "JobLibrarian": ["curator"],
    "JobLawyer": ["lawyer"], "JobChaplain": ["chaplain"], "JobJanitor": ["janitor"], "JobBotanist": ["botany"],
    "JobClown": ["clown"], "JobMime": ["clown"], "JobMusician": CIVILIAN, "JobQuartermaster": ["cargo"],
    "JobCargoTechnician": ["cargo"], "JobSalvageSpecialist": ["cargo"], "JobChiefEngineer": ["engineering"],
    "JobTechnicalAssistant": ["engineering"], "JobStationEngineer": ["engineering"],
    "JobAtmosphericTechnician": ["atmos", "engineering"], "JobResearchDirector": ["science"],
    "JobScientist": ["science", "robotics"], "JobResearchAssistant": ["science"], "JobHeadOfSecurity": ["security"],
    "JobWarden": ["security"], "JobSecurityOfficer": ["security"], "JobDetective": ["detective"],
    "JobSecurityCadet": ["security"], "JobChiefMedicalOfficer": ["medical"], "JobMedicalDoctor": ["medical"],
    "JobMedicalIntern": ["medical"], "JobChemist": ["chemistry", "medical"], "JobParamedic": ["medical"],
    "JobReporter": CIVILIAN, "JobPsychologist": ["medical"], "JobBoxer": CIVILIAN, "JobZookeeper": CIVILIAN,
}

# SS13 flags_inv -> (humanoid layer, slot that hides it) for HideLayerClothing.
HIDES = [
    ("HIDEHAIR", "Hair"), ("HIDEFACIALHAIR", "FacialHair"), ("HIDESNOUT", "Snout"), ("HIDETAIL", "Tail"),
]
SLOT_FLAG = {"head": "HEAD", "mask": "MASK", "outerClothing": "OUTERCLOTHING", "neck": "NECK", "eyes": "EYES"}

# SS13 armour value (percent) at and above which an item counts as protective, and the most it may block here.
ARMOR_PROTECTIVE = 20
ARMOR_CAP = 70
# HUD traits on glasses -> the Wolfgate HUD with that job.
HUD_TRAITS = [("TRAIT_SECURITY_HUD", "ClothingEyesHudSecurity"), ("TRAIT_MEDICAL_HUD", "ClothingEyesHudMedical"),
              ("TRAIT_DIAGNOSTIC_HUD", "ClothingEyesHudDiagnostic"), ("TRAIT_MESON_VISION", "ClothingEyesGlassesMeson")]
FIRE_PROOF = 30000          # FIRE_SUIT_MAX_TEMP_PROTECT
SPACE_COLD = 72             # SPACE_SUIT_MIN_TEMP_PROTECT_OFF and below: rated for deep cold


class Mechanics:
    """What an item's SS13 stats give it here: component lines, a parent with that job, and whether it protects."""

    def __init__(self):
        self.lines = []
        self.parent = None
        self.protective = False

    def component(self, *lines):
        self.lines += list(lines)


def mechanics(src, t, item, parent):
    m = Mechanics()
    d = src.datums
    raw = lambda var: d.raw(t, var) or ""
    num = lambda var: d.value(t, var) if isinstance(d.value(t, var), (int, float)) else None
    slot = item.slot

    # Armour: melee is blunt and slash, bullets pierce, lasers and energy burn.
    armor = d.value(t, "armor_type")
    if isinstance(armor, str) and armor.startswith("/datum/armor"):
        val = lambda k: d.value(armor, k) if isinstance(d.value(armor, k), (int, float)) else 0
        melee, bullet, burn = val("melee"), val("bullet"), max(val("laser"), val("energy"))
        if max(melee, bullet, burn) >= 10:
            coef = lambda x: round(1 - min(max(x, 0), ARMOR_CAP) / 100, 2)
            m.component("  - type: Armor", "    modifiers:", "      coefficients:",
                        "        Blunt: %s" % coef(melee), "        Slash: %s" % coef(melee),
                        "        Piercing: %s" % coef(bullet), "        Heat: %s" % coef(burn))
            m.protective |= max(melee, bullet, burn) >= ARMOR_PROTECTIVE

    flags = raw("clothing_flags")
    traits = raw("clothing_traits")

    if slot == "gloves" and num("siemens_coefficient") == 0:
        m.component("  - type: Insulated")
        m.protective = True

    if slot == "mask" and "MASKINTERNALS" in flags:
        m.component("  - type: BreathMask")
        if "BLOCK_GAS_SMOKE_EFFECT" in flags:
            m.component("  - type: IngestionBlocker")
        m.protective = True

    sealed = slot in ("outerClothing", "head") and "STOPSPRESSUREDAMAGE" in flags
    if sealed:
        m.component("  - type: PressureProtection", "    highPressureMultiplier: 0.6",
                    "    lowPressureMultiplier: 1000")
        m.protective = True

    # SS13 protects each limb on its own; here every worn item's coefficient multiplies the rest, so only the
    # suit and, more weakly, the helmet carry it, as the EVA gear here does.
    if slot in ("outerClothing", "head"):
        hot, cold = num("max_heat_protection_temperature"), num("min_cold_protection_temperature")
        fire = hot is not None and hot >= FIRE_PROOF
        # Sealed suits are EVA gear; unsealed ones rated for deep cold (winter coats, fire suits) keep warm like the
        # coats here; the mild ratings SS13 gives every helmet and vest are left out.
        warm = cold is not None and cold <= SPACE_COLD
        if slot == "outerClothing":
            heating = 0.005 if fire else 0.01 if sealed else 1
            cooling = 0.01 if sealed else 0.1 if warm else 1
        else:
            heating = 0.2 if fire or sealed else 1
            cooling = 0.2 if sealed else 1
        if (heating, cooling) != (1, 1):
            m.component("  - type: TemperatureProtection", "    heatingCoefficient: %s" % heating,
                        "    coolingCoefficient: %s" % cooling)
        if fire:
            m.component("  - type: FireProtection", "    reduction: %s" % (0.65 if slot == "outerClothing" else 0.2))
            m.protective = True

    flash = d.value(t, "flash_protect")
    if slot in ("eyes", "head", "mask") and isinstance(flash, int) and flash >= 1:
        m.component("  - type: FlashImmunity", "  - type: EyeProtection")

    if slot == "eyes" and parent == PARENTS["eyes"]:
        hud = next((p for trait, p in HUD_TRAITS if trait in traits), None)
        if hud:
            m.parent = hud
            m.protective = True

    if slot == "shoes" and re.search(r"TRAIT_NO_SLIP_(WATER|ALL)", traits):
        m.component("  - type: NoSlip")
        m.protective = True

    slowdown = num("slowdown")
    if slowdown and slowdown > 0:
        modifier = round(max(0.5, 1 - 0.2 * slowdown), 2)
        m.component("  - type: ClothingSpeedModifier", "    walkModifier: %s" % modifier,
                    "    sprintModifier: %s" % modifier)
    return m


def camel(text):
    return "".join(p[:1].upper() + p[1:] for p in re.split(r"[^A-Za-z0-9]+", text) if p)


def ftl_text(text):
    """A Fluent value that is read literally."""
    text = text.replace("{", "{\"{\"}").replace("}", "{\"}\"}") if "{" in text or "}" in text else text
    if text[:1] in "[*.":
        text = '{"%s"}' % text[0] + text[1:]
    return text


def _git(root, *args):
    try:
        return subprocess.run(["git", "-C", root] + list(args), capture_output=True, text=True, check=True).stdout.strip()
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def _write_text(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


class Planned:
    def __init__(self, item, pid, rsi, parent, theme, protective):
        self.item = item
        self.id = pid
        self.rsi = rsi              # path under the module's texture folder, without .rsi
        self.parent = parent
        self.theme = theme
        self.protective = protective
        self.states = {}            # set name -> [(state, colour slot or None)]
        self.size = (32, 32)
        self.mechanics = []         # component lines from the item's SS13 stats

    @property
    def loadout(self):
        return ID_PREFIX + "Loadout" + self.id[len(ID_PREFIX):]

    @property
    def distributed(self):
        return not self.protective and self.theme not in ("adult", "admin", "antag")


class Port:
    def __init__(self, source_root, index_cache=None):
        self.source_root = source_root
        self.src = source.Source(source_root)
        self.repo = repo.Repo()
        self.index = dedupe.Index(repo.TEXTURES, index_cache)
        self.planned = []
        self.outcomes = {}  # typepath -> (verdict, detail)

    # --------------------------------------------------------------------------- planning

    def run(self):
        names_here = self.repo.entity_names()
        seen = {}
        taken = set(self.repo.entities)
        for t in self.src.types():
            if self.src.is_abstract(t):
                continue
            slot = self.src.slot_of(t)
            if slot is None:
                continue
            if LEFT_OUT.search(t) or LEFT_OUT_FILES.search(self.src.files.get(t, "")):
                self.outcomes[t] = ("left out", "kind")
                continue
            parent, protective = self._parent(t, slot)
            if parent is False:
                self.outcomes[t] = ("left out", "kind")
                continue
            item = self.src.read(t)
            if parent == PARENTS.get(slot) and slot == "eyes" and "/glasses/hud" in t:
                parent, protective = next(((p, True) for word, p in HUDS if word in (item.name or "").lower()),
                                          (parent, False))
            # A name the type only inherits from the slot's base type ("under", "suit") marks a base type.
            if not item.name or item.name == source.clean_text(self.src.value("/".join(t.split("/")[:5]), "name")):
                self.outcomes[t] = ("left out", "no name")
                continue
            if not item.icon or not item.worn:
                self.outcomes[t] = ("left out", "no icon" if not item.icon else "no worn sprite")
                continue
            sig = dedupe.signature(item)
            if sig in seen:
                self.outcomes[t] = ("same as", seen[sig])
                continue
            seen[sig] = t
            here = self.index.match(dedupe.south(item.worn, item.colors))
            if item.gags:
                # Recolouring is what the item adds, so it stays, and says so when its name is taken here.
                if here or item.name.lower() in names_here:
                    item.name = "recolourable " + item.name
            elif here:
                self.outcomes[t] = ("already here", here)
                continue
            elif item.name.lower() in names_here:
                self.outcomes[t] = ("already here", "name: " + item.name)
                continue
            tail = t[len("/obj/item/clothing/"):]
            pid = ID_PREFIX + "Clothing" + camel(tail)
            while pid in taken:
                pid += "Alt"
            taken.add(pid)
            rsi = "%s/%s" % (FOLDERS[slot], re.sub(r"[^a-z0-9]+", "_", tail.split("/", 1)[-1].lower()).strip("_"))
            theme = "adult" if ADULT_FILES.search(self.src.files.get(t, "")) else self._theme(t, item)
            stats = mechanics(self.src, t, item, parent)
            planned = Planned(item, pid, rsi, stats.parent or parent, theme, protective or stats.protective)
            planned.mechanics = stats.lines
            self._plan_states(planned)
            self.planned.append(planned)
            self.outcomes[t] = ("ported", pid)
        rsis = set()
        for p in self.planned:
            base = p.rsi
            n = 2
            while p.rsi in rsis:
                p.rsi = "%s_%d" % (base, n)
                n += 1
            rsis.add(p.rsi)

    def _parent(self, t, slot):
        """(parent, protective) for a type, or (False, _) when its kind is left out."""
        for prefix, parent in FUNCTION:
            if t == prefix or t.startswith(prefix + "/"):
                if parent is None:
                    return False, False
                return parent, True
        for prefix, parent in WARM:
            if t == prefix or t.startswith(prefix + "/"):
                return parent, False
        parent = PARENTS[slot]
        if slot == "outerClothing" and POCKETED.search(t):
            parent = "ClothingOuterStorageBase"
        return parent, False

    @staticmethod
    def _theme(t, item):
        # The slot segment is left off, or every hat would read as a head of staff.
        subject = "/" + "/".join(t.split("/")[5:])
        for theme, pattern in THEMES:
            if re.search(pattern, subject):
                return theme
        return "civilian"

    def _plan_states(self, p):
        item = p.item
        equipped = source.EQUIPPED[item.slot]
        sets = [("icon", item.icon), (equipped, item.worn),
                ("inhand-left", item.inhand_left), ("inhand-right", item.inhand_right)]
        width = height = 32
        for _, layers in sets:
            for layer in layers:
                w, h = layer.frames[0][0].size
                width, height = max(width, w), max(height, h)
        p.size = (width, height)
        for base, layers in sets:
            if not layers:
                continue
            if not item.gags:
                p.states[base] = [(base, None)]
                continue
            names = []
            for i, layer in enumerate(layers):
                name = "%s-%d" % (base, i + 1)
                if layer.color:
                    name += "-c%d" % layer.color
                names.append((name, layer.color))
            p.states[base] = names

    # ---------------------------------------------------------------------------- writing

    def write(self):
        for folder in (OUT_TEXTURES, OUT_PROTOTYPES, OUT_LOCALE):
            if os.path.isdir(folder):
                shutil.rmtree(folder)
            os.makedirs(folder)
        commit = _git(self.source_root, "rev-parse", "HEAD")
        by_folder = {}
        for p in self.planned:
            self._write_rsi(p, commit)
            by_folder.setdefault(FOLDERS[p.item.slot], []).append(p)
        for folder, group in sorted(by_folder.items()):
            group.sort(key=lambda p: p.id)
            _write_text(os.path.join(OUT_PROTOTYPES, "Entities", folder + ".yml"),
                        "\n".join(self._prototype(p) for p in group))
            _write_text(os.path.join(OUT_LOCALE, folder.lower() + ".ftl"),
                        "".join(self._names(p) for p in group))
        loadouts = [p for p in self.planned if p.distributed]
        _write_text(os.path.join(OUT_PROTOTYPES, "loadouts.yml"),
                    "\n".join(self._loadout(p) for p in sorted(loadouts, key=lambda p: p.id)))
        self._write_groups(loadouts)
        self._write_vendors(loadouts)

    def _write_rsi(self, p, commit):
        folder = os.path.join(OUT_TEXTURES, p.rsi + ".rsi")
        os.makedirs(folder, exist_ok=True)
        item = p.item
        size = p.size
        states = []
        sets = {"icon": item.icon, source.EQUIPPED[item.slot]: item.worn,
                "inhand-left": item.inhand_left, "inhand-right": item.inhand_right}
        for base, names in p.states.items():
            layers = sets[base]
            if not item.gags:
                layers = [source.composite(layers, [])]
            for (name, _), layer in zip(names, layers):
                dirs = 4 if base != "icon" or layer.dirs >= 4 else 1
                frames = source.fit(layer.frames, dirs, len(layer.frames))
                count = len(frames)
                columns = max(1, math.ceil(math.sqrt(dirs * count)))
                rows = math.ceil(dirs * count / columns)
                sheet = Image.new("RGBA", (columns * size[0], rows * size[1]), (0, 0, 0, 0))
                for d in range(dirs):
                    for f in range(count):
                        index = d * count + f
                        cell = frames[f][d]
                        x = (index % columns) * size[0] + (size[0] - cell.size[0]) // 2
                        y = (index // columns) * size[1] + (size[1] - cell.size[1]) // 2
                        sheet.alpha_composite(cell, (x, y))
                sheet.save(os.path.join(folder, name + ".png"), optimize=True)
                state = {"name": name}
                if dirs != 1:
                    state["directions"] = dirs
                if count > 1:
                    delays = [round(d, 3) for d in (layer.delays or [0.1] * count)][:count]
                    delays += [0.1] * (count - len(delays))
                    state["delays"] = [delays] * dirs
                states.append(state)
        states.sort(key=lambda s: s["name"])
        meta = {
            "version": 1,
            "license": "CC-BY-SA-3.0",
            "copyright": "Taken from Meridian Rift at %s/tree/%s (%s, %s), which carries the art of NovaSector, "
                         "Skyrat-tg and tgstation. Converted for Wolfgate by Tools/_WF/%s/port.py."
                         % (SOURCE_URL, commit, item.typepath, item.source, MODULE),
            "size": {"x": size[0], "y": size[1]},
            "states": states,
        }
        _write_text(os.path.join(folder, "meta.json"), json.dumps(meta, indent=2) + "\n")

    def _layers_yaml(self, p, base, indent):
        lines = []
        for name, color in p.states[base]:
            lines.append("%s- state: %s" % (indent, name))
            if color:
                lines.append('%s  color: "%s"' % (indent, p.item.colors[color - 1]))
        return lines

    def _prototype(self, p):
        item = p.item
        sprite = "%s/%s.rsi" % (TEXTURE_ROOT, p.rsi)
        equipped = source.EQUIPPED[item.slot]
        lines = [
            "- type: entity",
            "  parent: %s" % p.parent,
            "  id: %s" % p.id,
            "  components:",
            "  - type: Sprite",
            "    sprite: %s" % sprite,
        ]
        if item.gags:
            lines.append("    layers:")
            lines += self._layers_yaml(p, "icon", "    ")
        else:
            lines.append("    state: icon")
        lines += ["  - type: Clothing", "    sprite: %s" % sprite]
        if item.slot == "ears":
            # Headsets carry a radio, so ear wear has the plain clothing base and names its slot itself.
            lines.append("    slots: [ears]")
        if item.gags:
            lines += ["    clothingVisuals:", "      %s:" % item.slot]
            lines += self._layers_yaml(p, equipped, "      ")
        if item.gags and ("inhand-left" in p.states or "inhand-right" in p.states):
            lines += ["  - type: Item", "    inhandVisuals:"]
            for side in ("left", "right"):
                if "inhand-" + side in p.states:
                    lines.append("      %s:" % side)
                    lines += self._layers_yaml(p, "inhand-" + side, "      ")
        hides = [(layer, SLOT_FLAG[item.slot]) for flag, layer in HIDES
                 if item.flags_inv & source.FLAGS_INV[flag] and item.slot in SLOT_FLAG]
        if hides:
            lines += ["  - type: HideLayerClothing", "    layers:"]
            lines += ["      %s: %s" % h for h in hides]
        if item.flags_inv & source.FLAGS_INV["HIDEFACE"] and item.slot in ("head", "mask"):
            lines.append("  - type: IdentityBlocker")
        lines += p.mechanics
        if item.gags:
            lines += ["  - type: WFGags", "    colors:"]
            lines += ['    - "%s"' % c for c in item.colors]
        return "\n".join(lines) + "\n"

    @staticmethod
    def _names(p):
        lines = ["ent-%s = %s" % (p.id, ftl_text(p.item.name))]
        if p.item.desc:
            lines.append("    .desc = %s" % ftl_text(p.item.desc))
        else:
            lines.append("    .desc = { \"\" }")
        return "\n".join(lines) + "\n"

    @staticmethod
    def _loadout(p):
        return "\n".join([
            "- type: loadout",
            "  id: %s" % p.loadout,
            "  price: 0",
            "  equipment:",
            "    %s: %s" % (p.item.slot, p.id),
        ]) + "\n"

    def _write_groups(self, loadouts):
        by_slot_theme = {}
        for p in loadouts:
            by_slot_theme.setdefault((p.item.slot, p.theme), []).append(p.loadout)
        wanted = {}
        for role, groups in self.repo.roles.items():
            themes = ROLES.get(role)
            if not themes:
                continue
            for group in groups:
                wanted.setdefault(group, set()).update(themes)
        self.group_counts = {}
        for group, (path, _) in sorted(self.repo.groups.items()):
            entries = []
            themes = wanted.get(group)
            slot = self.repo.group_slot(group) if themes and not GROUP_SKIP.search(group) else None
            slot = slot if slot in LOADOUT_SLOTS else None
            if slot:
                for theme in sorted(themes):
                    entries += by_slot_theme.get((slot, theme), [])
            entries = sorted(set(entries))
            if entries:
                self.group_counts[group] = len(entries)
            # Called for every group so a block from an earlier run that no longer applies goes away.
            if entries or self._has_block(path):
                repo.write_group(path, group, entries)

    def _write_vendors(self, loadouts):
        stock = {}
        for p in loadouts:
            for inventory in VENDORS.get(p.theme, []):
                stock.setdefault(inventory, []).append(p.id)
        self.vendor_counts = {}
        for inventory, path in sorted(self.repo.inventories.items()):
            items = sorted(stock.get(inventory, []))
            if items:
                self.vendor_counts[inventory] = len(items)
            if items or self._has_block(path):
                repo.write_inventory(path, inventory, [(i, STOCK) for i in items])

    @staticmethod
    def _has_block(path):
        with open(path, encoding="utf-8", errors="replace") as f:
            return "# WOLFGATE(%s) START" % MODULE in f.read()

    # --------------------------------------------------------------------------- reporting

    def summary(self):
        verdicts = {}
        for verdict, _ in self.outcomes.values():
            verdicts[verdict] = verdicts.get(verdict, 0) + 1
        lines = ["%d types: %s" % (len(self.outcomes), ", ".join("%d %s" % (n, v) for v, n in sorted(verdicts.items())))]
        slots = {}
        themes = {}
        for p in self.planned:
            slots[p.item.slot] = slots.get(p.item.slot, 0) + 1
            themes[p.theme] = themes.get(p.theme, 0) + 1
        lines.append("ported by slot: " + ", ".join("%s %d" % kv for kv in sorted(slots.items())))
        lines.append("ported by theme: " + ", ".join("%s %d" % kv for kv in sorted(themes.items())))
        lines.append("greyscale: %d, protective: %d, baked blends: %d" % (
            sum(1 for p in self.planned if p.item.gags), sum(1 for p in self.planned if p.protective),
            self.src.gags.baked))
        return "\n".join(lines)

    def report(self):
        return {t: {"verdict": v, "detail": d} for t, (v, d) in sorted(self.outcomes.items())}


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    parser.add_argument("--source", required=True, help="a Meridian Rift checkout")
    parser.add_argument("--write", action="store_true", help="write the RSIs, prototypes, names and stock")
    parser.add_argument("--report", metavar="FILE", help="write what became of every source type, as JSON")
    parser.add_argument("--index-cache", metavar="FILE", help="keep the index of worn states here between runs")
    args = parser.parse_args()

    if not os.path.isfile(os.path.join(args.source, "tgstation.dme")):
        raise SystemExit("%s is not an SS13 checkout (no tgstation.dme)" % args.source)
    port = Port(os.path.abspath(args.source), args.index_cache)
    port.run()
    print(port.summary())
    if args.report:
        _write_text(args.report, json.dumps(port.report(), indent=1))
    if args.write:
        port.write()
        print("groups: %d, vendors: %s" % (len(port.group_counts), port.vendor_counts))
        print("written; now run python Tools/_WF/Ci/modules.py --write")


if __name__ == "__main__":
    main()
