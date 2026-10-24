"""Install the actual Automation nupkg in an isolated C# consumer and exercise it live.

Run on Windows with .NET 8 (including the x86 runtime for cross-bitness injection).
--gestures additionally requires an unlocked interactive desktop.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import uuid
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
import zipfile


ROOT = Path(__file__).resolve().parent.parent


def run(command, name, work, env):
    print(f"Running {name}...", flush=True)
    log = work / f"{name}.log"
    with log.open("w", encoding="utf-8") as output:
        result = subprocess.run([str(arg) for arg in command], cwd=work, env=env,
                                stdout=output, stderr=subprocess.STDOUT, timeout=300)
    if result.returncode:
        raise RuntimeError(f"{name} failed; {log}\n{log.read_text(encoding='utf-8', errors='replace')[-12000:]}")
    if name.startswith(("smoke-", "replay-")):
        print(log.read_text(encoding="utf-8", errors="replace"), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--packages", required=True, type=Path)
    parser.add_argument("--gestures", action="store_true")
    args = parser.parse_args()
    packages = args.packages.resolve()
    candidates = list(packages.glob("WpfVisualTreeMcp.Automation.*.nupkg"))
    if len(candidates) != 1:
        raise ValueError("Use a package directory containing exactly one Automation .nupkg")
    with zipfile.ZipFile(candidates[0]) as package:
        names = package.namelist()
        for assembly in ("Automation", "Shared", "Injector"):
            assert f"lib/net8.0/WpfVisualTreeMcp.{assembly}.dll" in names, assembly
        assert "buildTransitive/WpfVisualTreeMcp.Automation.targets" in names
        for architecture in ("x64", "x86"):
            for runtime_directory in ("", "coreclr/"):
                assert f"payload/native/{architecture}/{runtime_directory}WpfVisualTreeMcp.Inspector.dll" in names
            assert f"payload/native/{architecture}/WpfInspectorBootstrapper.dll" in names
        assert "payload/native/x86/WpfInjectorHelper.exe" in names
        assert not any("Server.dll" in name or "ModelContextProtocol" in name for name in names)
        manifest = ET.fromstring(package.read("WpfVisualTreeMcp.Automation.nuspec"))
        version = manifest.find(".//{*}version").text
        dependencies = {item.attrib["id"] for item in manifest.findall(".//{*}dependency")}
        assert dependencies == {"Microsoft.Extensions.Logging.Abstractions"}, dependencies

    work = ROOT / "TestResults" / f"sdk-package-{uuid.uuid4().hex}"
    work.mkdir(parents=True)
    print(f"Artifacts: {work}", flush=True)
    env = os.environ.copy()
    env["NUGET_PACKAGES"] = str(work / "nuget-cache")
    (work / "NuGet.Config").write_text(f"""<configuration><packageSources><clear />
      <add key="local-sdk" value="{escape(str(packages), {'"': '&quot;'})}" />
      <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
      </packageSources></configuration>""", encoding="utf-8")
    consumer = work / "consumer"
    consumer.mkdir()
    project = consumer / "PackageConsumer.csproj"
    project.write_text(f"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
      <OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework>
      <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
      </PropertyGroup><ItemGroup>
      <PackageReference Include="WpfVisualTreeMcp.Automation" Version="{version}" />
      </ItemGroup></Project>""", encoding="utf-8")
    shutil.copy2(ROOT / "scripts" / "AutomationPackageSmoke.cs", consumer / "Program.cs")
    run(["dotnet", "build", project, "-c", "Release", "--verbosity", "quiet"], "consumer-build", work, env)
    output = work / "published-consumer"
    run(["dotnet", "publish", project, "-c", "Release", "--no-build", "-o", output, "--verbosity", "quiet"], "consumer-publish", work, env)
    for folder in (consumer / "bin" / "Release" / "net8.0-windows", output):
        for name in names:
            if name.startswith("payload/native/"):
                assert (folder / name.removeprefix("payload/")).is_file(), (folder, name)

    for framework, architecture in (("net8.0-windows", "x64"), ("net472", "x86")):
        case = f"{framework}-{architecture}"
        source = work / f"source-{case}"
        source.mkdir()
        for file in (ROOT / "samples" / "SampleWpfApp").iterdir():
            if file.suffix in (".cs", ".xaml", ".csproj"):
                shutil.copy2(file, source / file.name)
        sample = work / f"sample-{case}"
        command = ["dotnet", "publish", source / "SampleWpfApp.csproj", "-c", "Release",
                   f"-p:SampleTargetFramework={framework}", f"-p:PlatformTarget={architecture}",
                   "-p:EnableInspectorSelfHosting=false", "-o", sample, "--verbosity", "quiet"]
        if framework == "net8.0-windows":
            command += ["--runtime", f"win-{architecture}", "--self-contained", "false"]
        run(command, f"sample-build-{case}", work, env)
        executable = sample / "SampleWpfApp.exe"
        exported = work / f"exported-{case}"
        command = [output / "PackageConsumer.exe", executable, exported]
        if args.gestures:
            command += ["--gestures"]
        run(command, f"smoke-{case}", work, env)
        replay_env = dict(env, WPF_TEST_APP=str(executable))
        run(["dotnet", "test", exported / "RecordedWorkflow.Tests.csproj", "-c", "Release", "--verbosity", "normal"],
            f"replay-{case}", work, replay_env)
    print(f"PASS: standalone NuGet consumer, exact waits, inspection, actions, export/replay. Artifacts: {work}", flush=True)


if __name__ == "__main__":
    main()
