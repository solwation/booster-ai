#!/usr/bin/env python3
"""Render synthetic UX review scenarios; never starts a real CLI or reads user config."""
import concurrent.futures
import json
from pathlib import Path
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
DLL = ROOT / 'src/BoosterAI/bin/Release/net10.0/BoosterAI.dll'
BASE = {'Language': 'sv', 'AppTheme': 'light', 'CheckForUpdates': False,
        'Tabs': [{'Name': 'Atlas', 'Directory': '/sample/atlas', 'Llm': 'claude', 'Model': 'opus', 'Expanded': True,
                  'SubTabs': [{'Name': 'Review', 'Llm': 'codex', 'Model': 'gpt-5-codex'}]},
                 {'Name': 'Harbor', 'Directory': '/sample/harbor', 'Llm': 'agy', 'Model': 'gemini-2.5-pro'}]}
SCENARIOS = {
 'main-sv-light': [],
 'main-empty-sv': [],
 'main-denied-en-dark': ['--fake-denied'],
 'main-min-en-dark': ['--width','900','--height','380'],
 'main-alias-sv': ['--fake-setup','alias'],
 'main-setup-sv': ['--fake-setup'],
 'main-error-sv': ['--fake-status','error:Kunde inte öppna mappen /sample/atlas: åtkomst nekad.'],
 'main-update-min-en-dark': ['--fake-update','0.3.0','--width','900','--height','380'],
 'projects-min-sv': ['--window','settings','--tab','projects','--width','640','--height','400'],
 'general-min-sv': ['--window','settings','--tab','general','--width','640','--height','400'],
 'appearance-min-en-dark': ['--window','settings','--tab','appearance','--width','640','--height','400'],
 'usage-sv': ['--window','settings','--tab','optimization','--section','usage'],
 'limit-min-sv': ['--window','settings','--tab','optimization','--section','limit','--width','640','--height','400'],
 'limit-sv': ['--window','settings','--tab','optimization','--section','limit'],
 'environment-sv': ['--window','settings','--tab','optimization','--section','environment'],
 'compact-global-min-sv': ['--window','settings','--tab','optimization','--section','compact','--width','640','--height','400'],
 'agents-sv': ['--window','settings','--tab','agents'],
 'agent-codex-en-dark': ['--window','settings','--tab','agents','--section','codex'],
 'agent-agy-sv': ['--window','settings','--tab','agents','--section','agy'],
 'agent-aider-sv': ['--window','settings','--tab','agents','--section','aider'],
 'agent-interpreter-sv': ['--window','settings','--tab','agents','--section','interpreter'],
 'agent-openhands-sv': ['--window','settings','--tab','agents','--section','openhands'],
 'compact-manual-sv': ['--window','settings','--tab','agents','--section','codex-compact'],
 'import-min-sv': ['--window','import','--width','520','--height','320'],
 'restart-sv': ['--window','restart'],
 'install-sv': ['--window','install'],
 'close-en-dark': ['--window','close'],
 'wsl-sv': ['--window','wsl'],
 'alias-sv': ['--window','alias'],
 'yolo-en-dark': ['--window','agent-yolo'],
}

def render(item):
    name, args = item
    config = BASE | ({'Language':'en','AppTheme':'dark'} if 'en-dark' in name else {})
    if name == 'main-empty-sv': config = config | {'Tabs': []}
    if name == 'compact-manual-sv': config = config | {'AutoCompactMode':'manual'}
    cfg = HERE / (name + '.json')
    cfg.write_text(json.dumps(config, indent=2) + '\n')
    try:
        subprocess.run(['dotnet', str(DLL), '--config', str(cfg), '--screenshot', str(HERE / (name + '.png')), *args], cwd=ROOT, check=True, timeout=45, capture_output=True)
    finally:
        cfg.unlink(missing_ok=True)
    return name

if __name__ == '__main__':
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        for name in pool.map(render, SCENARIOS.items()): print(name, flush=True)
