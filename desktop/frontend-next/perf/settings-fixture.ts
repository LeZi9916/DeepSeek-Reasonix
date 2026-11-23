import { MockPort } from "../src/port/mock";
import type { AccountState, DeviceGrant } from "../src/port/port";

export class SettingsBenchPort extends MockPort {
  constructor(private readonly query: URLSearchParams) {
    super();
  }

  async account(): Promise<AccountState> {
    const sample = this.query.get("accountview");
    if (sample === "unread") throw new Error("身份服务暂不可用");
    if (sample === "signed-in" || sample === "offline") {
      return {
        signedIn: true,
        user: { label: "demo", email: "demo@example.com", handle: "demo" },
        ...(sample === "offline" ? { error: "身份服务暂不可用" } : {}),
      };
    }
    if (sample) return { signedIn: false, expired: sample === "expired" };
    const state = await super.account();
    const label = this.query.get("accountLabel");
    return label && state.signedIn && state.user ? { ...state, user: { ...state.user, label } } : state;
  }

  async accountLogin(): Promise<DeviceGrant> {
    if (this.query.get("accountview") !== "code") return super.accountLogin();
    const verificationUri = "https://example.test/device";
    return { deviceCode: "fixture-device", userCode: "W7XQ-4TDP", verificationUri, verificationUriComplete: verificationUri, interval: 3, expiresIn: 300 };
  }

  async openExternal(url: string) {
    if (this.query.get("accountview") === "code" && url === "https://example.test/device") return;
    await super.openExternal(url);
  }

  async observabilitySettings() {
    const settings = await super.observabilitySettings();
    return this.query.get("traceview") === "configured" ? { ...settings, enabled: true, endpoint: "http://localhost:4318" } : settings;
  }
}
