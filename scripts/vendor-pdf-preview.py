"""Refresh the pinned offline PDF.js assets (Python 3, standard library only)."""
import base64
import hashlib
import io
from pathlib import Path
import tarfile
import urllib.request

VERSION = "6.3.289"
INTEGRITY = "ZHjSVpDa3D6izMq8/04lvkhkATUmL9px6ChPaXc1k6nU2Mrhlg1/7F0bdUqCwUjw3NsPTfPZsMDUU6ZIcRaeQw=="
URL = f"https://registry.npmjs.org/pdfjs-dist/-/pdfjs-dist-{VERSION}.tgz"
TARGET = Path(__file__).resolve().parents[1] / "src/FilesMate.App/Assets/PdfPreview"

def main():
    with urllib.request.urlopen(URL, timeout=60) as response:
        archive = response.read(32 * 1024 * 1024 + 1)
    if len(archive) > 32 * 1024 * 1024:
        raise RuntimeError("Unexpected archive size")
    if base64.b64encode(hashlib.sha512(archive).digest()).decode() != INTEGRITY:
        raise RuntimeError("PDF.js archive integrity mismatch")
    count = 0
    with tarfile.open(fileobj=io.BytesIO(archive), mode="r:gz") as package:
        for member in package:
            if not member.isfile() or not member.name.startswith("package/"):
                continue
            relative = member.name.removeprefix("package/")
            if relative not in {"LICENSE", "build/pdf.mjs", "build/pdf.worker.mjs", "web/pdf_viewer.css"} and not relative.startswith(("cmaps/", "standard_fonts/", "wasm/")):
                continue
            destination = (TARGET / relative).resolve()
            if not destination.is_relative_to(TARGET.resolve()) or member.size > 16 * 1024 * 1024:
                raise RuntimeError("Unexpected archive member")
            destination.parent.mkdir(parents=True, exist_ok=True)
            with package.extractfile(member) as source:
                destination.write_bytes(source.read())
            count += 1
    (TARGET / "VERSION.txt").write_text(f"pdfjs-dist {VERSION}\nSource: {URL}\nsha512-{INTEGRITY}\n", encoding="utf8")
    print(f"Verified and copied {count} PDF.js assets")

if __name__ == "__main__":
    main()
