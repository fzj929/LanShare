#!/bin/sh
set -eu
cd -- "$(dirname -- "$0")"
chmod +x ./LanShare
exec ./LanShare --no-browser "$@"
