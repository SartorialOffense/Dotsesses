# Signing & Distribution (macOS)

How to publish Dotsesses as a `.app` bundle that runs without a console
window, and how to evolve from local-only signing to friends-can-double-click
distribution.

## The three stages

| Stage | Cost | Friends' experience | Use when |
|---|---|---|---|
| **1. Ad-hoc** (default) | $0 | Gatekeeper blocks first launch; must right-click → Open, or strip quarantine | Local dev, you trust the recipient to follow instructions |
| **2. Developer ID** | $99/yr | Gatekeeper still blocks on first launch (notarization required to skip) | Bridge step while setting up notarization |
| **3. Developer ID + notarized** | $99/yr | Double-click, no warnings | Real distribution |

All three use the same `publish-macos.sh` script — only the
`SIGNING_IDENTITY` env var and a few extra steps change.

---

## Stage 1 — Ad-hoc signing (current default)

No setup needed. Just run:

```bash
./publish-macos.sh
open ./publish/Dotsesses.app
```

The script signs with identity `-` (ad-hoc), which is enough for the app to
launch locally as a proper windowed GUI app — no terminal, no console.

**For friends:** they download `Dotsesses.zip`, unzip it, then either:

- Right-click `Dotsesses.app` → Open → Open (one-time approval), **or**
- Run `xattr -dr com.apple.quarantine /path/to/Dotsesses.app` before
  launching.

Without one of those, macOS Gatekeeper will refuse to launch the app
because the signature isn't from a known developer.

---

## Stage 2 — Developer ID Application certificate

One-time setup:

1. Enroll in the [Apple Developer Program](https://developer.apple.com/programs/) ($99/yr).
2. In Xcode → Settings → Accounts, sign in with your Apple ID, then create
   a "Developer ID Application" certificate. Xcode installs it into your
   login keychain automatically.
3. Confirm it's there:

   ```bash
   security find-identity -v -p codesigning
   ```

   You should see a line like
   `1) ABC123... "Developer ID Application: Your Name (TEAMID)"`.

Use it:

```bash
SIGNING_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
    ./publish-macos.sh
```

At this point the signature is real, but Gatekeeper will still warn
friends on first launch until the app is notarized (stage 3).

### Hardened runtime

For notarization, Apple requires the **hardened runtime** flag. Add
`--options runtime` to both `codesign` calls in `publish-macos.sh` when
moving to stage 3:

```bash
codesign --force --sign "$SIGNING_IDENTITY" \
    --timestamp --options runtime "$APP_BUNDLE"
```

Note: `--timestamp=none` becomes `--timestamp` (Apple requires a secure
timestamp for notarization).

---

## Stage 3 — Notarization

Notarization uploads the signed app to Apple, which scans it and returns
a "ticket" you staple to the bundle. After that, Gatekeeper trusts it.

One-time setup — create an app-specific password for `notarytool`:

1. At [appleid.apple.com](https://appleid.apple.com) → Sign-In and
   Security → App-Specific Passwords, generate one for "notarytool".
2. Store credentials in the keychain so you don't have to retype:

   ```bash
   xcrun notarytool store-credentials "dotsesses-notary" \
       --apple-id "you@example.com" \
       --team-id "TEAMID" \
       --password "xxxx-xxxx-xxxx-xxxx"
   ```

Then, after running the publish script (with hardened runtime enabled):

```bash
# Submit the zip and wait
xcrun notarytool submit ./publish/Dotsesses.zip \
    --keychain-profile "dotsesses-notary" \
    --wait

# If it says "Accepted", staple the ticket to the .app bundle
xcrun stapler staple ./publish/Dotsesses.app

# Re-zip so the zip contains the stapled bundle
rm ./publish/Dotsesses.zip
ditto -c -k --sequesterRsrc --keepParent \
    ./publish/Dotsesses.app ./publish/Dotsesses.zip
```

Friends can now download, unzip, and double-click — no warnings.

If notarization fails, fetch the log:

```bash
xcrun notarytool log <submission-id> --keychain-profile "dotsesses-notary"
```

Common failures: missing hardened runtime, unsigned nested binary, or a
disallowed entitlement.

---

## Known gotcha: `PublishSingleFile` and notarization

The `.csproj` currently sets `PublishSingleFile=true` and
`IncludeNativeLibrariesForSelfExtract=true` for macOS Release. At runtime,
.NET extracts the bundled native libraries (`libSkiaSharp.dylib`, etc.)
into a cache directory. Those extracted copies are **not** signed.

For ad-hoc and Developer ID signing this is fine. For notarization with
hardened runtime, the extracted libs may fail to load because the runtime
refuses unsigned dylibs.

If you hit this at stage 3, switch the publish to a non-single-file
layout. In `Dotsesses.csproj`, change the Release/OSX property group to:

```xml
<PublishSingleFile>false</PublishSingleFile>
```

The bundle ends up with many files in `Contents/MacOS/` instead of one
big binary, and the inside-out signing loop in `publish-macos.sh` already
handles them.
