#!/usr/bin/env bash
# Connects ADB to the Android phone over Wi-Fi so the phone's only USB-C port is free for the
# XREAL glasses. Works on IPv4 networks and on IPv6-only networks.
#
# TCP mode survives Wi-Fi changes but not a phone reboot: after a reboot, run this again with the
# phone plugged in over USB.

set -euo pipefail

PORT="${ADB_PORT:-5555}"
USB_WAIT_SECONDS=60
CONNECT_ATTEMPTS=3

usage() {
    cat <<EOF
Usage:
  $(basename "$0")                 Plug the phone in over USB, run, then unplug once connected.
  $(basename "$0") <address>       Reconnect without USB while the phone is still in TCP mode
                                   (IPv4, IPv6, or address:port).
  $(basename "$0") --disconnect    Drop all wireless ADB connections.

Environment:
  ADB        Path to adb (default: newest Unity editor's bundled adb, then adb on PATH).
  ADB_PORT   TCP port (default: 5555).
EOF
}

find_adb() {
    if [[ -n "${ADB:-}" ]]; then
        echo "$ADB"
        return
    fi
    local unity_adb
    unity_adb=$(ls -d /Applications/Unity/Hub/Editor/*/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb 2>/dev/null | tail -1 || true)
    if [[ -n "$unity_adb" ]]; then
        echo "$unity_adb"
        return
    fi
    command -v adb || { echo "error: adb not found; set ADB=/path/to/adb" >&2; exit 1; }
}

ADB_BIN=$(find_adb)
adb() { "$ADB_BIN" "$@"; }

# Formats an address as an adb connect target, adding the port and IPv6 brackets as needed.
to_target() {
    local addr="$1"
    case "$addr" in
        \[*\]:*) echo "$addr" ;;            # [ipv6]:port
        *:*:*)   echo "[$addr]:$PORT" ;;    # bare ipv6
        *:*)     echo "$addr" ;;            # ipv4:port
        *)       echo "$addr:$PORT" ;;      # bare ipv4
    esac
}

# Succeeds once adb is connected to the target and the device answers a shell command.
connect_to() {
    local target="$1" out="" attempt
    for ((attempt = 1; attempt <= CONNECT_ATTEMPTS; attempt++)); do
        out=$(adb connect "$target" 2>&1 || true)
        if [[ "$out" == *"connected to"* ]] && adb -s "$target" shell true >/dev/null 2>&1; then
            return 0
        fi
        sleep 2
    done
    echo "  could not connect to $target: $out" >&2
    return 1
}

# Prints the serial of the first authorized phone attached over USB.
wait_for_usb_device() {
    local deadline=$((SECONDS + USB_WAIT_SECONDS)) hinted="" serial state
    while ((SECONDS < deadline)); do
        while read -r serial state _; do
            # Skip the header and network (address:port) connections.
            [[ -z "$serial" || "$serial" == "List" || "$serial" == *:* ]] && continue
            if [[ "$state" == "device" ]]; then
                echo "$serial"
                return 0
            fi
            if [[ "$state" == "unauthorized" && -z "$hinted" ]]; then
                echo "Phone found but unauthorized: accept the 'Allow USB debugging' prompt on the phone." >&2
                hinted=1
            fi
        done < <(adb devices)
        sleep 2
    done
    return 1
}

# Prints the phone's Wi-Fi addresses to try: IPv4 first, then the stable IPv6, then other global IPv6.
phone_addresses() {
    local serial="$1"
    {
        adb -s "$serial" shell ip -4 addr show wlan0 2>/dev/null |
            awk '/inet /{split($2, a, "/"); print a[1]}'
        adb -s "$serial" shell ip -6 addr show wlan0 2>/dev/null |
            awk '/scope global/ && /mngtmpaddr/{split($2, a, "/"); print a[1]}'
        adb -s "$serial" shell ip -6 addr show wlan0 2>/dev/null |
            awk '/scope global/{split($2, a, "/"); print a[1]}'
    } | awk 'NF && !seen[$0]++'
}

case "${1:-}" in
    -h|--help)
        usage
        exit 0
        ;;
    --disconnect)
        adb disconnect
        exit 0
        ;;
    "")
        ;;
    *)
        target=$(to_target "$1")
        connect_to "$target" || exit 1
        echo "Connected: $target"
        exit 0
        ;;
esac

echo "Using adb: $ADB_BIN"
echo "Waiting up to ${USB_WAIT_SECONDS}s for the phone on USB..."
serial=$(wait_for_usb_device) || {
    echo "error: no authorized phone on USB. Plug it in with USB debugging enabled and try again." >&2
    exit 1
}
echo "Found $(adb -s "$serial" shell getprop ro.product.model) ($serial) on USB"

addresses=$(phone_addresses "$serial")
if [[ -z "$addresses" ]]; then
    echo "error: the phone has no Wi-Fi address. Turn on Wi-Fi and join the same network as this Mac." >&2
    exit 1
fi

adb -s "$serial" tcpip "$PORT" >/dev/null
sleep 3

for addr in $addresses; do
    target=$(to_target "$addr")
    echo "Connecting to $target..."
    if connect_to "$target"; then
        echo
        echo "Connected: $target"
        echo "You can unplug the USB cable now."
        exit 0
    fi
done

echo "error: could not reach the phone over Wi-Fi. Check that the phone and this Mac are on the same network." >&2
exit 1
