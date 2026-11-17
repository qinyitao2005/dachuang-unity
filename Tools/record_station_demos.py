"""Record the current Windows player through real shared-pendant controls, then encode its rendered frames.

Example:
  python Tools/record_station_demos.py --player Builds/Windows/RobotLab.exe --output D:/RobotLab_Demos_20261117
  python Tools/record_station_demos.py --output D:/RobotLab_Demos_20261117 --encode-only

The dedicated desktop keeps the user's desktop untouched. No pre-existing program files or frames are overwritten.
"""
from __future__ import annotations
import argparse
import ctypes as c
from ctypes import wintypes as w
import json
import os
from pathlib import Path
import shutil
import subprocess
import time


def run_player(player: Path, output: Path, station: int, timeout: int, photo_only: bool = False) -> None:
    if os.name != "nt":
        raise RuntimeError("Capture requires the Windows Unity player.")
    user = c.WinDLL("user32", use_last_error=True)
    kernel = c.WinDLL("kernel32", use_last_error=True)
    user.CreateDesktopW.argtypes = [w.LPCWSTR, w.LPCWSTR, w.LPVOID, w.DWORD, w.DWORD, w.LPVOID]
    user.CreateDesktopW.restype = w.HANDLE
    user.CloseDesktop.argtypes = [w.HANDLE]

    class Startup(c.Structure):
        _fields_ = [("cb", w.DWORD), ("lpReserved", w.LPWSTR), ("lpDesktop", w.LPWSTR), ("lpTitle", w.LPWSTR),
                    ("dwX", w.DWORD), ("dwY", w.DWORD), ("dwXSize", w.DWORD), ("dwYSize", w.DWORD),
                    ("dwXCountChars", w.DWORD), ("dwYCountChars", w.DWORD), ("dwFillAttribute", w.DWORD),
                    ("dwFlags", w.DWORD), ("wShowWindow", w.WORD), ("cbReserved2", w.WORD),
                    ("lpReserved2", w.LPVOID), ("hStdInput", w.HANDLE), ("hStdOutput", w.HANDLE), ("hStdError", w.HANDLE)]

    class Process(c.Structure):
        _fields_ = [("process", w.HANDLE), ("thread", w.HANDLE), ("pid", w.DWORD), ("tid", w.DWORD)]

    name = f"RobotLabDemo_{os.getpid()}_{station}"
    desktop = user.CreateDesktopW(name, None, None, 0, 0x10000000, None)
    if not desktop:
        raise c.WinError(c.get_last_error())
    startup = Startup()
    startup.cb = c.sizeof(startup)
    startup.lpDesktop = name
    startup.dwFlags = 1
    startup.wShowWindow = 1
    process = Process()
    command = [str(player), "--station-demo-capture", "-demo-station", str(station), "-demo-output", str(output),
               "-window-mode", "borderless", "-screen-width", "1600", "-screen-height", "900", "-screen-fullscreen", "0",
               "-force-d3d11-bltblt-mode", "-logFile", str(output / f"Station0{station}.log")]
    if photo_only:
        command.append("--demo-photo-only")
    kernel.CreateProcessW.argtypes = [w.LPCWSTR, w.LPWSTR, w.LPVOID, w.LPVOID, w.BOOL, w.DWORD,
                                      w.LPVOID, w.LPCWSTR, c.POINTER(Startup), c.POINTER(Process)]
    kernel.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
    kernel.GetExitCodeProcess.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
    kernel.TerminateProcess.argtypes = [w.HANDLE, w.UINT]
    kernel.CloseHandle.argtypes = [w.HANDLE]
    try:
        if not kernel.CreateProcessW(str(player), c.create_unicode_buffer(subprocess.list2cmdline(command)), None, None,
                                     False, 0, None, str(player.parent), c.byref(startup), c.byref(process)):
            raise c.WinError(c.get_last_error())
        print(f"Station {station}: recording actual player PID {process.pid}", flush=True)
        started = time.monotonic()
        while kernel.WaitForSingleObject(process.process, 1000) == 258:
            if time.monotonic() - started > timeout:
                kernel.TerminateProcess(process.process, 1)
                raise TimeoutError(f"Station {station} recording exceeded {timeout}s.")
        code = w.DWORD()
        kernel.GetExitCodeProcess(process.process, c.byref(code))
        if code.value:
            raise RuntimeError(f"Station {station} player exited {code.value}; inspect its log and CaptureReport.json.")
    finally:
        if process.thread:
            kernel.CloseHandle(process.thread)
        if process.process:
            kernel.CloseHandle(process.process)
        user.CloseDesktop(desktop)


def encode(output: Path, stations: list[int]) -> None:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("ffmpeg is required on PATH.")
    for station in stations:
        root = output / f"Station0{station}"
        report = json.loads((root / "CaptureReport.json").read_text(encoding="utf-8-sig"))
        if not report["passed"]:
            raise RuntimeError(f"Station {station} failed capture: {report.get('exception')}")
        frames = list((root / "frames").glob("*.jpg"))
        if len(frames) != report["frames"]:
            raise RuntimeError(f"Station {station} frame count differs from capture evidence.")
        subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "warning", "-y", "-framerate", str(report["fps"]),
                        "-i", str(root / "frames" / "%06d.jpg"), "-vf",
                        "fps=30,scale=in_range=pc:out_range=tv:in_color_matrix=bt601:out_color_matrix=bt709,format=yuv420p", "-c:v", "libx264",
                        "-preset", "medium", "-crf", "22", "-profile:v", "baseline", "-level:v", "4.0", "-bf", "0",
                        "-color_primaries", "bt709", "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv",
                        "-bsf:v", "h264_metadata=colour_primaries=1:transfer_characteristics=1:matrix_coefficients=1:video_full_range_flag=0",
                        "-movflags", "+faststart", "-an",
                        str(output / f"LabDemoStation0{station}.mp4")], check=True)
        print(f"Station {station}: encoded {len(frames) / report['fps']:.1f}s", flush=True)
    if all((output / f"LabDemoStation0{i}.mp4").is_file() for i in (1, 2, 3)):
        manifest = output / "concat.txt"
        manifest.write_text("".join(f"file 'LabDemoStation0{i}.mp4'\n" for i in (1, 2, 3)), encoding="utf-8")
        subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "warning", "-y", "-f", "concat", "-safe", "0",
                        "-i", str(manifest), "-c", "copy", "-movflags", "+faststart", str(output / "TrainingLesson.mp4")], check=True)
        print(f"Combined wall lesson: {output / 'TrainingLesson.mp4'}", flush=True)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=Path, default=Path("Builds/Windows/RobotLab.exe"))
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--stations", type=int, nargs="+", default=[1, 2, 3], choices=[1, 2, 3])
    parser.add_argument("--timeout", type=int, default=1800)
    parser.add_argument("--encode-only", action="store_true")
    parser.add_argument("--capture-only", action="store_true")
    parser.add_argument("--photo-only", action="store_true", help="Render the isolated original arm and exit; use --stations 3.")
    args = parser.parse_args()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    if not args.encode_only:
        player = args.player.resolve(strict=True)
        for station in args.stations:
            frame_dir = output / f"Station0{station}" / "frames"
            if frame_dir.exists() and any(frame_dir.glob("*.jpg")):
                raise FileExistsError(f"Use a fresh output folder for a new take: {frame_dir}")
            run_player(player, output, station, args.timeout, args.photo_only)
    if not args.capture_only and not args.photo_only:
        encode(output, args.stations)


if __name__ == "__main__":
    main()
