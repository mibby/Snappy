# Snappy
#### (Formerly XIVSnapper)

### A forked and maintained mod originally based on work by @eqbot, @ViviAshe, @astrodoobs & @BeSlightly.

**It is not recommended you use this fork if the upstream version is available and updated. This is a custom build with experimental (probably broken) changes & fixes specific to my needs.**

### What is it?
Ever had a friend ask you to share your character’s appearance because it looked especially sharp that day - only to realize you’d have to dig through your mod list or settle for the limited Mare Character Data Format (MCDF)? Snappy solves that problem.

Snappy is a plugin designed to save and load your character’s full appearance - mods and all - with a single click. No restrictions, no compromises. It creates a single-character mod collection that captures everything exactly as it is. That means you can share your setup with friends, and they can load your character just as you see it, then customize it however they like. No more relying on MCDF. Just a clean, accurate snapshot of your character, ready to use.

### Where to get it:
Add this custom repo to Dalamud (check their documentation if you’re unfamiliar with that process):

`https://raw.githubusercontent.com/mibby/Snappy/0.2.0-dev/data/pluginmaster.json`

### How to use it:
1. **Set a working directory** in the settings menu.
  - Optionally, also set a Glamourer Fallback string in case the plugin can't get your Glamourer string. 
2. **To save snapshots**: Press the save icon.
  - If you already have a Snapshot, you can append to the existing save. **Hitting the `save` icon while having a Snapshot appends for you.**
3. **To load snapshots**:
   - Enter GPose.
   - Select an actor.
   - Use the Load button to pick a snapshot folder.
   - Best performance comes from using [Brio](https://github.com/AsgardXIV/Brio)-spawned actors. I test against those, and if you load onto anything else, you're on your own. Fixes for that won’t be a priority.
