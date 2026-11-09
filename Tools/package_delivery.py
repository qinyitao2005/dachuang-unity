"""Package the Unity source and verified Windows build without Unity caches.

Run from any directory: python Tools/package_delivery.py
Use --check-only to validate inputs and show the selected files without creating ZIPs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zipfile
from datetime import datetime, timezone
from pathlib import Path


PROJECT = Path(__file__).resolve().parent.parent
DELIVERABLES = PROJECT / "Deliverables"
SOURCE_DIRECTORIES = ("Assets", "Packages", "ProjectSettings", "Documentation", "Tools", "操作说明", "Promo")
SOURCE_FILES = ("Open Project.cmd", "Play Demo.cmd", "Play L-C Recording.cmd", "README.md", ".gitignore")
CACHE_NAMES = {"__pycache__", ".pytest_cache", ".mypy_cache"}
WINDOWS_README = """机械臂虚拟实训室 — Windows 演示版

请先完整解压，再双击本文件夹中的 RobotLab.exe。
RobotLab_Data、UnityPlayer.dll 及其他随包文件必须与程序一起保留。
运行演示不需要安装 Unity；修改工程请使用另一个源码包。

进入流程：领取并佩戴安全帽 → 右墙学习与问答 → 靠近工位按 E。
01 循迹：使用原装 Pointer（换）笔形探针（28 个网格），沿印线移动并评分，不留新线。
左侧 J 型手动操作；右侧圆、三角形、正方形、复合曲线自动循迹。
02 套圈：夹住圆环沿钢丝前进。
03 画图：保留真实笔迹，支持自由绘图、示教点和轨迹指令。
三台右上方“示教器 / L・C 编程”使用同一套新控制器流程。
SELECT 打开程序列表，F2 CREATE 新建，接受或输入程序名后按 F1 END / ENTER。
SHIFT + ±X/±Y/±Z 点动；键盘也可 SHIFT 配合 WASD、Q/E。方向键只导航程序。
松开 SHIFT，F1 POINT 选择 L 或 C，ENTER 选择并立即记录当前位置。
SHIFT + F1 重复当前类型：L 记录终点，C 先记录经过点；C 到终点后 SHIFT + F5 补齐。
上下选择程序行；SHIFT + F5 TOUCHUP 修改点位，左右键选择 C 经过点/终点。
SHIFT + FWD 从选中行运行/恢复；STEP 切换单步模式；HOLD 暂停。
FCTN → 1 ABORT → ENTER 终止并保留程序；FCTN → 2 删除末行。
RESET 复位已排除的故障，不终止正常运行或 HOLD。急停解除后 RESET，再 SHIFT + FWD。
指令固定 20 mm/sec，未接入调速；真实进给仍受原工位上限、IK 和碰撞约束。
01 保留首尾 L P[1] 安全点课堂要求、探针评分，不画新线。
02 记录真实圆环中心，不要求纸面安全高度或强制回 P[1]，所有钢丝与碰撞限制仍有效。
03 用 IO/P 切换新录行的 UP/DOWN；UP 不留线但不自动升高工具，DOWN 整段需贴纸。
03 先用 UP 接近纸面，再用 DOWN 示教线段和圆弧。End 输入 1、ENTER 可追加引用 P[1] 的 L，采用当前笔状态。
C 为经过中间点的真实三点圆弧；未完成、共线或越界的程序会拒绝运行并提示原因。
运行/暂停时禁止编辑或手动抢控制。SELECT 重开程序，修改后自动保存，三台分别存在用户数据目录 PendantPrograms/Station01、Station02、Station03。
旧速度程序不会自动改速；固定速度不符时拒绝加载。旧 F1/F2、RESET 停止、DATA 流程仅保留用于历史录制接口。
原有高级轨迹编辑继续可用，与实体示教器互斥执行。

漫游：WASD 移动，鼠标转视角，Tab 释放光标，Shift 快走。
示教器收起时：WASD 平移，Q/E 升降，R 复位训练。
示教器收起时，循迹可用 P 切换探针抬起/贴纸；画图可用 P 切换抬笔/落笔。
空格急停，Esc 退出工位；示教器收起后可用 F1 查看操作指南。
自动循迹在左侧面板启动；自动运行时禁止手动移动。
循迹默认显示无遮挡的纸面俯视图：蓝色为当前参考线，橙色十字为实际探针。
纸面与俯视图都不生成新线，只显示印好的图形与实际探针位置。
可点击收起或展开俯视图，机械臂动作继续在旁边显示。

循迹成功程度 = 覆盖率 × 精度 / 100。
成绩按实际探针触纸轨迹计算，属于教学评分，不代表工业精度认证。
"""


def files_under(directory: Path, *, windows: bool = False) -> list[Path]:
    if not directory.is_dir():
        raise RuntimeError(f"Missing required directory: {directory}")
    selected = []
    for path in sorted(directory.rglob("*")):
        if not path.is_file():
            continue
        relative = path.relative_to(directory)
        if any(part in CACHE_NAMES for part in relative.parts):
            continue
        if path.suffix in {".pyc", ".pyo"} or path.name in {".DS_Store", "Thumbs.db"}:
            continue
        if windows and any("BackUpThisFolder_ButDontShipItWithYourGame" in part for part in relative.parts):
            continue
        if windows and len(relative.parts) == 1 and path.name in {
            "StandaloneSmoke.json", "StandaloneSmoke.png", "StandaloneTracingPaper.png", "StandaloneTracingWorkcell.png"
        }:
            continue
        try:
            path.resolve().relative_to(PROJECT)
        except ValueError as exc:
            raise RuntimeError(f"Refusing file outside the project: {path}") from exc
        selected.append(path)
    return selected


def successful_result(value: object) -> bool:
    if isinstance(value, dict):
        if str(value.get("result", "")).casefold() == "succeeded":
            return True
        return any(successful_result(child) for child in value.values())
    if isinstance(value, list):
        return any(successful_result(child) for child in value)
    return False


def latest_successful_report() -> Path:
    candidates = [PROJECT / "Documentation" / name for name in ("BuildReport.txt", "BuildReport.json")]
    candidates = [path for path in candidates if path.is_file()]
    if not candidates:
        raise RuntimeError("No Documentation/BuildReport.txt or BuildReport.json; build Windows first.")
    report = max(candidates, key=lambda path: path.stat().st_mtime_ns)
    content = report.read_text(encoding="utf-8-sig")
    if report.suffix == ".json":
        success = successful_result(json.loads(content))
    else:
        success = bool(re.search(r"^\s*(?:Result\s*:\s*)?Succeeded\s*$", content, re.IGNORECASE | re.MULTILINE))
    if not success:
        raise RuntimeError(f"Latest build report is not Succeeded: {report}")
    return report


def select_inputs() -> tuple[list[tuple[Path, str]], list[tuple[Path, str]], Path]:
    report = latest_successful_report()
    build = PROJECT / "Builds" / "Windows"
    for required in (build / "RobotLab.exe", build / "UnityPlayer.dll"):
        if not required.is_file() or required.stat().st_size == 0:
            raise RuntimeError(f"Windows build is missing or empty: {required}")
    if not (build / "RobotLab_Data").is_dir():
        raise RuntimeError("Windows build is missing RobotLab_Data.")

    source: list[tuple[Path, str]] = []
    for name in SOURCE_DIRECTORIES:
        source.extend((path, path.relative_to(PROJECT).as_posix()) for path in files_under(PROJECT / name))
    for name in SOURCE_FILES:
        path = PROJECT / name
        if not path.is_file():
            raise RuntimeError(f"Missing required source file: {path}")
        source.append((path, name))
    windows = [(path, path.relative_to(build).as_posix()) for path in files_under(build, windows=True)]
    return sorted(source, key=lambda item: item[1]), windows, report


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def write_archive(name: str, files: list[tuple[Path, str]], readme: str | None = None) -> dict[str, object]:
    target = DELIVERABLES / name
    temporary = target.with_suffix(target.suffix + ".partial")
    entries = {arcname for _, arcname in files}
    if len(entries) != len(files):
        raise RuntimeError(f"Duplicate archive paths in {name}.")
    if readme is not None and "使用说明.txt" in entries:
        raise RuntimeError("Windows build already contains 使用说明.txt; resolve the duplicate before packaging.")
    try:
        with zipfile.ZipFile(temporary, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6, allowZip64=True) as archive:
            for path, arcname in files:
                archive.write(path, arcname)
            if readme is not None:
                archive.writestr("使用说明.txt", readme.encode("utf-8-sig"))
        with zipfile.ZipFile(temporary, "r") as archive:
            bad = archive.testzip()
            if bad is not None:
                raise RuntimeError(f"ZIP integrity check failed: {name}: {bad}")
            count = len(archive.infolist())
            unpacked = sum(info.file_size for info in archive.infolist())
        temporary.replace(target)
    except Exception:
        if temporary.is_file():
            temporary.unlink()
        raise
    return {
        "path": str(target), "entries": count, "bytes": target.stat().st_size,
        "uncompressed_bytes": unpacked, "sha256": sha256(target), "zip_test": "passed",
    }


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-only", action="store_true", help="Validate inputs without creating archives.")
    args = parser.parse_args()
    source, windows, report = select_inputs()
    print(json.dumps({"project": str(PROJECT), "build_report": str(report),
                      "source_entries": len(source), "windows_entries": len(windows) + 1}, ensure_ascii=False), flush=True)
    if args.check_only:
        return
    DELIVERABLES.mkdir(parents=True, exist_ok=True)
    packages = [write_archive("RobotLab_Source.zip", source),
                write_archive("RobotLab_Windows.zip", windows, WINDOWS_README)]
    manifest = {"generated_at_utc": datetime.now(timezone.utc).isoformat(),
                "build_report": report.relative_to(PROJECT).as_posix(), "packages": packages}
    (DELIVERABLES / "PackageManifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2), flush=True)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, zipfile.BadZipFile) as error:
        print(f"Packaging failed: {error}", file=sys.stderr)
        raise SystemExit(1)
