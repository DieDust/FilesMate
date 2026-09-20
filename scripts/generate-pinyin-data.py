"""Generate the offline name-sorting table from the pinned Unicode 17.0 Unihan data.

Run from the repository root. Only this development script uses the network.
The shipped table uses the first kMandarin reading, without contextual polyphone inference.
"""
import hashlib
from pathlib import Path
import struct
import unicodedata
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
archive = ROOT / "artifacts/Unihan-17.0.0.zip"
archive.parent.mkdir(parents=True, exist_ok=True)
if not archive.exists():
    urllib.request.urlretrieve("https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip", archive)
if hashlib.sha256(archive.read_bytes()).hexdigest() != "f7a48b2b545acfaa77b2d607ae28747404ce02baefee16396c5d2d7a8ef34b5e":
    raise ValueError("Unihan archive checksum mismatch")
readings = {}
with zipfile.ZipFile(archive) as package:
    for line in package.read("Unihan_Readings.txt").decode("utf-8").splitlines():
        fields = line.split("\t")
        if len(fields) != 3 or fields[1] != "kMandarin":
            continue
        syllable = unicodedata.normalize("NFD", fields[2].split()[0].lower())
        syllable = syllable.replace("u\u0308", "v")
        syllable = "".join(c for c in syllable if not unicodedata.combining(c))
        if not syllable.isascii() or not syllable.isalpha():
            raise ValueError(f"Unexpected Mandarin reading: {line}")
        readings[int(fields[0][2:], 16)] = syllable
syllables = sorted(set(readings.values()))
ids = {value: index for index, value in enumerate(syllables)}
target = ROOT / "src/FilesMate.Core/Entries/Data/mandarin-17.bin"
target.parent.mkdir(parents=True, exist_ok=True)
with target.open("wb") as output:
    output.write(b"FMP1")
    output.write(struct.pack("<HI", len(syllables), len(readings)))
    for syllable in syllables:
        encoded = syllable.encode("ascii")
        output.write(struct.pack("<B", len(encoded)) + encoded)
    for codepoint, syllable in sorted(readings.items()):
        output.write(struct.pack("<IH", codepoint, ids[syllable]))
print(f"{len(readings)} characters, {len(syllables)} syllables, {target.stat().st_size} bytes")
