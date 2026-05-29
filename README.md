# WurmShare

**A Party Interface for Wurm Online** — a *WurmMaps.XYZ* project.

WurmShare is a lightweight desktop overlay that reads your character's vital
stats and broadcasts them to the rest of your party in real time. Wurm Online
has no built-in party interface, so when you group up there's no easy way to see
how your companions are holding up. WurmShare fills that gap: it shows your
entire team's health, sustenance, and combat state at a glance, in a compact
panel that mirrors the in-game layout you already know — so no comrade falls
unseen.

- **Free and open source**, forever.
- **End-to-end encrypted** — your data is sealed with a password the server never sees.
- **Whisper-light** — under ~30 MB of RAM with minimal-to-no performance cost.
- **Plug and play** — set up in under a minute, no game files modified.

> Current version: **v1.0.0** · Platforms: **Windows 7 / 10 / 11 (64-bit)**

---

## Table of Contents

- [What It Tracks](#what-it-tracks)
- [How It Works](#how-it-works)
- [Getting Started](#getting-started)
- [Features](#features)
- [Self-Hosting](#self-hosting)
- [Privacy & Security](#privacy--security)
- [Disclaimer — A Word of Caution](#disclaimer--a-word-of-caution)
- [About](#about)

---

## What It Tracks

WurmShare reads your game logs and small parts of the screen to track the state
of your character, then shares that state with your party. The following are
displayed live for every connected member:

- **Health / Damage** | Live health to the last percent. Members who are currently or recently in combat, stunned, or encumbered are highlighted. |
- **Stamina** | The breath behind every sprint, dodge, and parry — know who has the wind to hold the line and who may straggle behind. |
- **Food** | The slow burn of every buffed meal, with forewarning when a special move depletes an ally's food and leaves them vulnerable. |
- **Water** | Thirst is the number-one killer of Wurmians. Monitor your whole team's hydration at a glance. |
- **Favor** | Track the favor burn of your priests in battle so you can time and coordinate spell casts and favor regeneration. |
- **Shield Bash Cooldown** | Time your bash sequences accurately so the enemy never gets a gap to recover. |
- **Aim / Shelter Direction** | See where party members are aiming or taking shelter. |
- **Fighting Stance** | Know each member's current stance at a glance. |
- **In Combat** | Characters who are recently in combat are highlighted Red. |
- **Encumbered** | Characters who are overencumbered are highlighted Green. |

The overlay reproduces the same status layout you're already familiar with from
the game — just compacted and miniaturized so the whole party fits neatly on
screen.

---

## How It Works

WurmShare never modifies the game. It observes only what your own client already
produces, turns it into a handful of small values, encrypts them, and relays
them to your party. A single update travels through five stages:

1. **Observe** — WurmShare tails the log files the Wurm client writes to disk
   (for discrete events such as combat, stuns, shield bashes, spell casts, and
   stance changes) and samples a small region of *your own screen* that you
   position over the in-game status bars (for the continuous Health, Stamina,
   Food, Water, and Favor bars).
2. **Interpret** — Log lines are matched into structured, timestamped events,
   and the captured bar regions are reduced to simple fill percentages. The
   result is a tidy snapshot describing one character at one moment.
3. **Seal** — That snapshot is encrypted locally using a key derived from the
   **group password** you share with your party. The password itself is never
   transmitted or stored on the server.
4. **Relay** — The encrypted data is sent to a communication server, which
   simply forwards it to the other members of your group. The server only ever
   handles ciphertext.
5. **Render** — Each party member's WurmShare decrypts the update and draws it
   into the shared overlay — draggable, resizable, and arranged into the rows
   and columns you choose.

The full round trip is fast enough that the panel feels live, and the whole
pipeline runs quietly in the background. Close WurmShare and it stops reading and
broadcasting immediately.

---

## Getting Started

Setup takes under a minute. There are no accounts to create and no servers of
your own to run.

1. **Download WurmShare.** Drop it anywhere and run it. Point it to your Wurm
   install directory, then enter your character name and your group's password.
2. **Adjust the capture box.** Drag the capture region over your in-game health
   and sustenance bars, positioning it as accurately as you can.
3. **Join your party.** Click **Join** to begin broadcasting your vitals to the
   rest of your team. Everyone using the same password sees one another live.

That's it — anyone in your group who is running WurmShare and connected will
appear on the overlay.

---

## Features

- **Multi-Monitor Support** — full support for multi-monitor setups, with
  variable scaling and DPI settings.
- **Persistent Memory** — settings are saved when you close the settings panel,
  and window and capture-box positions are remembered for next start-up.
- **Configurable Display** — choose how many rows and columns are shown, drag the
  overlay anywhere, and resize it freely.
- **Automatic Update Detection** — checks GitHub for new releases and lets you
  know when an update is available.
- **End-to-End Encrypted** — all communication is encrypted with the password you
  choose, which is never shared with the server.
- **Whisper-Light** — under ~30 MB of RAM at rest on a single idle thread, for
  minimal-to-no performance cost.
- **Fully Open Source** — the complete source is available, including the
  communication server, so you can host your own. No trust required.

---

## Self-Hosting

Because WurmShare is fully open source and all traffic through the relay is
encrypted end-to-end, you don't have to rely on the public communication server.
You can run your own instance instead — the server is intentionally simple, and
since it only ever sees ciphertext, no trust in the operator is required either
way.

Hosting your own private communication server can be done on almost any basic
web host. The only requirements are PHP and MySQL support.

To host the communication server, simply upload the files from /server to the
desired path on your web server. Enter the new path into WurmShare, and you're
finished.

---

## Privacy & Security

- Updates are encrypted on your machine with a key derived from your **group
  password**; that password is never sent to or stored on the server.
- The communication server only ever forwards encrypted data — it cannot read
  any value your party broadcasts.
- Only people who know the same password can decrypt your data. Choose a unique
  password, share it only with members you trust, and change it if someone should
  no longer have access.

---

## Disclaimer — A Word of Caution

> **WurmShare is a passive, read-only companion. It is built to respect the game,
> but you are responsible for how you use it. Please read this before using it.**
>
> - **It never modifies the game.** WurmShare does not read or write the Wurm
>   client's memory, does not inject code, does not send keystrokes or mouse
>   input, and does not alter any game file. It only observes log files the client
>   itself produces and a region of your own screen, and it only ever *displays*
>   information that is already visible to you.
> - **Rules and Terms of Service are your responsibility.** What counts as a
>   permitted third-party tool or overlay can change at the discretion of the
>   developers and staff of Wurm Online. WurmShare does not automate play or make
>   decisions for you, but WurmMaps.XYZ cannot and does not guarantee that its use
>   is permitted under the current rules. If you are unsure, ask the game's staff
>   before using it, and stop immediately if asked to. You alone accept any risk
>   to your account.
> - **Readings are estimates, not gospel.** Values derived from screen capture can
>   occasionally misread due to UI scaling, custom themes, overlapping windows,
>   particle effects, or unusual lighting. Treat displayed stats as a close
>   approximation and never rely on a single reading for a critical decision.
> - **No warranty.** WurmShare is provided "as is," without warranty of any kind,
>   express or implied. To the fullest extent permitted by law, the authors accept
>   no liability for any loss, damage, or account action arising from its use.

WurmShare and WurmMaps.XYZ are **not affiliated with, endorsed by, or sponsored
by** Wurm Online, Code Club AB, Game Chest Group, Wurm Unlimited, or any related
parent or subsidiary companies, products, or intellectual property. WurmMaps.XYZ
is an unofficial, non-profit fan project run by players, for entertainment and
educational purposes only.

---

## About

WurmShare is a **WurmMaps.XYZ** project, developed by **Jason Bloomer** —
made for players, by players.

It is free and open source. See the repository's `LICENSE` file for license
details, and feel free to open an issue or pull request to contribute.
