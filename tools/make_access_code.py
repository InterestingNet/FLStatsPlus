#!/usr/bin/env python3
"""Make an FLStats Plus access code outside the game.

    python make_access_code.py path/to/FLStatsPlus-owner-key.pem "Name"

Same format as the in-game owner window (/flpowner). Needs: pip install cryptography
"""
import os
import sys
import datetime
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.asymmetric.utils import decode_dss_signature

ALPHABET = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"
EPOCH = datetime.datetime(2026, 1, 1, tzinfo=datetime.timezone.utc)


def encode(data: bytes) -> str:
    out, buf, bits = [], 0, 0
    for b in data:
        buf = (buf << 8) | b
        bits += 8
        while bits >= 5:
            out.append(ALPHABET[(buf >> (bits - 5)) & 31])
            bits -= 5
    if bits:
        out.append(ALPHABET[(buf << (5 - bits)) & 31])
    return "".join(out)


def make(key_pem: bytes, label: str) -> str:
    key = serialization.load_pem_private_key(key_pem, None)
    lb = label.strip().encode()
    if len(lb) > 24:
        raise SystemExit("Name is too long (max 24 bytes).")
    days = max(0, (datetime.datetime.now(datetime.timezone.utc) - EPOCH).days)
    payload = bytes([1]) + os.urandom(4) + days.to_bytes(2, "big") + bytes([len(lb)]) + lb
    r, s = decode_dss_signature(key.sign(payload, ec.ECDSA(hashes.SHA256())))
    raw = encode(payload + r.to_bytes(32, "big") + s.to_bytes(32, "big"))
    return "FLP-" + "-".join(raw[i:i + 6] for i in range(0, len(raw), 6))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    print(make(open(sys.argv[1], "rb").read(), sys.argv[2]))
