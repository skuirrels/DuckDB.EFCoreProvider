#!/usr/bin/env python3
"""Validate package assets and EF selection in isolated consumers of the packed NTS package."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def run(arguments, directory, expected_failure=False):
    result = subprocess.run(arguments, cwd=directory, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if expected_failure:
        diagnostics = (expected_failure,) if isinstance(expected_failure, str) else ("NU1605", "NU1608", "NU1107")
        if result.returncode == 0 or not any(code in result.stdout for code in diagnostics):
            raise RuntimeError(f"Expected failure containing {diagnostics}:\n{result.stdout}")
    elif result.returncode:
        raise RuntimeError(result.stdout)
    return result.stdout


def main():
    artifacts = Path(sys.argv[1] if len(sys.argv) > 1 else "artifacts").resolve()
    versions = {}
    ef_versions = {}
    for major in (10, 11):
        core_id = "DuckDB.EFCoreProvider" + (".EF11" if major == 11 else "")
        for package_id in (core_id, core_id + ".NTS"):
            candidates = list((artifacts / f"ef{major}").glob(f"{package_id}.*.nupkg"))
            candidates = [p for p in candidates if ".NTS." not in p.name or package_id.endswith(".NTS")]
            if len(candidates) != 1:
                raise RuntimeError(f"Expected exactly one {package_id} package in {artifacts / f'ef{major}'}")
            with zipfile.ZipFile(candidates[0]) as package:
                root = ET.fromstring(package.read(next(n for n in package.namelist() if n.endswith(".nuspec"))))
                metadata = root.find("{*}metadata")
                version = metadata.find("{*}version").text
                versions[major, package_id] = version
                assert ("-" in version) == (major == 11), (package_id, version)
                assemblies = [n for n in package.namelist() if n.startswith("lib/") and n.endswith(".dll")]
                assembly_name = "DuckDB.EFCoreProvider" + (".NTS" if package_id.endswith(".NTS") else "")
                assert assemblies == [f"lib/net{major}.0/{assembly_name}.dll"], assemblies
                groups = metadata.findall("{*}dependencies/{*}group")
                assert len(groups) == 1 and groups[0].get("targetFramework") == f"net{major}.0"
                dependencies = {d.get("id"): d.get("version") for d in groups[0]}
                ef_range = dependencies["Microsoft.EntityFrameworkCore.Relational"]
                if major == 10:
                    assert ef_range.startswith("[10.") and ef_range.endswith(", 10.1.0)"), ef_range
                    assert all("-" not in value.split(",")[0] for value in dependencies.values()), dependencies
                else:
                    assert ef_range.startswith("[11.") and ef_range.endswith("]") and "," not in ef_range, ef_range
                ef_versions[major] = ef_range[1:].split(",")[0].rstrip("]")
                if package_id.endswith(".NTS"):
                    assert versions[major, core_id] in dependencies[core_id]
    print("Package assets, versions, dependency groups and EF bounds verified.", flush=True)

    with tempfile.TemporaryDirectory(prefix="duckdb-package-consumers-") as temp:
        root = Path(temp)
        repository = Path(__file__).resolve().parent.parent
        pack = ["dotnet", "pack", str(repository / "src/DuckDB.EFCoreProvider/DuckDB.EFCoreProvider.csproj"),
                "--no-build", "--no-restore", "-o", str(root / "rejected")]
        run(pack, repository, "Select an EF package line")
        run(pack + ["-p:DuckDBEFCoreMajorVersion=12"], repository, "must be 10 or 11")
        run(pack + ["-p:DuckDBEFCoreMajorVersion=10", "-p:TargetFrameworks=net11.0"], repository,
            "must contain only net10.0")
        print("Ambiguous and mismatched package builds are rejected.", flush=True)
        config = ET.Element("configuration")
        sources = ET.SubElement(config, "packageSources")
        ET.SubElement(sources, "clear")
        for key, value in (("ef10", str(artifacts / "ef10")), ("ef11", str(artifacts / "ef11")),
                           ("nuget.org", "https://api.nuget.org/v3/index.json")):
            ET.SubElement(sources, "add", key=key, value=value)
        settings = ET.SubElement(config, "config")
        ET.SubElement(settings, "add", key="globalPackagesFolder", value=str(root / "packages"))
        ET.ElementTree(config).write(root / "NuGet.config", encoding="unicode")

        for framework, major, wrong_ef in ((10, 10, None), (11, 10, None), (11, 11, None),
                                           (11, 10, 11), (11, 11, 10)):
            name = f"net{framework}-ef{major}" + (f"-mismatch{wrong_ef}" if wrong_ef else "")
            directory = root / name
            directory.mkdir()
            project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
            properties = ET.SubElement(project, "PropertyGroup")
            for key, value in (("OutputType", "Exe"), ("TargetFramework", f"net{framework}.0"),
                               ("ImplicitUsings", "enable"), ("Nullable", "enable"), ("TreatWarningsAsErrors", "true")):
                ET.SubElement(properties, key).text = value
            references = ET.SubElement(project, "ItemGroup")
            nts_id = "DuckDB.EFCoreProvider" + (".EF11" if major == 11 else "") + ".NTS"
            ET.SubElement(references, "PackageReference", Include=nts_id,
                          Version=f"[{versions[major, nts_id]}]")
            if wrong_ef:
                ET.SubElement(references, "PackageReference", Include="Microsoft.EntityFrameworkCore.Relational",
                              Version=f"[{ef_versions[wrong_ef]}]")
            ET.ElementTree(project).write(directory / "Consumer.csproj", encoding="unicode")
            run(["dotnet", "restore", "--configfile", str(root / "NuGet.config")], directory, bool(wrong_ef))
            if wrong_ef:
                print(f"{name}: incompatible EF dependency detected.", flush=True)
                continue
            assets = json.loads((directory / "obj/project.assets.json").read_text())
            actual_ef = next(key.split("/")[1] for key in assets["libraries"] if key.startswith("Microsoft.EntityFrameworkCore.Relational/"))
            assert actual_ef == ef_versions[major], (name, actual_ef)
            (directory / "Program.cs").write_text(CONSUMER.replace("EXPECTED_EF_MAJOR", str(major)))
            run(["dotnet", "run", "--no-restore", "--disable-build-servers"], directory)
            print(f"{name}: EF {actual_ef}; native-array insert, read and update passed.", flush=True)


CONSUMER = '''
using DuckDB.EFCoreProvider.Extensions;
using DuckDB.EFCoreProvider.NTS.Extensions;
using DuckDB.NET.Data;
using Microsoft.EntityFrameworkCore;
if (typeof(DbContext).Assembly.GetName().Version!.Major != EXPECTED_EF_MAJOR)
    throw new Exception("Unexpected EF assembly");
_ = typeof(DuckDBNetTopologySuiteDbContextOptionsBuilderExtensions);
using var connection = new DuckDBConnection("Data Source=:memory:");
connection.Open();
using var context = new ConsumerContext(new DbContextOptionsBuilder<ConsumerContext>().UseDuckDB(connection).Options);
context.Database.EnsureCreated();
context.Add(new Row { Id = 1, Values = [1, 2, 3] });
context.SaveChanges();
context.ChangeTracker.Clear();
var row = context.Set<Row>().Single();
if (!row.Values.SequenceEqual(new[] { 1, 2, 3 })) throw new Exception("Array read failed");
row.Values = [4, 5];
context.SaveChanges();
context.ChangeTracker.Clear();
if (!context.Set<Row>().Single().Values.SequenceEqual(new[] { 4, 5 })) throw new Exception("Array update failed");
public sealed class ConsumerContext(DbContextOptions<ConsumerContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
        => builder.Entity<Row>().Property(row => row.Id).ValueGeneratedNever();
}
public sealed class Row { public int Id { get; set; } public int[] Values { get; set; } = []; }
'''

if __name__ == "__main__":
    main()
