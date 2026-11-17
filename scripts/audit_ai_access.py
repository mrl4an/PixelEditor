import subprocess, json, os, sys, tempfile, shutil
"""Проверка доступа ИИ ко всем инструментам: запускает pixed-mcp.exe и pixed.exe и вызывает каждую операцию, проверку, шаблон и MCP-инструмент.
Запуск: python scripts/audit_ai_access.py [папка_с_exe]  (по умолчанию Debug-сборка из репозитория). Код выхода 1, если что-то не прошло."""
import sys as _sys
W = os.path.abspath(os.path.join(os.path.dirname(__file__), "..")).replace("\\", "/") + "/"
BIN = _sys.argv[1].replace("\\", "/").rstrip("/") + "/" if len(_sys.argv) > 1 else None
MCP = (BIN + "pixed-mcp.exe") if BIN else W + "src/PixelEditor.Mcp/bin/Debug/net10.0-windows/pixed-mcp.exe"
CLI = (BIN + "pixed.exe") if BIN else W + "src/PixelEditor.Cli/bin/Debug/net10.0-windows/pixed.exe"
FAILED = 0
IMG = W + "examples/web/the_fox_tuned.png"
OUT = tempfile.mkdtemp(prefix="mcp-audit-")
# The audit saves palettes and templates: keep them (and the log) out of the real %AppData%\PixelEditor. CLI and MCP honor PIXED_DATA_DIR; the folder is deleted at the end.
DATA = tempfile.mkdtemp(prefix="pixed-audit-data-")
os.environ["PIXED_DATA_DIR"] = DATA
import atexit
atexit.register(lambda: shutil.rmtree(DATA, ignore_errors=True))

p = subprocess.Popen([MCP], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, encoding="utf-8", bufsize=1)
_id = 0
def rpc(method, params=None):
    global _id
    _id += 1
    p.stdin.write(json.dumps({"jsonrpc": "2.0", "id": _id, "method": method, "params": params or {}}) + "\n"); p.stdin.flush()
    while True:
        line = p.stdout.readline()
        if not line: raise RuntimeError("MCP closed")
        m = json.loads(line)
        if m.get("id") == _id: return m
def call(name, args):
    r = rpc("tools/call", {"name": name, "arguments": args})
    if "error" in r: return False, r["error"]
    res = r["result"]
    text = next((c["text"] for c in res.get("content", []) if c.get("type") == "text"), "")
    return not res.get("isError", False), text

rpc("initialize", {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "audit", "version": "1"}})
tools = [t["name"] for t in rpc("tools/list")["result"]["tools"]]
print("MCP tools:", len(tools)); print(" ", ", ".join(tools))

# 1) operations: the list matches the CLI and each one is called through apply_operation
ok, t = call("list_operations", {"names_only": True})
mcp_ops = json.loads(t)
cli_ops = json.loads(subprocess.run([CLI, "ops", "--names"], capture_output=True, text=True, encoding="utf-8").stdout)
print("ops MCP/CLI:", len(mcp_ops), len(cli_ops), "равны:", mcp_ops == cli_ops)

SAMPLES = {
 "resize": {"width": 32}, "alphaThreshold": {}, "quantize": {"colors": 8}, "applyPalette": {"paletteName": "PICO-8"}, "removeIslands": {}, "fillHoles": {},
 "removeBackground": {}, "adjust": {"contrast": 10}, "polygonMask": {"points": [[5, 5], [60, 5], [60, 40], [5, 40]]}, "edgeShade": {}, "cutout": {},
 "removeOutline": {}, "cleanEdges": {}, "mergeRareColors": {}, "simplifyColorRegions": {}, "applySpriteStyle": None, "patchPixels": {"edits": [[1, 1, "#FF0000"]]},
 "replaceColor": {"from": "#000000", "to": "#FFFFFF"}, "outline": {}, "pad": {}, "trim": {}, "shift": {"dx": 1}, "resizeCanvas": {"width": 80, "height": 60}, "rotate": {"angle": 20},
 "flipH": {}, "flipV": {}, "rotate90": {}, "invert": {}, "grayscale": {}, "hueShift": {}, "tint": {}, "posterize": {}, "threshold": {}, "autoLevels": {"cutoff": 1}, "median": {}, "unsharp": {}, "edges": {}, "swapColors": {"a": "#000000", "b": "#FFFFFF"},
 "background": {}, "crop": {"width": 30, "height": 30}, "mirror": {}, "tile": {}, "grow": {}, "shrink": {}, "dropShadow": {}, "smooth": {}, "pixelate": {}, "despeckle": {},
}
bad = []
for op in mcp_ops:
    if op not in SAMPLES: bad.append((op, "нет примера в аудите")); continue
    if SAMPLES[op] is None: continue
    ok, t = call("apply_operation", {"op": op, "input": IMG, "output": f"{OUT}/{op}.png", "args": SAMPLES[op]})
    if not ok or not os.path.exists(f"{OUT}/{op}.png"): bad.append((op, t[:120]))
FAILED += len(bad)
print("apply_operation: вызвано", sum(1 for o in mcp_ops if SAMPLES.get(o) is not None), "ошибок:", len(bad))
for b in bad: print("   ", b)

# 2) checks: all of them through MCP
ok, t = call("list_checks", {"names_only": True}); mcp_checks = json.loads(t)
cli_checks = json.loads(subprocess.run([CLI, "checks", "--names"], capture_output=True, text=True, encoding="utf-8").stdout)
print("checks MCP/CLI:", len(mcp_checks), len(cli_checks), mcp_checks == cli_checks)
CHECK_ARGS = {"maxColors": {"value": 40}, "minColors": {"value": 1}, "size": {"maxWidth": 200}, "opaquePixels": {"min": 1}, "noIslands": {}, "noHoles": {}, "noRareColors": {}, "onlyPalette": {"paletteName": "PICO-8"}, "binaryAlpha": {}, "trimmed": {}, "connected": {}}
cbad = []
for c in mcp_checks:
    step = dict(CHECK_ARGS.get(c, {})); step["check"] = c
    pipe = json.dumps({"name": "c", "steps": [], "checks": [dict(step, required=False)]})
    ok, t = call("check_image", {"pipeline": pipe, "image": IMG})
    if not ok: cbad.append((c, t[:120]))
FAILED += len(cbad)
print("check_image: проверок", len(mcp_checks), "ошибок:", len(cbad)); [print("   ", b) for b in cbad]

# 3) templates: list, run by name, validation
ok, t = call("list_templates", {}); templates = json.loads(t)
print("templates:", len(templates))
tbad = []
for tpl in templates:
    name = tpl["Name"]
    out = f"{OUT}/tpl_{abs(hash(name))}.png"
    ok, txt = call("run_pipeline", {"pipeline": name, "input": IMG, "output": out})
    ok2, v = call("validate_pipeline", {"pipeline": name})
    if not os.path.exists(out) or not ok2: tbad.append((name, txt[:100], v[:100]))
    # the same templates through the CLI by name
    r = subprocess.run([CLI, "run", name, IMG, out + ".cli.png"], capture_output=True, text=True, encoding="utf-8")
    if r.returncode not in (0, 2): tbad.append((name, "CLI exit " + str(r.returncode), r.stdout[:80]))
# template parameters: an override through MCP "params" and through the CLI key=value, and a bad one is rejected
pt = next((t for t in templates if t.get("Params")), None)
if pt:
    pname = pt["Params"][0]["Name"]; pval = pt["Params"][0]["Default"]
    ok, txt = call("run_pipeline", {"pipeline": pt["Name"], "input": IMG, "output": f"{OUT}/params_mcp.png", "params": {pname: pval}})
    if not ok: tbad.append((pt["Name"], "MCP params", txt[:100]))
    r = subprocess.run([CLI, "run", pt["Name"], IMG, f"{OUT}/params_cli.png", f"{pname}={pval}"], capture_output=True, text=True, encoding="utf-8")
    if r.returncode not in (0, 2): tbad.append((pt["Name"], "CLI params exit " + str(r.returncode), r.stderr[:80]))
    r = subprocess.run([CLI, "run", pt["Name"], IMG, f"{OUT}/params_bad.png", "nosuchparam=1"], capture_output=True, text=True, encoding="utf-8")
    if r.returncode != 1: tbad.append((pt["Name"], "an unknown parameter must be an error", str(r.returncode)))
else:
    tbad.append(("templates", "no template reports Params", ""))
FAILED += len(tbad)
print("шаблоны: запуск MCP+CLI по имени, ошибок:", len(tbad)); [print("   ", b) for b in tbad]

# 4) palettes, saving, the other tools
ok, t = call("list_palettes", {}); pals = json.loads(t); print("palettes:", len(pals))
ok, t = call("save_palette", {"name": "аудит-" + os.path.basename(OUT)[-6:], "colors": ["#000000", "#FFFFFF"]}); print("save_palette:", ok)
ok, t = call("image_info", {"path": IMG}); print("image_info:", ok)
ok, t = call("view_image", {"path": IMG, "max_side": 64}); print("view_image:", ok)
ok, t = call("draw_scene", {"scene": json.dumps({"size": [16, 16], "items": [{"rect": [2, 2, 8, 8], "color": "#FF0000"}]}), "output": f"{OUT}/scene.png"}); print("draw_scene:", ok)
sheet = {"frameSize": [8, 8], "palette": {"a": "#FF0000"}, "rows": [{"name": "E", "frames": [{"name": "stand", "items": [{"rect": [1, 1, 5, 5], "color": "a"}]}, {"name": "step", "items": [{"rect": [1, 2, 5, 4], "color": "a"}]}]}, {"name": "W", "mirrorOf": "E"}]}
ok, t = call("draw_sheet", {"sheet": json.dumps(sheet), "output": f"{OUT}/sheet.png"}); print("draw_sheet:", ok); FAILED += 0 if ok else 1
for kind in ("image", "sprite", "sheet"):
    r = subprocess.run([CLI, "chat-prompt", "--kind", kind], capture_output=True, text=True, encoding="utf-8"); print("chat-prompt", kind, r.returncode == 0 and len(r.stdout) > 1000); FAILED += 0 if r.returncode == 0 and len(r.stdout) > 1000 else 1
ok, t = call("slice_sheet", {"sheet": f"{OUT}/scene.png", "cell_width": 8, "cell_height": 8, "output_dir": f"{OUT}/cells"}); print("slice_sheet:", ok)
cells = sorted(os.listdir(f"{OUT}/cells")) if os.path.isdir(f"{OUT}/cells") else []
ok, t = call("compose_sheet", {"inputs": [f"{OUT}/cells/{c}" for c in cells[:2]], "columns": 2, "output": f"{OUT}/sheet.png"}); print("compose_sheet:", ok)
ok, t = call("make_gif", {"output": f"{OUT}/a.gif", "delay_ms": 100, "frames": [f"{OUT}/cells/{c}" for c in cells[:2]]}); print("make_gif:", ok)
ok, t = call("export_animation", {"sheet": f"{OUT}/sheet.png", "frame_width": 8, "frame_height": 8, "output_dir": f"{OUT}/anim", "name": "t"}); print("export_animation:", ok, t[:80] if not ok else "")
ok, t = call("compare_pipelines", {"input": IMG, "pipelines": ["Clean up sprite", "Limit to 16 colors"]}); print("compare_pipelines:", ok, t[:80] if not ok else "")
ok, t = call("inspect_pixels", {"path": IMG, "x": 0, "y": 0, "width": 4, "height": 4}); print("inspect_pixels:", ok, t[:80] if not ok else "")
ok, t = call("build_shared_palette", {"paths": [IMG], "colors": 8}); print("build_shared_palette:", ok, t[:80] if not ok else "")
ok, t = call("extract_style", {"reference": IMG}); print("extract_style:", ok, t[:80] if not ok else "")
ok, t = call("batch_process", {"pipeline": "Clean up sprite", "input_dir": W + "examples/animals", "output_dir": f"{OUT}/batch"}); print("batch_process:", ok, t[:80] if not ok else "")
ok, t = call("read_log", {"level": "info", "tail": 3}); print("read_log:", ok, t[:60] if not ok else ""); FAILED += 0 if ok else 1
ok, t = call("save_template", {"name": "audit-" + os.path.basename(OUT)[-6:], "description": "t", "pipeline": json.dumps({"steps": [{"op": "trim"}], "checks": [{"check": "trimmed"}]})}); print("save_template:", ok, t[:80] if not ok else "")
# RSI: the four MCP tools and the same flow through the CLI
def check(label, cond, detail=""):
    global FAILED
    print(label + ":", cond, detail if not cond else "")
    if not cond: FAILED += 1
rsi_sheet = {"frameSize": [8, 8], "palette": {"a": "#FF0000"}, "rows": [
    {"name": "S", "frames": [{"items": [{"rect": [1, 1, 5, 5], "color": "a"}]}, {"items": [{"rect": [1, 2, 5, 4], "color": "a"}]}]},
    {"name": "N", "copyOf": "S"},
    {"name": "E", "frames": [{"items": [{"rect": [2, 1, 3, 5], "color": "a"}]}, {"items": [{"rect": [2, 2, 3, 4], "color": "a"}]}]},
    {"name": "W", "mirrorOf": "E"}]}
ok, t = call("draw_sheet", {"sheet": json.dumps(rsi_sheet), "output": f"{OUT}/rsi_sheet.png"}); check("draw_sheet (rsi sheet)", ok, t[:80])
ok, t = call("rsi_new", {"folder": f"{OUT}/mcp.rsi", "width": 8, "height": 8}); check("rsi_new", ok, t[:80])
ok, t = call("rsi_new", {"folder": f"{OUT}/mcp.rsi", "width": 8, "height": 8}); check("rsi_new refuses to overwrite", not ok)
ok, t = call("rsi_set_state", {"folder": f"{OUT}/mcp.rsi", "name": "walk", "sheet": f"{OUT}/rsi_sheet.png", "directions": 4, "delay": 0.2}); check("rsi_set_state", ok, t[:80])
ok, t = call("rsi_set_state", {"folder": f"{OUT}/mcp.rsi", "name": "bad", "sheet": f"{OUT}/rsi_sheet.png", "directions": 1}); check("rsi_set_state rejects a wrong size", not ok)
ok, t = call("rsi_info", {"folder": f"{OUT}/mcp.rsi"}); check("rsi_info", ok and json.loads(t)["States"][0]["SizeConsistent"] is True, t[:80])
ok, t = call("rsi_export_state", {"folder": f"{OUT}/mcp.rsi", "state": "walk", "output": f"{OUT}/rsi_out.png"}); check("rsi_export_state", ok and os.path.exists(f"{OUT}/rsi_out.png"), t[:80])
def cli(*a, stdin=None):
    return subprocess.run([CLI, *a], capture_output=True, text=True, encoding="utf-8", input=stdin)
r = cli("rsi", "new", f"{OUT}/cli.rsi", "8", "8", "license=CC0-1.0"); check("pixed rsi new", r.returncode == 0, r.stderr[:80])
r = cli("rsi", "state", f"{OUT}/cli.rsi", "walk", f"{OUT}/rsi_sheet.png", "directions=4", "delays=0.1,0.3"); check("pixed rsi state", r.returncode == 0, r.stderr[:80])
r = cli("rsi", "info", f"{OUT}/cli.rsi"); check("pixed rsi info", r.returncode == 0 and json.loads(r.stdout)["States"][0]["Frames"] == 2, r.stderr[:80])
r = cli("rsi", "export", f"{OUT}/cli.rsi", "walk", f"{OUT}/cli_out.png"); check("pixed rsi export", r.returncode == 0 and os.path.exists(f"{OUT}/cli_out.png"), r.stderr[:80])
# @file, stdin and globs in the CLI
with open(f"{OUT}/sheet.json", "w", encoding="utf-8-sig") as f: json.dump(rsi_sheet, f)
r = cli("draw-sheet", "@" + f"{OUT}/sheet.json", f"{OUT}/at.png"); check("pixed draw-sheet @file (BOM)", r.returncode == 0, r.stderr[:80])
r = cli("draw-sheet", "-", f"{OUT}/stdin.png", stdin=json.dumps(rsi_sheet)); check("pixed draw-sheet - (stdin)", r.returncode == 0, r.stderr[:80])
r = cli("draw-sheet", "@" + f"{OUT}/missing.json", f"{OUT}/x.png"); check("pixed @missing file is an error", r.returncode == 1 and "Error" in r.stderr)
pipe = json.dumps({"steps": [{"op": "trim"}], "checks": [{"check": "trimmed"}]})
with open(f"{OUT}/pipe.json", "w", encoding="utf-8") as f: f.write(pipe)
r = cli("run", "@" + f"{OUT}/pipe.json", f"{OUT}/cli.rsi/walk.png", f"{OUT}/run_at.png"); check("pixed run @file", r.returncode in (0, 2) and os.path.exists(f"{OUT}/run_at.png"), r.stderr[:80])
r = cli("validate", "-", stdin=pipe); check("pixed validate - (stdin)", r.returncode == 0, r.stderr[:80])
r = cli("sheet", "2", f"{OUT}/globbed.png", f"{OUT}/cells/cell_0*.png"); check("pixed sheet with a glob", r.returncode == 0 and os.path.exists(f"{OUT}/globbed.png"), r.stderr[:80])
r = cli("sheet", "2", f"{OUT}/globbed.png", f"{OUT}/cells/zzz*.png"); check("pixed glob without matches is an error", r.returncode == 1)
p.terminate()
shutil.rmtree(OUT, ignore_errors=True)
shutil.rmtree(DATA, ignore_errors=True)
print("ИТОГ:", "всё доступно" if FAILED == 0 else f"ошибок: {FAILED}")
sys.exit(1 if FAILED else 0)
