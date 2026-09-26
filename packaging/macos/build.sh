#!/bin/bash
# Builds the macOS release artifacts for one architecture:
#
#   dist/Perch-<version>-macos-<arch>.dmg          Perch.app (with perch-cli inside)
#   dist/perch-cli-<version>-macos-<arch>.tar.gz   perch-cli + perch-ble, portable
#
#   packaging/macos/build.sh <version> <osx-arm64|osx-x64>
#
# Needs the .NET 9 SDK and the Xcode command line tools. Signing is ad hoc unless these
# are set, in which case it signs with a Developer ID and notarises:
#
#   MACOS_SIGN_IDENTITY     "Developer ID Application: Name (TEAMID)", already in a keychain
#   MACOS_NOTARY_APPLE_ID   Apple ID that notarises
#   MACOS_NOTARY_PASSWORD   an app-specific password for it
#   MACOS_NOTARY_TEAM_ID    the team id
set -euo pipefail

version=${1:?usage: build.sh <version> <osx-arm64|osx-x64>}
rid=${2:?usage: build.sh <version> <osx-arm64|osx-x64>}
arch=${rid#osx-}

root=$(cd "$(dirname "$0")/../.." && pwd)
dist="$root/dist"
work="$root/dist/work-$rid"
rm -rf "$work"
mkdir -p "$work" "$dist"

identity=${MACOS_SIGN_IDENTITY:-}
entitlements="$root/packaging/macos/entitlements.plist"

publish() {
    dotnet publish "$root/src/$1" -c Release -f net9.0 -r "$rid" --self-contained true \
        "-p:Version=$version" "${@:3}" -o "$2"
}

echo "==> Publishing for $rid"
# The app and the CLI share one copy of the runtime inside the bundle, as they do in the
# Windows installer. perch-ble arrives with both through Perch.Platform.
publish Perch.App "$work/app"
publish Perch.Cli "$work/app"
publish Perch.Cli "$work/cli" -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true

echo "==> Assembling Perch.app"
bundle="$work/Perch.app"
mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
cp -R "$work/app/." "$bundle/Contents/MacOS/"
rm -f "$bundle/Contents/MacOS/"*.pdb
cp "$root/assets/perch.icns" "$bundle/Contents/Resources/perch.icns"
sed "s/__VERSION__/$version/g" "$root/packaging/macos/Info.plist" > "$bundle/Contents/Info.plist"
chmod +x "$bundle/Contents/MacOS/Perch" "$bundle/Contents/MacOS/perch-cli" "$bundle/Contents/MacOS/perch-ble"

sign() {
    if [[ -n "$identity" ]]; then
        codesign --force --timestamp --options=runtime --entitlements "$entitlements" --sign "$identity" "$@"
    else
        codesign --force --sign - "$@"
    fi
}

echo "==> Signing (${identity:-ad hoc})"
# Every file, then the bundle: the approach Avalonia documents for .NET apps, whose
# Contents/MacOS holds managed assemblies alongside the native binaries.
find "$bundle/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' file; do sign "$file"; done
sign "$bundle"
codesign --verify --deep --strict "$bundle"

cli="$work/cli"
rm -f "$cli/"*.pdb
chmod +x "$cli/perch-cli" "$cli/perch-ble"
sign "$cli/perch-cli"
sign "$cli/perch-ble"

echo "==> Packaging"
dmg="$dist/Perch-$version-macos-$arch.dmg"
staging="$work/dmg"
mkdir -p "$staging"
cp -R "$bundle" "$staging/"
ln -s /Applications "$staging/Applications"
rm -f "$dmg"
hdiutil create -volname "Perch $version" -srcfolder "$staging" -ov -format UDZO "$dmg"

tarball="$dist/perch-cli-$version-macos-$arch.tar.gz"
tar -czf "$tarball" -C "$cli" perch-cli perch-ble

if [[ -n "$identity" && -n "${MACOS_NOTARY_APPLE_ID:-}" ]]; then
    echo "==> Notarising"
    codesign --force --timestamp --sign "$identity" "$dmg"
    notarize() {
        xcrun notarytool submit "$1" --wait \
            --apple-id "$MACOS_NOTARY_APPLE_ID" \
            --password "$MACOS_NOTARY_PASSWORD" \
            --team-id "$MACOS_NOTARY_TEAM_ID"
    }
    notarize "$dmg"
    xcrun stapler staple "$dmg"

    # A tarball cannot carry a ticket, but notarising its binaries lets Gatekeeper
    # find theirs online.
    (cd "$cli" && zip -q "$work/cli.zip" perch-cli perch-ble)
    notarize "$work/cli.zip"
fi

rm -rf "$work"
echo "==> Built"
ls -l "$dmg" "$tarball"
