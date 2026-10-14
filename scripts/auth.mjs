// Betikler için giriş: --user/--pass, ARQ_USER/ARQ_PASS ya da (yalnız yerel geliştirmede) backend/.env'deki ilk eğitmen hesabı.
import { readFile } from "node:fs/promises";

export async function credentials(argv) {
  const flag = (name) => { const i = argv.indexOf(`--${name}`); return i >= 0 ? argv[i + 1] : undefined; };
  let user = flag("user") ?? process.env.ARQ_USER, pass = flag("pass") ?? process.env.ARQ_PASS;
  if (!user || !pass) {
    try {
      const env = await readFile(new URL("../backend/.env", import.meta.url), "utf8");
      const get = (k) => env.match(new RegExp(`^${k}=(.*)$`, "m"))?.[1]?.trim().replace(/^"|"$/g, "");
      user ??= get("Auth__BootstrapAdmin__Username");
      pass ??= get("Auth__BootstrapAdmin__Password");
    } catch { /* .env yok */ }
  }
  if (!user || !pass) throw new Error("Giriş bilgisi yok: --user/--pass ya da ARQ_USER/ARQ_PASS verin.");
  return { user, pass };
}

export async function login(url, argv) {
  const { user, pass } = await credentials(argv);
  const res = await fetch(`${url}/api/auth/login`, {
    method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ username: user, password: pass }),
  });
  if (!res.ok) throw new Error(`giriş başarısız (${res.status}): ${await res.text()}`);
  return (await res.json()).token;
}
