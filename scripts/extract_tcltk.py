"""Read an official signed CPython Tcl/Tk MSI as a database and extract files.

Does not invoke Windows Installer installation, change registry or register a
product. MSI actions and scripts are never executed.
"""
import ctypes
from ctypes import wintypes
import pathlib
import shutil
import subprocess
import sys

msi = ctypes.WinDLL("msi")
handle = wintypes.UINT
msi.MsiOpenDatabaseW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR, ctypes.POINTER(handle)]
msi.MsiDatabaseOpenViewW.argtypes = [handle, wintypes.LPCWSTR, ctypes.POINTER(handle)]
msi.MsiViewExecute.argtypes = [handle, handle]
msi.MsiViewFetch.argtypes = [handle, ctypes.POINTER(handle)]
msi.MsiRecordGetStringW.argtypes = [handle, wintypes.UINT, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
msi.MsiRecordReadStream.argtypes = [handle, wintypes.UINT, ctypes.c_void_p, ctypes.POINTER(wintypes.DWORD)]
msi.MsiCloseHandle.argtypes = [handle]


def check(result):
    if result:
        raise OSError(f"MSI read failed: {result}")


def text(record, field):
    size = wintypes.DWORD(32767)
    buffer = ctypes.create_unicode_buffer(size.value)
    check(msi.MsiRecordGetStringW(record, field, buffer, ctypes.byref(size)))
    return buffer.value


def rows(database, query):
    view = handle()
    check(msi.MsiDatabaseOpenViewW(database, query, ctypes.byref(view)))
    try:
        check(msi.MsiViewExecute(view, 0))
        while True:
            record = handle()
            result = msi.MsiViewFetch(view, ctypes.byref(record))
            if result == 259:
                break
            check(result)
            try:
                yield record
            finally:
                msi.MsiCloseHandle(record)
    finally:
        msi.MsiCloseHandle(view)


def extract(source, target, staging):
    database = handle()
    check(msi.MsiOpenDatabaseW(str(source), None, ctypes.byref(database)))
    target.mkdir(parents=True, exist_ok=True)
    staging.mkdir(parents=True, exist_ok=True)
    try:
        directories = {text(r, 1): (text(r, 2), text(r, 3)) for r in rows(database, "SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`")}
        components = {text(r, 1): text(r, 2) for r in rows(database, "SELECT `Component`, `Directory_` FROM `Component`")}
        files = [(text(r, 1), text(r, 2), text(r, 3)) for r in rows(database, "SELECT `File`, `Component_`, `FileName` FROM `File`")]
        cabinets = [text(r, 1).removeprefix("#") for r in rows(database, "SELECT `Cabinet` FROM `Media`")]
        for record in rows(database, "SELECT `Name`, `Data` FROM `_Streams`"):
            name = text(record, 1)
            if name not in cabinets:
                continue
            cabinet = staging / pathlib.Path(name).name
            with cabinet.open("wb") as output:
                while True:
                    buffer = ctypes.create_string_buffer(65536)
                    size = wintypes.DWORD(len(buffer))
                    check(msi.MsiRecordReadStream(record, 2, buffer, ctypes.byref(size)))
                    if not size.value:
                        break
                    output.write(buffer.raw[:size.value])
            subprocess.run(["expand.exe", "-F:*", str(cabinet), str(staging)], check=True, capture_output=True)

        def relative(key):
            if not key:
                return pathlib.Path()
            parent, name = directories[key]
            name = name.split(":")[0].split("|")[-1]
            prefix = relative(parent)
            return prefix if name in (".", "SourceDir") or key in ("TARGETDIR", "INSTALLDIR") else prefix / name

        for identifier, component, filename in files:
            source_file = staging / identifier
            if not source_file.is_file():
                raise FileNotFoundError(source_file)
            destination = (target / relative(components[component]) / filename.split("|")[-1]).resolve()
            if not destination.is_relative_to(target.resolve()):
                raise ValueError("Invalid MSI file path")
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source_file, destination)
        print(f"Extracted {len(files)} Tcl/Tk files without installing a product.")
    finally:
        msi.MsiCloseHandle(database)


if __name__ == "__main__":
    extract(*(pathlib.Path(value).resolve() for value in sys.argv[1:4]))
