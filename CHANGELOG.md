# Changelog

## Unreleased

### Finding Affinity after updates
- If you type (or pick) an Affinity folder, the app now always uses that folder. Previously it could silently patch a different, auto-detected folder instead — for example an old version left behind after an update.
- When several Affinity version folders exist side by side, the app automatically picks the newest one.
- Newer installs that register under the Canva name (instead of Serif) are now found automatically too.

### Clearer progress while patching
- Before changing anything, the app tells you exactly which file it is about to patch.
- Icon lines in the log are now yellow, so the final green **Finished** line clearly stands out at the bottom.
- At the end you get a short summary, e.g. "Replaced 2033 icons with v2 versions, kept 615 originals." If nothing could be replaced, the app says so plainly instead of looking finished.
- Every successful run ends with a reminder to restart Affinity to see the result.
- Colorizing is noticeably faster on large icon sets.

### Safer patching
- The original file is no longer deleted before the new one is ready. If writing fails, your Affinity installation is left untouched.
- If Affinity is still running, the app warns you before touching its files (or use `--terminate` to close it automatically).
- Permission problems now produce a plain-language explanation (run as administrator, close Affinity, check folder permissions) instead of a crash with a technical error.
- New **Restore backup** button (and `restore` command): brings back the original files from the `.bak` backup copies in one click.

### Friendlier window
- Next to "Admin rights" the app now explains that it will ask for permission automatically when you press a button ("no" is shown in red, the explanation in green).
- The detected values (status, path, files) are bold; the labels are not.
- Detected files are listed one per line.
- The two main actions (**Colorize icons**, **Replace splash**) are highlighted in blue; the folder/import/export tools sit right-aligned.
- Fixed a crash that could happen when copying text (e.g. the path) out of the window with Ctrl+C.
