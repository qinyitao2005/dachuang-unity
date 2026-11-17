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

场景已增加白色墙板、分区地坪、吊顶照明和教学区饰面。
背景音乐自动循环；点击设置齿轮或按 F2 调节音量，0 为静音，观看教学或演示视频时音乐自动减弱。

问答页底部可点“跳过全部20题，去实操”，立即解锁三台工位。跳过不增加答对题数，界面会明确标记已跳过。

请先完整解压，再双击本文件夹中的 RobotLab.exe。
RobotLab_Data、UnityPlayer.dll 及其他随包文件必须与程序一起保留。
运行演示不需要安装 Unity；修改工程请使用另一个源码包。

进入流程：领取并佩戴安全帽 → 右墙学习与问答 → 靠近工位按 E。
左侧“演示视频”替换原安全帽状态条，实际佩戴与学习前置流程仍保留。
漫游时点击默认打开三工位合辑；在工位中点击默认打开当前工位视频。
视频窗口可切换合辑、01 循迹、02 套圈、03 画图，观看操作、编程与运行过程。
空格或播放按钮切换播放/暂停，“从头播放”重播，Esc 或“返回实操”关闭。
观看时实训运动暂停，当前程序、点位和进度保留；返回后需 SHIFT + FWD 明确继续。
右墙第一部分播放三工位演示合辑；第二部分为 20 道单选题。
题目右下角的正确答案默认遮盖，鼠标移入显示，移开再次遮盖；查看不会代答。
每题答对后点击左下方“下一题”，最后点击“完成问答”开放实操工位。

01 循迹：使用原装笔形探针，沿 J 型、圆形、三角形、正方形、复合曲线的印线运动并评分，不留新线。
02 套圈：夹住圆环沿钢丝前进。
03 画图：依据实际笔尖高度留线，支持手动点动和 L/C 程序复现。
进入任一工位都会自动显示示教器，三台共用以下控制器流程。
SELECT 打开程序列表，F2 CREATE 新建，接受或输入程序名后按 F1 END / ENTER。
SHIFT + ±X/±Y/±Z 点动；键盘也可 SHIFT 配合 WASD、Q/E。方向键只导航程序。
松开 SHIFT，F1 POINT 选择 L 或 C，ENTER 选择并立即记录当前位置。
SHIFT + F1 重复当前类型：L 记录终点，C 先记录经过点；C 到终点后 SHIFT + F5 补齐。
录点只保存当时真实工具的位置，不会录下手动点动时绕行的完整路线。
上下选择程序行；SHIFT + F5 TOUCHUP 修改点位，左右键选择 C 经过点/终点。
SHIFT + FWD 从选中行运行/恢复；STEP 切换单步模式；HOLD 暂停。
光标在 End 时不会启动；运行时松开 SHIFT 会暂停，之后需 SHIFT + FWD 明确继续。
FCTN → 1 ABORT → ENTER 终止并保留程序；FCTN → 2 删除末行。
RESET 复位已排除的故障，不终止正常运行或 HOLD。急停解除后 RESET，再 SHIFT + FWD。
指令固定 20 mm/sec，未接入调速；真实进给仍受原工位上限、IK 和碰撞约束。

01 循迹：程序保留首尾 L P[1] 安全点课堂要求，运行和评分分开控制。
推荐将 P[1] 设在绿色起点正上方 18 mm；先用 STEP 执行首行到安全点。
左侧选图并点击“开始评分”，再关闭 STEP、SHIFT + FWD 继续，也可正常点动完成图形。
实际探针 Z＜10 mm 开始采样；按参考线箭头方向走完整图，或采样后首次抬到 Z＞10 mm 时结算。
Z 恰好等于 10 mm 时保持当前评测状态。“重置评分”只重置评分，不暂停机械臂。
需要暂停用 HOLD；“历史得分”显示本次游戏各图形最高分，重启游戏后清空。
循迹成功程度 = 覆盖率 × 精度 / 100，使用实际探针轨迹，属于教学评分。
纸面俯视图显示参考线和实际探针位置，可收起或展开，不生成新线。

02 记录真实圆环中心，不要求纸面安全高度或强制回 P[1]，所有钢丝与碰撞限制仍有效。
返回原点也须示教合法路径；L P[1] 是直线，不会自动沿钢丝倒退或绕障碍。

03 实际笔尖距纸面 0–10 mm 时留线，高于 10 mm 时移动不留线，无需 IO/P 切换笔状态。
安全点 P[1] 须高于纸面至少 15 mm，首尾使用同一个 L P[1]。
要避免回程连接线，先示教在图形边缘抬高到超过 10 mm 的路径，再返回 P[1]。
“擦除纸上轨迹”清掉手动示教和复现的笔迹，保留程序；运行程序不会自动清纸。
第一台和第二台不使用 IO 落笔；IO 不会把循迹探针强制压到纸面。

C 为经过中间点的真实三点圆弧，中间点不是圆心；未完成、共线或越界的程序会拒绝运行并提示原因。
End 输入 1、ENTER 可追加引用已有 P[1] 的 L。修改点位会影响所有引用该点的指令。
运行/暂停时禁止编辑或手动抢控制。SELECT 重开程序，修改后自动保存，三台分别存在用户数据目录 PendantPrograms/Station01、Station02、Station03。
SELECT 列表中 F3 DELETE 发起删除，F4 YES 确认删除整个程序及点位，F5 NO 或 PREV 取消。
旧速度程序不会自动改速；固定速度不符时拒绝加载。不要把历史录像中的旧 F1/F2、RESET 停止、DATA 菜单与当前步骤混用。

漫游：WASD 移动，鼠标转视角，Tab 释放光标，Shift 快走。
实操：空格急停，Esc 退出工位。观看视频时空格只控制视频，不操作机械臂。
Xbox 手柄和完整步骤见源码包“操作说明”文件夹；20 题的官方参考资料与逐题来源也保存在该文件夹。
本轮同时修正左侧文字裁切，并移除顶部右侧多余提示。
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
