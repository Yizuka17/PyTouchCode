"""Standard serial REPL transport; raw execution/file operations use mpremote."""
import json
import sys
import threading
import serial

port = serial.Serial(sys.argv[1], 115200, timeout=0.15, write_timeout=2)
stopped = threading.Event()


def receive():
    try:
        while not stopped.is_set():
            value = port.read(port.in_waiting or 1)
            if value:
                sys.stdout.buffer.write(value)
                sys.stdout.buffer.flush()
    except serial.SerialException as exc:
        print(str(exc), file=sys.stderr, flush=True)
        stopped.set()


thread = threading.Thread(target=receive, daemon=True)
thread.start()
try:
    for line in sys.stdin:
        request = json.loads(line)
        value = request.get("text", "").encode("utf-8")
        if "control" in request:
            value = bytes([int(request["control"])])
        port.write(value)
finally:
    stopped.set()
    thread.join(timeout=1)
    port.close()
