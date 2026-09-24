# Builds a throwaway repository under %TEMP%/subverted-ctx-capture and captures, per case, both
# sides of a local edit and of a committed one, with svn's own diff of each — raw bytes throughout.
#   python capture.py        (writes beside itself; needs svn and svnadmin on PATH)
import os, shutil, sqlite3, subprocess

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(os.environ.get("TEMP", "/tmp"), "subverted-ctx-capture")
REPO, WC = os.path.join(BASE, "repo"), os.path.join(BASE, "wc")
ENV = dict(os.environ, LC_ALL="C")
URL = "file:///" + REPO.replace("\\", "/").lstrip("/")


def lines(n, eol=b"\n"):
    return b"".join(b"line %d%s" % (i, eol) for i in range(1, n + 1))


def sub(old, new):
    return lambda text: text.replace(old, new, 1)


# name: (base, r2 edit, local edit, properties)
CASES = {
    "plain.txt": (lines(40), lambda t: sub(b"line 30\n", b"line thirty\n")(sub(b"line 5\n", b"line five\n")(t)), lambda t: sub(b"line 38\n", b"line thirty-eight\n")(sub(b"line five\n", b"line 5 again\n")(t)), {}),
    "gap5.txt": (lines(40), sub(b"line 10\n", b"line ten\n"), lambda t: sub(b"line 17\n", b"line seventeen\n")(sub(b"line 11\n", b"line eleven\n")(t)), {}),
    "gap6.txt": (lines(40), sub(b"line 10\n", b"line ten\n"), lambda t: sub(b"line 18\n", b"line eighteen\n")(sub(b"line 11\n", b"line eleven\n")(t)), {}),
    "crlf-noprop.txt": (lines(12, b"\r\n"), sub(b"line 6\r\n", b"line six\r\n"), sub(b"line 7\r\n", b"line seven\r\n"), {}),
    "mixed-noprop.txt": (lines(12), sub(b"line 6\n", b"line six\r\n"), sub(b"line 3\n", b"line three\r\n"), {}),
    "lone-cr.txt": (b"a\rb\nc\nd\n", sub(b"b\n", b"B\n"), sub(b"c\n", b"C\n"), {}),
    "native.txt": (lines(12), sub(b"line 6", b"line six"), sub(b"line 7", b"line seven"), {"svn:eol-style": "native"}),
    "native-lf-on-disk.txt": (lines(12), sub(b"line 6", b"line six"), lambda t: sub(b"line 7", b"line seven")(t.replace(b"\r\n", b"\n")), {"svn:eol-style": "native"}),
    "lf-style.txt": (lines(12), sub(b"line 6", b"line six"), sub(b"line 7", b"line seven"), {"svn:eol-style": "LF"}),
    "crlf-style.txt": (lines(12), sub(b"line 6", b"line six"), sub(b"line 7", b"line seven"), {"svn:eol-style": "CRLF"}),
    "cr-style.txt": (lines(12), sub(b"line 6", b"line six"), sub(b"line 7", b"line seven"), {"svn:eol-style": "CR"}),
    "text-mime.txt": (lines(12), sub(b"line 6", b"line six"), sub(b"line 7", b"line seven"), {"svn:mime-type": "text/plain"}),
    "empty.txt": (b"", lambda _: lines(2), lambda _: lines(1), {}),
    "becomes-empty.txt": (lines(3), lambda _: b"", lambda _: lines(2), {}),
    "no-eol.txt": (b"one\ntwo\nthree", lambda _: b"one\ntwo\nTHREE", lambda _: b"one\ntwo\nTHREE\nfour", {}),
    "gains-no-eol.txt": (b"one\ntwo\nthree\n", lambda _: b"one\ntwo\nthree", lambda _: b"one\ntwo\nthree\n", {}),
    "no-eol-context.txt": (b"a\nb\nc\nd", lambda _: b"a\nB\nc\nd", lambda _: b"A\nB\nc\nd", {}),
    "ambiguous.txt": (b"a\nb\nc\na\nb\nc\n", lambda t: t + b"x\n", lambda _: b"a\nb\nc\nx\na\nb\nc\na\nb\nc\nx\n", {}),
    "swap.txt": (b"1\n2\n3\n4\n5\n6\n7\n8\n", lambda _: b"5\n6\n7\n8\n1\n2\n3\n4\n", lambda _: b"1\n2\n3\n4\n5\n6\n7\n8\n", {}),
    # Two minimal diffs each, where a tie broken the other way from svn picks the other one.
    "tie-insertion.txt": (b"\n}\na\n{\nx0\n\nx3\n{\n{\n\n}\na\n\n{\n", lambda t: t, lambda _: b"\n}\na\nx0\n\nx3\n{\n{\n}\n\na\n\n{\n", {}),
    "tie-deletion.txt": (b"a\n{\na\n\n\n\n}\na\n", lambda t: t, lambda _: b"a\na\n{\n}\n\n\n}\na\n", {}),
}


def run(*args, cwd=WC):
    return subprocess.run(args, cwd=cwd, env=ENV, check=True, capture_output=True).stdout


def unlock(func, path, _):
    os.chmod(path, 0o666)
    func(path)


def write(name, data):
    with open(os.path.join(WC, name), "wb") as f:
        f.write(data)


def read(name):
    with open(os.path.join(WC, name), "rb") as f:
        return f.read()


def keep(name, data):
    with open(os.path.join(HERE, name), "wb") as f:
        f.write(data)


if os.path.exists(BASE):
    shutil.rmtree(BASE, onerror=unlock)
os.makedirs(BASE)
run("svnadmin", "create", REPO, cwd=BASE)
run("svn", "checkout", "-q", URL, WC, cwd=BASE)
for name, (base, _, _, _) in CASES.items():
    write(name, base)
run("svn", "add", "-q", *CASES)
for name, (_, _, _, props) in CASES.items():
    for prop, value in props.items():
        run("svn", "propset", "-q", prop, value, name)
run("svn", "commit", "-q", "-m", "r1")
run("svn", "update", "-q")
for name, (_, committed, _, _) in CASES.items():
    write(name, committed(read(name)))
run("svn", "commit", "-q", "-m", "r2")
run("svn", "update", "-q")
for name, (_, _, local, _) in CASES.items():
    write(name, local(read(name)))

db = sqlite3.connect("file:" + os.path.join(WC, ".svn", "wc.db") + "?mode=ro", uri=True)
for name in CASES:
    sha1 = db.execute(
        "select checksum from nodes where local_relpath = ? and op_depth = 0", (name,)
    ).fetchone()[0].split("$")[-1]
    with open(os.path.join(WC, ".svn", "pristine", sha1[:2], sha1 + ".svn-base"), "rb") as f:
        keep(name + ".base", f.read())
    keep(name + ".working", read(name))
    keep(name + ".diff", run("svn", "diff", name))
    keep(name + ".r1", run("svn", "cat", "-r", "1", f"{URL}/{name}@2"))
    keep(name + ".r2", run("svn", "cat", "-r", "2", f"{URL}/{name}@2"))
    keep(name + ".rev.diff", run("svn", "diff", "-c", "2", f"{URL}/{name}@2"))
db.close()
print(len(CASES), "cases captured with", run("svn", "--version", "--quiet").decode().strip())
