"""Reconstruct the approved intro, verifying its immutable digest."""
import hashlib
import pathlib
import sys
import tempfile
import os

root = pathlib.Path(__file__).resolve().parent
expected = "398e8564ff27401011067a1f29bffa9c7ce134e6d30caf4b62ff2fd5f970daa6"
if len(sys.argv) != 2:
    raise SystemExit("Usage: python assemble_master.py output.webm")
output = pathlib.Path(sys.argv[1]).resolve()
output.parent.mkdir(parents=True, exist_ok=True)
data = bytearray()
for i in range(10):
    part = (root / "master" / f"intro.{i:03}.part").read_bytes()
    if len(part) != (316840 if i == 9 else 375000):
        raise SystemExit(f"Invalid part length: {i}")
    data.extend(part)
if hashlib.sha256(data).hexdigest() != expected:
    raise SystemExit("Invalid approved intro SHA-256")
with tempfile.NamedTemporaryFile(dir=output.parent, delete=False) as file:
    temporary = pathlib.Path(file.name)
    file.write(data)
try:
    os.replace(temporary, output)
finally:
    temporary.unlink(missing_ok=True)
print(expected, output)
