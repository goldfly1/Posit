#!/usr/bin/env python3
"""
Stub Certification — compile each C# stub template in isolation to verify
it's honest code with no phantom type references.

This is the microscope: every template gets rendered with a test component
name and compiled standalone. If it fails, the template has a bug that would
infect every trial that selects it.

Usage:
    python scripts/certify-stubs.py

Exit 0 if all templates compile clean, 1 if any fail.
"""
import os
import subprocess
import shutil
import sys
import tempfile
from pathlib import Path

POSIT_ROOT = Path(__file__).parent.parent
STUB_DIR = POSIT_ROOT / "patterns" / "csharp-stubs"


def certify_template(template_path: Path) -> dict:
    """Render and compile a single stub template in isolation."""
    content = template_path.read_text()

    # Render with test component name
    rendered = content.replace("{{ComponentName}}", "TestComponent")
    rendered = rendered.replace("{{componentName}}", "TestComponent")

    # Create temp project
    work = Path(tempfile.mkdtemp(prefix="stub-cert-"))
    cs_file = work / "TestComponent.cs"
    cs_file.write_text(rendered)

    proj = work / "TestComponent.csproj"
    proj.write_text("""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>""")

    # Add SqlClient for database templates
    if "SqlClient" in rendered or "SqlConnection" in rendered:
        subprocess.run(
            ["dotnet", "add", str(proj), "package", "Microsoft.Data.SqlClient"],
            capture_output=True, text=True, timeout=60, cwd=str(work),
        )

    # Build
    build = subprocess.run(
        ["dotnet", "build", str(proj)],
        capture_output=True, text=True, timeout=120, cwd=str(work),
    )

    result = {
        "file": template_path.name,
        "status": "GREEN" if build.returncode == 0 else "FAIL",
        "warnings": 0,
        "errors": [],
    }

    if build.returncode == 0:
        result["warnings"] = build.stdout.count("warning")
    else:
        result["errors"] = [
            l.strip()[:120] for l in build.stdout.split("\n") if "error CS" in l
        ]

    # Check for phantom Dafny type references in non-comment lines
    phantom_types = []
    for line in rendered.split("\n"):
        if line.strip().startswith("//"):
            continue
        for marker in ["Result<", "IWorkflow", "WorkflowInstance", "ValidationResult"]:
            if marker in line:
                phantom_types.append(line.strip()[:120])
    result["phantom_types"] = phantom_types

    shutil.rmtree(work, ignore_errors=True)
    return result


def main():
    if not STUB_DIR.exists():
        print(f"[ERROR] Stub directory not found: {STUB_DIR}")
        return 1

    templates = sorted(STUB_DIR.glob("*.cs.template"))
    if not templates:
        print(f"[ERROR] No .cs.template files found in {STUB_DIR}")
        return 1

    print(f"Stub Certification — {len(templates)} templates")
    print(f"{'='*70}")

    all_green = True
    for tmpl in templates:
        result = certify_template(tmpl)

        status_icon = "✅" if result["status"] == "GREEN" else "❌"
        print(f"  {status_icon} {result['file']:<45s} {result['status']:<6s} warns={result['warnings']}", end="")

        if result["errors"]:
            print(f"  errors={len(result['errors'])}")
            for e in result["errors"][:3]:
                print(f"      {e}")
            all_green = False

        if result["phantom_types"]:
            print(f"  ⚠️  phantom types: {result['phantom_types'][:2]}")
            all_green = False
        elif not result["errors"]:
            print()

    print(f"{'='*70}")
    if all_green:
        print(f"PASS — all {len(templates)} templates certified clean")
        return 0
    else:
        print(f"FAIL — templates with issues (see above)")
        return 1


if __name__ == "__main__":
    sys.exit(main())