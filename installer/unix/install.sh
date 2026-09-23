#!/bin/sh
# Installs this bundle for the current user and puts `sv` and `subverted` on PATH.
#   ./install.sh               install, or upgrade in place
#   ./install.sh --uninstall   remove everything this script added
# SUBVERTED_PREFIX overrides where it goes (default ~/.local).
set -eu

prefix="${SUBVERTED_PREFIX:-$HOME/.local}"
app_dir="$prefix/share/subverted"
bin_dir="$prefix/bin"
desktop_file="$HOME/.local/share/applications/subverted.desktop"
profile_marker="# added by the Subverted installer"

case "$(basename "${SHELL:-sh}")" in
  zsh) profile="$HOME/.zprofile" ;;
  bash) profile="$HOME/.bashrc" ;;
  *) profile="$HOME/.profile" ;;
esac

stop_daemon() {
  if [ -x "$app_dir/sv" ]; then
    "$app_dir/sv" daemon stop > /dev/null 2>&1 || true
  fi
}

# A wrapper rather than a symlink: the front-ends find the daemon beside their own real path.
write_wrapper() {
  cat > "$bin_dir/$1" << EOF
#!/bin/sh
exec "$app_dir/$2" "\$@"
EOF
  chmod +x "$bin_dir/$1"
}

remove_profile_line() {
  [ -f "$profile" ] || return 0
  grep -vF "$profile_marker" "$profile" > "$profile.subverted-tmp" || true
  mv "$profile.subverted-tmp" "$profile"
}

uninstall() {
  stop_daemon
  rm -rf "$app_dir"
  rm -f "$bin_dir/sv" "$bin_dir/subverted" "$desktop_file"
  remove_profile_line
  echo "Subverted removed."
}

install() {
  source_dir="$(cd "$(dirname "$0")" && pwd)"
  if [ "$source_dir" = "$(cd "$app_dir" 2> /dev/null && pwd || true)" ]; then
    echo "Run this from the unpacked archive, not from $app_dir." >&2
    exit 1
  fi

  stop_daemon
  rm -rf "$app_dir"
  mkdir -p "$app_dir" "$bin_dir"
  cp -R "$source_dir/." "$app_dir/"

  write_wrapper sv sv
  write_wrapper subverted Subverted

  if [ "$(uname -s)" = "Linux" ]; then
    mkdir -p "$(dirname "$desktop_file")"
    cat > "$desktop_file" << EOF
[Desktop Entry]
Type=Application
Name=Subverted
Comment=A Subversion client
Exec=$app_dir/Subverted
Terminal=false
Categories=Development;RevisionControl;
EOF
  fi

  case ":$PATH:" in
    *":$bin_dir:"*) ;;
    *)
      if ! grep -qF "$profile_marker" "$profile" 2> /dev/null; then
        echo "export PATH=\"$bin_dir:\$PATH\" $profile_marker" >> "$profile"
      fi
      echo "Added $bin_dir to PATH in $profile — open a new terminal to pick it up."
      ;;
  esac

  if ! command -v svn > /dev/null 2>&1; then
    echo "Note: svn is not on PATH. Everything but 'sv st' needs it." >&2
  fi
  echo "Subverted installed to $app_dir."
}

case "${1:-}" in
  --uninstall) uninstall ;;
  "") install ;;
  *)
    echo "usage: $0 [--uninstall]" >&2
    exit 2
    ;;
esac
