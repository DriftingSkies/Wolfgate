"""Ports the brewing sprites (roasted coffee beans, dried tea leaves) from Paragon Moon into brewing.rsi.

Usage: python Tools/_WG/Cooking/port_paragon_brewing.py [path/to/Paragon-Moon]
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from port_paragon_kitchen import FOOD, SOURCE, TEXTURES, sheet, write_rsi  # noqa: E402

# Paragon state -> our state.
STATES = {
    "coffeebeans_roasted": "coffee_roasted",
    "teadry": "tea_dried",
}


def main():
    drinks = sheet(SOURCE / FOOD / "drinks.dmi")
    write_rsi(TEXTURES / "brewing.rsi", {ours: drinks[theirs] for theirs, ours in STATES.items()}, f"{FOOD}/drinks.dmi")
    print(f"{len(STATES)} brewing sprites")


if __name__ == "__main__":
    main()
