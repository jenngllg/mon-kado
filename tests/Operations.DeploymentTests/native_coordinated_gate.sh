#!/usr/bin/env bash
# Run in a new mount/network namespace; never modify the caller's firewall.
set -Eeuo pipefail
[[ ${EUID} == 0 && -n ${MONKADO_TEST_NFT:-} ]] || exit 1
[[ $(readlink /proc/self/ns/net) != $(readlink /proc/1/ns/net) &&
   $(readlink /proc/self/ns/mnt) != $(readlink /proc/1/ns/mnt) ]] || {
    echo 'An isolated mount and network namespace is required.' >&2
    exit 1
}
repo=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)
work=$(mktemp -d /tmp/monkado-gate-test-XXXXXX)
server=
cleanup() {
    if [[ -n $server ]]; then kill "$server" 2>/dev/null || true; wait "$server" 2>/dev/null || true; fi
    ip netns del monkado-gate-client 2>/dev/null || true
    rm -rf -- "$work"
}
trap cleanup EXIT
ip netns add monkado-gate-client
ip link add gate-server type veth peer name gate-client
ip link set gate-client netns monkado-gate-client
ip addr add 192.0.2.1/24 dev gate-server
ip -6 addr add 2001:db8::1/64 dev gate-server nodad
ip link set gate-server up
ip link set lo up
ip netns exec monkado-gate-client ip addr add 192.0.2.2/24 dev gate-client
ip netns exec monkado-gate-client ip -6 addr add 2001:db8::2/64 dev gate-client nodad
ip netns exec monkado-gate-client ip link set gate-client up
ip netns exec monkado-gate-client ip link set lo up
mkfifo "$work/ready"
python3 -u - >"$work/ready" <<'PY' &
import selectors, socket
selector = selectors.DefaultSelector()
for family, host in ((socket.AF_INET, "0.0.0.0"), (socket.AF_INET6, "::")):
    for port in (22, 80, 443, 8080):
        listener = socket.socket(family)
        if family == socket.AF_INET6:
            listener.setsockopt(socket.IPPROTO_IPV6, socket.IPV6_V6ONLY, 1)
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        listener.bind((host, port))
        listener.listen()
        selector.register(listener, selectors.EVENT_READ)
print("ready", flush=True)
while True:
    for key, _ in selector.select():
        client, _ = key.fileobj.accept()
        with client:
            client.recv(4096)
            client.sendall(b"HTTP/1.0 200 OK\r\nContent-Length: 2\r\n\r\nOK")
PY
server=$!
read -r ready < "$work/ready"
[[ $ready == ready ]]
probe() { curl --noproxy '*' --fail --silent --show-error --max-time 2 "http://$1:$2/" >/dev/null; }
remote() { ip netns exec monkado-gate-client curl --noproxy '*' --fail --silent --max-time 2 "http://192.0.2.1:$1/" >/dev/null; }
remote6() { ip netns exec monkado-gate-client curl --noproxy '*' --fail --silent --max-time 2 "http://[2001:db8::1]:$1/" >/dev/null; }
gate() {
    PYTHONPATH="$repo/src/Operations.Deployment" python3 - "$1" <<'PY'
import os, sys
import coordinated_gate
coordinated_gate.NFT = os.environ["MONKADO_TEST_NFT"]
getattr(coordinated_gate.Gate(), sys.argv[1])()
PY
}
remote 80
remote 443
remote6 80
remote6 443
gate close
probe 127.0.0.1 80
probe 127.0.0.1 443
remote 8080
remote 22
remote6 22
if remote 80 || remote 443 || remote6 80 || remote6 443; then
    echo 'Public HTTP remained reachable while the gate was closed.' >&2
    exit 1
fi
# Simulate loss of kernel state on reboot while durable intent survives. The
# boot guard must recreate the gate before Caddy is allowed to start.
mkdir "$work/state"
touch "$work/state/coordinated-transition.json"
gate open
PYTHONPATH="$repo/src/Operations.Deployment" python3 - "$work/state" <<'PY'
import os, sys
from pathlib import Path
import coordinated_gate
from coordinated_boot import guard
coordinated_gate.NFT = os.environ["MONKADO_TEST_NFT"]
assert guard(Path(sys.argv[1]), coordinated_gate.Gate()) == "closed"
PY
if remote 80 || remote 443 || remote6 80 || remote6 443; then
    echo 'The boot guard did not restore the gate.' >&2
    exit 1
fi
rm -- "$work/state/coordinated-transition.json"
gate open
remote 80
remote 443
remote6 80
remote6 443
echo 'Isolated native HTTP gate passed.'
