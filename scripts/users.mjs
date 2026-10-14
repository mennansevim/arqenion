// Kullanıcı yönetimi (eğitmen hesabıyla).
//
//   node scripts/users.mjs list
//   node scripts/users.mjs add <kullanici-adi> [--name "Ad Soyad"] [--role trainee|instructor]   → şifre üretilir ve bir kez gösterilir
//   node scripts/users.mjs reset <kullanici-adi>                                               → yeni şifre üretilir
//   node scripts/users.mjs disable|enable <kullanici-adi>
//
// Giriş: --user/--pass, ARQ_USER/ARQ_PASS ya da yerelde backend/.env'deki ilk eğitmen hesabı. Sunucu: --url (varsayılan localhost:5080).

import { randomBytes } from "node:crypto";
import { login } from "./auth.mjs";

const argv = process.argv.slice(2);
const flag = (name, def) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : def; };
const URL_ = flag("url", "http://localhost:5080").replace(/\/$/, "");
const [cmd, username] = argv.filter((a, i) => !a.startsWith("--") && !argv[i - 1]?.startsWith("--"));
const token = await login(URL_, argv);

async function api(method, path, body) {
  const res = await fetch(`${URL_}${path}`, {
    method, headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  const data = text ? JSON.parse(text) : {};
  if (!res.ok) throw new Error(data.message || `HTTP ${res.status}`);
  return data;
}

// Okunabilir, 16 karakterlik şifre (karışabilen 0/O, 1/l yok).
const newPassword = () => [...randomBytes(16)].map((b) => "abcdefghjkmnpqrstuvwxyz23456789"[b % 31]).join("");
const find = async (name) => (await api("GET", "/api/auth/users")).find((u) => u.username === name)
  ?? (() => { throw new Error(`kullanıcı yok: ${name}`); })();

switch (cmd) {
  case "list":
    for (const u of await api("GET", "/api/auth/users"))
      console.log(`${u.isActive ? " " : "✕"} ${u.username.padEnd(24)} ${u.role.padEnd(10)} ${u.name}`);
    break;
  case "add": {
    const password = newPassword();
    const u = await api("POST", "/api/auth/users", { username, password, name: flag("name"), role: flag("role", "trainee") });
    console.log(`açıldı: ${u.username} (${u.role})\nşifre: ${password}\nŞifreyi kişiye güvenli bir kanaldan iletin; tekrar gösterilmez.`);
    break;
  }
  case "reset": {
    const password = newPassword();
    await api("POST", `/api/auth/users/${(await find(username)).id}/password`, { password });
    console.log(`yeni şifre (${username}): ${password}`);
    break;
  }
  case "disable":
  case "enable":
    await api("POST", `/api/auth/users/${(await find(username)).id}/active?value=${cmd === "enable"}`);
    console.log(`${username}: ${cmd === "enable" ? "aktif" : "pasif"}`);
    break;
  default:
    console.log("kullanım: users.mjs list | add <ad> [--name …] [--role …] | reset <ad> | disable <ad> | enable <ad>");
    process.exit(2);
}
