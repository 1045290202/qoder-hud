import { execFileSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.dirname(fileURLToPath(import.meta.url));
const args = ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path.join(root, "install.ps1")];
for (const arg of process.argv.slice(2)) {
  if (arg === "--remove") args.push("-Uninstall");
  else if (arg === "--autostart") args.push("-Autostart");
  else args.push(arg);
}
if (process.env.QODER_HUD_SETTINGS) args.push("-SettingsPath", process.env.QODER_HUD_SETTINGS);
if (process.env.QODER_HUD_NO_START) args.push("-NoStart");

execFileSync("powershell.exe", args, { stdio: "inherit" });
