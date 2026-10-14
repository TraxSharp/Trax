// Node 26's experimental global localStorage is unavailable (it needs --localstorage-file) and
// shadows jsdom's, so app code that reads localStorage throws in tests. Install a working
// in-memory Storage on globalThis for the test run.
function hasWorkingStorage(): boolean {
  try {
    return typeof globalThis.localStorage?.getItem === "function";
  } catch {
    return false;
  }
}

if (!hasWorkingStorage()) {
  const store = new Map<string, string>();
  const storage = {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => {
      store.set(key, String(value));
    },
    removeItem: (key: string) => {
      store.delete(key);
    },
    clear: () => store.clear(),
    key: (index: number) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size;
    },
  } as Storage;
  try {
    Object.defineProperty(globalThis, "localStorage", {
      value: storage,
      configurable: true,
      writable: true,
    });
  } catch {
    /* non-configurable global — nothing we can do */
  }
}
