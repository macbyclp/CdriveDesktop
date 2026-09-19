#!/usr/bin/env bash
# Kullanıcı düzeyinde kurulum (sudo gerekmez): uygulama menüsü girişi, "Birlikte aç" listesi
# ve Nautilus / Nemo sağ tık "Cdrive'a yükle" girişi.
set -euo pipefail
LINUX_DIR="$(cd "$(dirname "$0")/.." && pwd)"
LAUNCHER="$LINUX_DIR/cdrive-desktop.sh"
chmod +x "$LAUNCHER"

APPS="$HOME/.local/share/applications"
ICONS="$HOME/.local/share/icons/hicolor/150x150/apps"
mkdir -p "$APPS" "$ICONS"
cp "$LINUX_DIR/assets/icon.png" "$ICONS/cdrive-desktop.png"

cat > "$APPS/cdrive-desktop.desktop" <<DESK
[Desktop Entry]
Type=Application
Name=Cdrive
Comment=Kurumsal dosyalarınız, kendi sunucunuzda
Exec="$LAUNCHER" %F
Icon=cdrive-desktop
Terminal=false
Categories=Network;FileTransfer;
StartupWMClass=Cdrive
MimeType=application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;application/vnd.ms-excel;application/vnd.openxmlformats-officedocument.wordprocessingml.document;application/msword;application/vnd.openxmlformats-officedocument.presentationml.presentation;application/vnd.ms-powerpoint;application/pdf;text/csv;text/plain;
Actions=upload;

[Desktop Action upload]
Name=Cdrive'a yükle
Exec="$LAUNCHER" --upload %F
DESK

# Nautilus (GNOME / Zorin): sağ tık → Komut Dosyaları → Cdrive'a yükle
NAUT="$HOME/.local/share/nautilus/scripts"
mkdir -p "$NAUT"
cat > "$NAUT/Cdrive'a yükle" <<NS
#!/usr/bin/env bash
# Nautilus seçili dosyaları satır satır NAUTILUS_SCRIPT_SELECTED_FILE_PATHS ile verir.
mapfile -t FILES <<< "\$NAUTILUS_SCRIPT_SELECTED_FILE_PATHS"
[ \${#FILES[@]} -gt 0 ] && exec "$LAUNCHER" --upload "\${FILES[@]}"
NS
chmod +x "$NAUT/Cdrive'a yükle"

# Nemo (Cinnamon): sağ tık doğrudan "Cdrive'a yükle"
NEMO="$HOME/.local/share/nemo/actions"
mkdir -p "$NEMO"
cat > "$NEMO/cdrive-upload.nemo_action" <<NM
[Nemo Action]
Name=Cdrive'a yükle
Comment=Seçili dosyaları Cdrive'a yükle
Exec="$LAUNCHER" --upload %F
Icon-Name=cdrive-desktop
Selection=notnone
Extensions=any;
NM

update-desktop-database "$APPS" 2>/dev/null || true
echo "Kuruldu. Uygulama menüsünde 'Cdrive' görünür; dosya yöneticisinde sağ tık → Cdrive'a yükle."
