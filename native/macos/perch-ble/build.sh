#!/bin/sh
# Builds perch-ble, the CoreBluetooth helper, as a universal (arm64 + x86_64) binary.
#
#   native/macos/perch-ble/build.sh [output-path]
#
# Needs the Xcode command line tools (xcode-select --install); nothing else.
set -eu

here=$(cd "$(dirname "$0")" && pwd)
out=${1:-"$here/../../../dist/perch-ble"}
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT

mkdir -p "$(dirname "$out")"

for arch in arm64 x86_64; do
    # The Info.plist goes into the binary's __TEXT section, which is where macOS looks
    # for a bundle-less program's Bluetooth usage description.
    xcrun swiftc -O -swift-version 5 \
        -target "$arch-apple-macos12.0" \
        -Xlinker -sectcreate -Xlinker __TEXT -Xlinker __info_plist -Xlinker "$here/Info.plist" \
        -o "$work/perch-ble-$arch" \
        "$here/main.swift"
done

lipo -create -output "$out" "$work/perch-ble-arm64" "$work/perch-ble-x86_64"
codesign --force --sign - "$out"
echo "Built $out ($(lipo -archs "$out"))"
