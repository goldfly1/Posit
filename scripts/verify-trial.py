#!/usr/bin/env python3
"""
Posit Verification Harness — takes a trial directory and attempts to compile
the C# output into a real .NET project with the shared DafnyRuntime.

Usage:
    python scripts/verify-trial.py trials/T12-task-scheduler
    python scripts/verify-trial.py trials/T15-chat-messaging

What it does:
1. Reads dafny-verification.json for Dafny source per module
2. Re-translates each module to C# using dafny translate cs
3. Creates a .NET project referencing Posit.DafnyRuntime
4. Copies the domain stub C# files
5. Compiles the project
6. Reports: what compiled, what didn't, what's missing
"""
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

POSIT_ROOT = Path(__file__).parent.parent
DAFNY = "C:/Users/goldf/.dotnet/tools/dafny.exe"
Z3 = "C:/Users/goldf/.dotnet/tools/z3/bin/z3.exe"

def verify_trial(trial_dir: str):
    trial_path = Path(trial_dir)
    if not trial_path.exists():
        print(f"[ERROR] Trial directory not found: {trial_dir}")
        return False

    print(f"{'='*60}")
    print(f"Verifying trial: {trial_path.name}")
    print(f"{'='*60}")

    # 1. Read dafny-verification.json
    dafny_ver_file = trial_path / "dafny-verification.json"
    if not dafny_ver_file.exists():
        print("[ERROR] No dafny-verification.json found")
        return False

    dafny_ver = json.loads(dafny_ver_file.read_text())
    if not isinstance(dafny_ver, list):
        dafny_ver = [dafny_ver]

    print(f"\n[1] Dafny modules: {len(dafny_ver)}")
    for mod in dafny_ver:
        name = mod.get("moduleName", "?")
        verified = mod.get("isVerified", False)
        print(f"    {name:25s} verified={verified}")

    # 2. Re-translate each module to C#, then strip Dafny runtime from all but the first
    work_dir = Path(tempfile.mkdtemp(prefix="posit-verify-"))
    cs_dir = work_dir / "src"
    cs_dir.mkdir(parents=True)

    # Copy pattern dependency files (result.dfy, etc.) to work dir
    patterns_dir = POSIT_ROOT / "patterns"
    for dfy in patterns_dir.glob("*.dfy"):
        shutil.copy(dfy, work_dir / dfy.name)

    # Deduplicate modules
    seen_modules = set()
    unique_modules = []
    for mod in dafny_ver:
        name = mod.get("moduleName", "?")
        if name not in seen_modules:
            seen_modules.add(name)
            unique_modules.append(mod)

    print(f"\n[2] Re-translating {len(unique_modules)} Dafny modules to C#...")
    translated = 0
    failed = 0
    cs_files = []
    for i, mod in enumerate(unique_modules):
        name = mod.get("moduleName", "?")
        dafny_source = mod.get("dafnySource", "")
        if not dafny_source:
            continue

        dfy_file = work_dir / f"{name}.dfy"
        dfy_file.write_text(dafny_source)

        cs_file = cs_dir / f"{name}.cs"
        result = subprocess.run(
            [DAFNY, "translate", "cs", str(dfy_file),
             "--no-verify", "--allow-external-contracts", "--allow-warnings",
             f"--output:{cs_file}"],
            capture_output=True, text=True, timeout=120,
            cwd=str(work_dir)
        )

        if result.returncode == 0 and cs_file.exists():
            translated += 1
            content = cs_file.read_text()
            
            if i > 0:
                # Strip Dafny runtime namespace (keep only in first file)
                # Simple approach: remove everything before 'namespace _module'
                dafny_ns_end = content.find('namespace _module')
                if dafny_ns_end > 0:
                    runtime_block = content[:dafny_ns_end]
                    module_block = content[dafny_ns_end:]
                    content = module_block
                
                # Rename _module to _module_{name} to avoid __default collisions
                content = content.replace('namespace _module', f'namespace _module_{name}')
                content = content.replace('using _module;', f'using _module_{name};')
                
                cs_file.write_text(content)
                print(f"    {name:25s} ✅ translated + runtime stripped + namespace renamed ({cs_file.stat().st_size} bytes)")
            else:
                # First file: rename _module but keep Dafny runtime
                content = content.replace('namespace _module', f'namespace _module_{name}')
                cs_file.write_text(content)
                print(f"    {name:25s} ✅ translated with runtime + namespace renamed ({cs_file.stat().st_size} bytes)")
            cs_files.append(cs_file)
        else:
            failed += 1
            error = (result.stdout + result.stderr)[:200]
            print(f"    {name:25s} ❌ FAIL: {error}")

    print(f"    Translated: {translated}, Failed: {failed}")

    # 3. Copy domain stub C# files
    stub_cs_dir = trial_path / "csharp"
    if stub_cs_dir.exists():
        print(f"\n[3] Copying domain stub C# files...")
        stub_count = 0
        for cs_file in stub_cs_dir.glob("*.cs"):
            shutil.copy(cs_file, cs_dir / cs_file.name)
            stub_count += 1
        print(f"    Copied {stub_count} stub files")

    # 4. Create .NET project
    print(f"\n[4] Creating .NET project...")
    proj_file = work_dir / "Posit.Verified.csproj"
    proj_content = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\\..\\src\\Posit.DafnyRuntime\\Posit.DafnyRuntime.csproj" />
  </ItemGroup>
</Project>"""
    # Fix the project reference path to be absolute
    runtime_proj = POSIT_ROOT / "src" / "Posit.DafnyRuntime" / "Posit.DafnyRuntime.csproj"
    proj_content = f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{runtime_proj}" />
  </ItemGroup>
</Project>"""
    proj_file.write_text(proj_content)
    print(f"    Project: {proj_file}")

    # 5. Build
    print(f"\n[5] Building .NET project...")
    result = subprocess.run(
        ["dotnet", "build", str(proj_file)],
        capture_output=True, text=True, timeout=120,
        cwd=str(work_dir)
    )

    if result.returncode == 0:
        print(f"    ✅ BUILD SUCCEEDED")
        # Count warnings
        warnings = result.stdout.count("warning")
        print(f"    Warnings: {warnings}")
    else:
        print(f"    ❌ BUILD FAILED")
        # Extract errors
        errors = [l for l in result.stdout.split('\n') if 'error' in l.lower() or 'Error' in l]
        print(f"    Errors: {len(errors)}")
        for err in errors[:10]:
            print(f"      {err.strip()[:120]}")

    # 6. Copy test files and try to build them too
    test_dir = trial_path / "tests"
    if test_dir.exists():
        print(f"\n[6] Copying test files...")
        test_cs_dir = work_dir / "tests"
        test_cs_dir.mkdir()
        for cs_file in test_dir.glob("*.cs"):
            shutil.copy(cs_file, test_cs_dir / cs_file.name)
        test_count = len(list(test_dir.glob("*.cs")))
        print(f"    Copied {test_count} test files")

    # Cleanup
    print(f"\n[7] Cleanup...")
    shutil.rmtree(work_dir, ignore_errors=True)
    print(f"    Work directory removed")

    print(f"\n{'='*60}")
    print(f"SUMMARY: {trial_path.name}")
    print(f"  Dafny modules: {len(dafny_ver)}")
    print(f"  Translated: {translated}")
    print(f"  Translation failures: {failed}")
    print(f"  Build: {'SUCCEEDED' if result.returncode == 0 else 'FAILED'}")
    print(f"{'='*60}")

    return result.returncode == 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print("Usage: python scripts/verify-trial.py <trial-directory>")
        print("Example: python scripts/verify-trial.py trials/T12-task-scheduler")
        sys.exit(1)

    success = verify_trial(sys.argv[1])
    sys.exit(0 if success else 1)