"""Puts a working copy back into the fresh-checkout state: every file's mtime set to the value
wc.db recorded for it, so SVN's metadata fast path settles every node without hashing anything.

The inverse of tools/move-mtimes.ps1. Reads wc.db read-only and writes nothing but mtimes, so the
working copy's content and SVN's metadata are both left alone.

Usage: python tools/restore-mtimes.py <working-copy-path>
"""

import os
import sqlite3
import sys

wc = os.path.abspath(sys.argv[1])
db = os.path.join(wc, ".svn", "wc.db")

connection = sqlite3.connect(f"file:{db}?mode=ro", uri=True)
rows = connection.execute(
    """
    SELECT n.local_relpath, n.last_mod_time
    FROM nodes n
    WHERE n.kind = 'file'
      AND n.last_mod_time IS NOT NULL
      AND n.last_mod_time > 0
      AND n.op_depth = (
            SELECT MAX(n2.op_depth) FROM nodes n2
            WHERE n2.wc_id = n.wc_id AND n2.local_relpath = n.local_relpath
      )
    """
).fetchall()
connection.close()

restored = 0
missing = 0
for relpath, apr_time in rows:
    path = os.path.join(wc, relpath.replace("/", os.sep))
    if not os.path.isfile(path):
        missing += 1
        continue

    # APR time is microseconds since the Unix epoch; os.utime takes nanoseconds.
    os.utime(path, ns=(apr_time * 1000, apr_time * 1000))
    restored += 1

print(f"restored {restored} mtimes, {missing} paths absent, {len(rows)} rows recorded")
