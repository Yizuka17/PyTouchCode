"""Local JSON-lines language worker. Runs inside the interpreter selected by the user.

Never imports or executes the edited program. Jedi is lazy-loaded; AST/keyword
completion and syntax errors remain available without third-party dependencies.
"""
import ast
import builtins
import io
import json
import keyword
import re
import sys
import subprocess
import tokenize


def analyze(request):
    source = request.get("source", "")
    line = max(1, request.get("line", 1))
    column = max(0, request.get("column", 0))
    path = request.get("path") or None
    diagnostics = []
    try:
        tree = ast.parse(source, filename=path or "untitled.py")
    except SyntaxError as exc:
        tree = None
        diagnostics.append({"line": exc.lineno or 1, "column": exc.offset or 1, "message": exc.msg})
    if request.get("action") == "diagnose":
        return {"diagnostics": diagnostics}
    if request.get("action") == "format":
        try:
            result = subprocess.run([sys.executable, "-m", "ruff", "format", "--stdin-filename", path or "untitled.py", "-"], input=source, text=True, capture_output=True, timeout=10, encoding="utf-8")
            if result.returncode == 0:
                return {"formatted": result.stdout, "diagnostics": diagnostics}
        except (OSError, subprocess.TimeoutExpired):
            pass
        # Preserve multiline string contents. Only strip whitespace outside strings.
        protected = set()
        try:
            for tok in tokenize.generate_tokens(io.StringIO(source).readline):
                if tok.type == tokenize.STRING and tok.end[0] > tok.start[0]:
                    protected.update(range(tok.start[0], tok.end[0] + 1))
        except (tokenize.TokenError, IndentationError):
            return {"formatted": source, "diagnostics": diagnostics}
        lines = source.splitlines(keepends=True)
        formatted = "".join(value if i in protected else value.rstrip("\r\n").rstrip(" \t") + ("\n" if value.endswith("\n") else "") for i, value in enumerate(lines, 1))
        return {"formatted": formatted, "diagnostics": diagnostics}
    completions, signatures, doc = [], [], ""
    engine = "offline"
    try:
        import jedi
        script = jedi.Script(code=source, path=path)
        if request.get("action") == "hover":
            definitions = script.infer(line, column)
            doc = "\n\n".join(x.docstring() for x in definitions)[:6000]
        else:
            completions = [{"name": x.name, "insert": x.complete, "kind": x.type} for x in script.complete(line, column)][:120]
            signatures = [x.to_string() for x in script.get_signatures(line, column)][:3]
        engine = "jedi"
    except ImportError:
        current = (source.split("\n") + [""] * line)[line - 1][:column]
        prefix = re.search(r"[\w]*$", current).group()
        names = set(keyword.kwlist) | set(dir(builtins))
        if tree:
            names.update(x.id for x in ast.walk(tree) if isinstance(x, ast.Name))
            names.update(x.name for x in ast.walk(tree) if isinstance(x, (ast.FunctionDef, ast.ClassDef)))
        completions = [{"name": n, "insert": n[len(prefix):], "kind": "name"} for n in sorted(names) if n.startswith(prefix) and n != prefix][:100]
    except (ValueError, TypeError, AttributeError):
        pass  # Incomplete syntax while typing can transiently invalidate a position.
    return {"completions": completions, "signatures": signatures, "doc": doc, "diagnostics": diagnostics, "engine": engine}


def main():
    for line in sys.stdin:
        try:
            request = json.loads(line)
            result = analyze(request)
            result["id"] = request.get("id")
        except Exception as exc:
            result = {"id": locals().get("request", {}).get("id"), "error": str(exc)}
        print(json.dumps(result, ensure_ascii=True), flush=True)


if __name__ == "__main__":
    main()
