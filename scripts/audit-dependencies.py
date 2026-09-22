"""Check NuGet advisories and the separately vendored PDF.js release. No source files are uploaded."""
import json
import re
import subprocess
import urllib.request
from pathlib import Path

root = Path(__file__).resolve().parents[1]
result = subprocess.run(
    ["dotnet", "list", str(root / "FilesMate.slnx"), "package", "--vulnerable", "--include-transitive", "--format", "json"],
    cwd=root, capture_output=True, text=True, encoding="utf-8", check=True)
nuget = json.loads(result.stdout)
if nuget.get("problems"):
    raise RuntimeError(f"NuGet audit incomplete: {nuget['problems']}")
findings = []
for project in nuget.get("projects", []):
    for framework in project.get("frameworks", []):
        for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
            if package.get("vulnerabilities"):
                findings.append({"package": package["id"], "version": package["resolvedVersion"], "advisories": package["vulnerabilities"]})
version = re.search(r'^VERSION = "([^"]+)"', (root / "scripts/vendor-pdf-preview.py").read_text(), re.M)[1]
request = urllib.request.Request("https://api.osv.dev/v1/query", method="POST",
    data=json.dumps({"package": {"name": "pdfjs-dist", "ecosystem": "npm"}, "version": version}).encode(),
    headers={"Content-Type": "application/json"})
with urllib.request.urlopen(request, timeout=30) as response:
    pdf = json.load(response)
for advisory in pdf.get("vulns", []):
    if not advisory.get("withdrawn"):
        findings.append({"package": "pdfjs-dist", "version": version, "advisory": advisory["id"]})
output = root / "artifacts/dependency-audit.json"
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps({"nuget": nuget, "pdfjs": {"version": version, "result": pdf}, "findings": findings}, indent=2), encoding="utf-8")
print(json.dumps({"findings": findings, "pdfjs": version}, indent=2))
raise SystemExit(bool(findings))
