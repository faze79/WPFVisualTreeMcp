"""Install and refresh the packaged Codex plugin in an isolated profile (no model calls)."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--codex-cli', type=Path, required=True, help='Path to @openai/codex/bin/codex.js')
    parser.add_argument('--archive', type=Path, help='Release ZIP; otherwise package the current working files')
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    plugin = Path('plugins/wpfvisualtreemcp')
    files = {Path('.agents/plugins/marketplace.json'), Path('LICENSE')}
    files.update(path.relative_to(root) for path in (root / plugin).rglob('*') if path.is_file())
    cli = args.codex_cli.resolve()
    assert cli.is_file(), cli
    work_root = root / 'TestResults'
    work_root.mkdir(exist_ok=True)
    # Retain logs on failure without touching the user's Codex profile or plugin cache.
    work = Path(tempfile.mkdtemp(prefix='plugin-', dir=work_root))
    archive = args.archive.resolve() if args.archive else work / 'plugin.zip'
    if not args.archive:
        with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED) as package:
            for name in sorted(files):
                package.write(root / name, name.as_posix())
    extracted = work / 'extracted'
    with zipfile.ZipFile(archive) as package:
        assert package.testzip() is None, 'Corrupt plugin ZIP'
        names = {name for name in package.namelist() if not name.endswith('/')}
        assert names == {name.as_posix() for name in files}, 'Missing or unexpected plugin files'
        for name in files:
            assert package.read(name.as_posix()) == (root / name).read_bytes(), name
        package.extractall(extracted)
    manifest_path = plugin / '.codex-plugin/plugin.json'
    manifest = json.loads((extracted / manifest_path).read_text(encoding='utf-8'))
    expected_version = manifest['version']
    expected_launch = json.loads((extracted / plugin / '.mcp.json').read_text(encoding='utf-8'))['mcpServers']['wpf-visual-tree']
    assert (extracted / plugin / manifest['skills']).is_dir()

    fixture = work / 'marketplace'
    shutil.copytree(extracted, fixture)
    # Seed an older installed version to exercise cache replacement, including the skill.
    old_manifest = {**manifest, 'version': '0.0.0'}
    (fixture / manifest_path).write_text(json.dumps(old_manifest), encoding='utf-8')
    skill_path = plugin / 'skills/wpfvisualtreemcp/SKILL.md'
    (fixture / skill_path).write_text('---\nname: wpfvisualtreemcp\ndescription: Old test fixture.\n---\nOld instructions.\n', encoding='utf-8')
    environment = {**os.environ, 'CODEX_HOME': str(work / 'profile'), 'GIT_TERMINAL_PROMPT': '0'}
    (work / 'profile').mkdir()
    command_index = 0

    def run(*arguments):
        nonlocal command_index
        command_index += 1
        result = subprocess.run(['node', str(cli), *arguments], cwd=fixture, env=environment,
                                capture_output=True, encoding='utf-8', timeout=120)
        (work / f'{command_index}.stdout').write_text(result.stdout, encoding='utf-8')
        (work / f'{command_index}.stderr').write_text(result.stderr, encoding='utf-8')
        assert result.returncode == 0, (arguments, result.stderr, result.stdout)
        return json.loads(result.stdout)

    def installed_server():
        return next(server for server in run('mcp', 'list', '--json') if server['name'] == 'wpf-visual-tree')

    run('plugin', 'marketplace', 'add', '.', '--json')
    run('plugin', 'add', 'wpfvisualtreemcp@WPFVisualTreeMcp', '--json')
    assert Path(installed_server()['transport']['cwd']).resolve().name == '0.0.0'
    shutil.copytree(extracted, fixture, dirs_exist_ok=True)
    run('plugin', 'add', 'wpfvisualtreemcp@WPFVisualTreeMcp', '--json')
    server = installed_server()
    transport = server['transport']
    installed = Path(transport['cwd']).resolve()
    assert installed.name == expected_version, (installed, expected_version)
    for name in files:
        if name.is_relative_to(plugin):
            assert (installed / name.relative_to(plugin)).read_bytes() == (root / name).read_bytes(), name
    assert transport['command'] == expected_launch['command']
    assert transport['args'] == expected_launch['args']
    assert transport['env'] == expected_launch['env']
    assert server['startup_timeout_sec'] == expected_launch['startup_timeout_sec']
    assert (installed / 'global.json').is_file(), 'SDK selection lost during installation'
    print(f'PASS: plugin {expected_version} ZIP, isolated install, upgrade, complete skill and resolved MCP launch configuration')
    print(f'Logs: {work}')


if __name__ == '__main__':
    main()
